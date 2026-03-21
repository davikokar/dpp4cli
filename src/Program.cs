// dpp4cli - Converts Canon RAW files to JPEG using a recipe (.dr4)
// using the DppMWare.dll engine from Canon DPP4.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DPP4Cli
{
    internal static class Program
    {
        private const int ExitOk    = 0;
        private const int ExitError = 1;

        private static int _exitCode = ExitOk;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static void ReleaseConsole()
        {
            Console.Out.Flush();
            Console.Error.Flush();
            FreeConsole();
            // Inject a return in the buffer of the shell to resume the prompt
            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
        }

        // Path to the DPP4 installation folder.
        // Loaded from config file; can be overridden via --dpp4dir.
        internal static string Dpp4InstallDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Canon\Digital Photo Professional 4");

        [STAThread]
        private static void Main(string[] args)
        {
            AttachConsole(AttachParentProcess);
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // --- Parse arguments ---
            CliOptions opts;
            try
            {
                opts = CliOptions.Parse(args);
            }
            catch (CliUsageException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.Error.WriteLine();
                CliOptions.PrintUsage();
                ReleaseConsole();
                Environment.Exit(ExitError);
                return;
            }

            if (opts.ShowHelp)
            {
                CliOptions.PrintUsage();
                ReleaseConsole();
                Environment.Exit(ExitOk);
                return;
            }

            // --- Read the config file, then apply override from CLI ---
            LoadConfig();
            if (!string.IsNullOrEmpty(opts.Dpp4Dir))
                Dpp4InstallDir = opts.Dpp4Dir;

            // Adds the DPP4 folder to the process PATH before loading the Canon
            // DLLs (DppMWare.dll and its native dependencies).
            PrependDpp4DirToPath();

            // --- Validate inputs ---
            if (!File.Exists(opts.RecipeFile))
            {
                Console.Error.WriteLine("Error: recipe file not found: " + opts.RecipeFile);
                ReleaseConsole();
                Environment.Exit(ExitError);
                return;
            }

            bool anyMissing = false;
            foreach (string raw in opts.RawFiles)
            {
                if (!File.Exists(raw))
                {
                    Console.Error.WriteLine("Error: RAW file not found: " + raw);
                    anyMissing = true;
                }
            }
            if (anyMissing)
            {
                ReleaseConsole();
                Environment.Exit(ExitError);
                return;
            }

            // --- Verbose summary ---
            if (opts.Verbose)
            {
                Log($"Files   : {opts.RawFiles.Length}");
                Log("Recipe  : " + opts.RecipeFile);
                Log("Out dir : " + opts.OutputDir);
                Log("Suffix  : " + (opts.Suffix.Length > 0 ? opts.Suffix : "(none)"));
                Log("Quality : " + opts.JpegQuality);
                Log("DPP4    : " + Dpp4InstallDir);
            }

            var staReady = new ManualResetEventSlim(false);
            var staThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                staReady.Set();
                Application.Run();
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Name = "DPP4-STA-Pump";
            staThread.IsBackground = true;
            staThread.Start();
            staReady.Wait();

            // --- Start conversion on background thread ---
            var converter = new Converter(opts);

            try
            {
                int failures = converter.Convert(opts.RecipeFile);
                _exitCode = failures == 0 ? ExitOk : ExitError;
            }
            catch (ConversionException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                _exitCode = ExitError;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error: " + ex.Message);
                if (opts.Verbose)
                    Console.Error.WriteLine(ex.ToString());
                _exitCode = ExitError;
            }
            finally
            {
                Application.Exit();
                ReleaseConsole();
                Environment.Exit(_exitCode);
            }
        }

        internal static void Log(string msg)
        {
            Console.WriteLine("[dpp4cli] " + msg);
        }

        // ------------------------------------------------------------------
        //  Config file
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads dpp4cli.config from the same folder as the executable.
        /// Format: one entry per line, "key=value".
        /// Empty lines and lines starting with # are ignored.
        ///
        /// Example dpp4cli.config:
        ///   # Path to Canon DPP4 installation
        ///   dpp4dir=C:\Program Files\Canon\Digital Photo Professional 4
        /// </summary>
        private static void LoadConfig()
        {
            string exeDir  = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string cfgPath = Path.Combine(exeDir, "dpp4cli.config");

            if (!File.Exists(cfgPath))
                return;

            foreach (string rawLine in File.ReadAllLines(cfgPath, System.Text.Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 1) continue;

                string key   = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "dpp4dir":
                        if (!string.IsNullOrEmpty(value))
                            Dpp4InstallDir = value;
                        break;
                    // Space for future configuration keys
                }
            }
        }

        // ------------------------------------------------------------------
        //  Process PATH
        // ------------------------------------------------------------------

        private static void PrependDpp4DirToPath()
        {
            if (!Directory.Exists(Dpp4InstallDir))
            {
                Console.Error.WriteLine(
                    "DPP4 folder not found: " + Dpp4InstallDir);
                Console.Error.WriteLine(
                    "Set 'dpp4dir' in dpp4cli.config or use --dpp4dir.");
                return;
            }

            string current = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (current.IndexOf(Dpp4InstallDir, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Environment.SetEnvironmentVariable(
                    "PATH",
                    Dpp4InstallDir + Path.PathSeparator + current,
                    EnvironmentVariableTarget.Process);
            }
        }
    }

    internal sealed class ConversionException : Exception
    {
        public ConversionException(string msg) : base(msg) { }
        public ConversionException(string msg, Exception inner) : base(msg, inner) { }
    }
}
