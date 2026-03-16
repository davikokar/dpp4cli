// dpp4cli - Converts Canon RAW files to JPEG using a recipe (.dr4)
// usaing the DppMWare.dll engine from Canon DPP4.

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
                Console.Error.WriteLine("Errore: " + ex.Message);
                Console.Error.WriteLine();
                CliOptions.PrintUsage();
                Environment.Exit(ExitError);
                return;
            }

            if (opts.ShowHelp)
            {
                CliOptions.PrintUsage();
                Environment.Exit(ExitOk);
                return;
            }

            // --- Reads the config file, then apply override from CLI
            LoadConfig();
            if (!string.IsNullOrEmpty(opts.Dpp4Dir))
                Dpp4InstallDir = opts.Dpp4Dir;

            // Adds the DPP4 folder to the process PATH before
            // loading the Canon DLLs (DppMWare.dll and its native dependencies).
            PrependDpp4DirToPath();

            // --- Validations ---
            if (!File.Exists(opts.RawFile))
            {
                Console.Error.WriteLine("Error: RAW file not found: " + opts.RawFile);
                Environment.Exit(ExitError);
                return;
            }
            if (!File.Exists(opts.RecipeFile))
            {
                Console.Error.WriteLine("Error: recipe file not found: " + opts.RecipeFile);
                Environment.Exit(ExitError);
                return;
            }
            if (File.Exists(opts.OutputFile))
            {
                Console.Error.WriteLine("Error: output file already exists: " + opts.OutputFile);
                Console.Error.WriteLine("Rename it or delete it before proceeding.");
                Environment.Exit(ExitError);
                return;
            }

            if (opts.Verbose)
            {
                Log("RAW    : " + opts.RawFile);
                Log("Recipe : " + opts.RecipeFile);
                Log("Output : " + opts.OutputFile);
                Log("Quality: " + opts.JpegQuality);
                Log("DPP4   : " + Dpp4InstallDir);
            }

            // --- Start conversion on background thread ---
            var converter = new Converter(opts);

            var bgThread = new Thread(() =>
            {
                try
                {
                    converter.Convert(opts.RawFile, opts.RecipeFile, opts.OutputFile);
                    Console.WriteLine("OK: " + opts.OutputFile);
                    _exitCode = ExitOk;
                }
                catch (ConversionException ex)
                {
                    Console.Error.WriteLine("Error: " + ex.Message);
                    _exitCode = ExitError;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Unexpected errrore: " + ex.Message);
                    if (opts.Verbose)
                        Console.Error.WriteLine(ex.ToString());
                    _exitCode = ExitError;
                }
                finally
                {
                    Application.Exit();
                }
            });

            bgThread.Name = "DPP4-Converter";
            bgThread.SetApartmentState(ApartmentState.MTA);
            bgThread.Start();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run();

            Environment.Exit(_exitCode);
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
        /// Format: one raw per configuration, "key=value".
        /// Empty rows and rows starting with # are ignored.
        ///
        /// Example of dpp4cli.config:
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
                        // space for future configuration keys
                }
            }
        }

        // ------------------------------------------------------------------
        //  process PATH
        // ------------------------------------------------------------------

        private static void PrependDpp4DirToPath()
        {
            if (!Directory.Exists(Dpp4InstallDir))
            {
                Console.Error.WriteLine(
                    "Folder DPP4 not found: " + Dpp4InstallDir);
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
