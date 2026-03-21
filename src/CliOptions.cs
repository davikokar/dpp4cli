using System;
using System.Collections.Generic;
using System.IO;

namespace DPP4Cli
{
    internal sealed class CliOptions
    {
        // --- Required ---
        public string[] RawFiles   { get; private set; }  // one or more RAW files
        public string   RecipeFile { get; private set; }
        public string   OutputDir  { get; private set; }

        // --- Optional ---
        /// <summary>
        /// Optional suffix inserted between the base name and the .jpg extension.
        /// e.g. suffix "_edit" turns IMG_001.CR3 into IMG_001_edit.jpg
        /// </summary>
        public string Suffix { get; private set; } = "";

        // --- JPEG parameter ---
        public int JpegQuality { get; private set; } = 100;

        // --- DPP4 path (overrides config file) ---
        public string Dpp4Dir { get; private set; }

        // --- Diagnostics ---
        public bool Verbose  { get; private set; }
        public bool ShowHelp { get; private set; }

        // ------------------------------------------------------------------

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            var rawFiles = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-h": case "--help":
                        o.ShowHelp = true;
                        return o;

                    case "-v": case "--verbose":
                        o.Verbose = true;
                        break;

                    case "-p": case "--recipe":
                        o.RecipeFile = Next(args, ref i, "--recipe");
                        break;

                    case "-d": case "--outdir":
                        o.OutputDir = Next(args, ref i, "--outdir");
                        break;

                    case "-s": case "--suffix":
                        o.Suffix = Next(args, ref i, "--suffix");
                        break;

                    case "-q": case "--quality":
                    {
                        string v = Next(args, ref i, "--quality");
                        if (!int.TryParse(v, out int q) || q < 1 || q > 100)
                            throw new CliUsageException(
                                $"--quality must be an integer between 1 and 100, got: {v}");
                        o.JpegQuality = q;
                        break;
                    }

                    case "--dpp4dir":
                        o.Dpp4Dir = Next(args, ref i, "--dpp4dir");
                        break;

                    default:
                        // Every unrecognised positional argument is treated as a RAW file
                        if (args[i].StartsWith("-"))
                            throw new CliUsageException($"Unknown option: {args[i]}");
                        rawFiles.Add(args[i]);
                        break;
                }
            }

            if (!o.ShowHelp)
            {
                if (rawFiles.Count == 0) throw new CliUsageException("At least one RAW file is required.");
                if (o.RecipeFile == null)
                {
                    var defaultRecipe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dpp4_recipe_default.dr4");
                    if (File.Exists(defaultRecipe))
                    {
                        o.RecipeFile = defaultRecipe;
                        Console.WriteLine($"[dpp4cli] No recipe specified, using default: {defaultRecipe}");
                    }
                    else
                        throw new CliUsageException("Recipe file missing (--recipe).");
                }
                if (o.OutputDir  == null) throw new CliUsageException("Output folder missing (--outdir).");

                // Resolve to absolute paths
                for (int i = 0; i < rawFiles.Count; i++)
                    rawFiles[i] = Path.GetFullPath(rawFiles[i]);

                o.RawFiles   = rawFiles.ToArray();
                o.RecipeFile = Path.GetFullPath(o.RecipeFile);
                o.OutputDir  = Path.GetFullPath(o.OutputDir);
                if (o.Dpp4Dir != null) o.Dpp4Dir = Path.GetFullPath(o.Dpp4Dir);
            }

            return o;
        }

        /// <summary>
        /// Builds the output JPEG path for a given RAW file.
        /// e.g. IMG_001.CR3  ->  <OutputDir>\IMG_001<Suffix>.jpg
        /// </summary>
        public string BuildOutputPath(string rawFile)
        {
            string baseName = Path.GetFileNameWithoutExtension(rawFile);
            return Path.Combine(OutputDir, baseName + Suffix + ".jpg");
        }

        private static string Next(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
                throw new CliUsageException($"The value after {flag} is missing.");
            return args[++i];
        }

        public static void PrintUsage()
        {
            Console.WriteLine(@"
dpp4cli - Converts Canon RAW files to JPEG applying a DPP4 recipe

USAGE:
  dpp4cli --recipe <file.dr4> --outdir <folder> [options] file1.CR3 file2.CR3 ...

REQUIRED:
  --recipe / -p <path>       DPP4 recipe file (.dr4) with the development parameters
  --outdir / -d <folder>     Output folder (created automatically if it does not exist)
  <file1.CR3> ...            One or more Canon RAW files (.CR2, .CR3, .CRW, .CRF, ...)

OPTIONS:
  --suffix / -s <text>       Suffix inserted between the base name and .jpg
                             e.g. --suffix _edit  ->  IMG_001.CR3 becomes IMG_001_edit.jpg
  --quality / -q <1-100>     JPEG quality (default: 100)
  --dpp4dir <folder>         Override the DPP4 installation path from the config file
  --verbose / -v             Detailed diagnostic logging
  --help    / -h             Show this message

EXAMPLES:
  :: Single file
  dpp4cli --recipe portrait.dr4 --outdir C:\export IMG_001.CR3

  :: Multiple files
  dpp4cli --recipe portrait.dr4 --outdir C:\export IMG_001.CR3 IMG_002.CR3 IMG_003.CR3

  :: With suffix and quality
  dpp4cli --recipe params.dr4 --outdir C:\export --suffix _edit --quality 95 *.CR3

  :: PowerShell glob
  dpp4cli --recipe params.dr4 --outdir C:\export (Get-Item C:\raw\*.CR3)

CONFIG FILE:
  Place dpp4cli.config in the same folder as the executable:

    dpp4dir=C:\Program Files\Canon\Digital Photo Professional 4

  --dpp4dir takes priority over the config file.

HOW TO OBTAIN A RECIPE FILE (.dr4):
  In DPP4: adjust the development parameters, then
  Edit -> Save recipe to file  (Ctrl+Shift+C)
");
        }
    }

    internal sealed class CliUsageException : Exception
    {
        public CliUsageException(string msg) : base(msg) { }
    }
}
