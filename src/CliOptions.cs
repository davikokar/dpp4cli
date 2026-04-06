using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DPP4Cli
{

    /// <summary>
    /// RAW file extensions recognised by DPP4.
    /// JPEG and TIFF are intentionally excluded: they are valid DPP4 inputs
    /// but are not typical candidates for RAW development.
    /// </summary>
    internal static class RawExtensions
    {
        public static readonly HashSet<string> All = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ".CR3",
            ".CR2",
            ".TIF",
            ".CRW",
            ".CIP",
            ".CRN",
        };
    }

    internal enum OutputFormat
    {
        Jpeg,
        Tiff8,
        Tiff16,
        Tiff8AndJpeg,
        Tiff16AndJpeg
    }
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

        // --- Output Format (JPG, TIFF or combination) ---
        public OutputFormat Format { get; private set; } = OutputFormat.Jpeg;

        // --- Diagnostics ---
        public bool Verbose  { get; private set; }
        public bool ShowHelp { get; private set; }

        // ------------------------------------------------------------------

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();
            var rawFiles = new List<string>();
            string inputDir = null;
            bool recursive = false;

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

                    case "-o": case "--outputdir":
                        o.OutputDir = Next(args, ref i, "--outputdir");
                        break;

                    case "-i": case "--inputdir":
                        inputDir = Next(args, ref i, "--inputdir");
                        break;

                    case "--recursive":
                        recursive = true;
                        break;

                    case "-s": case "--suffix":
                        o.Suffix = Next(args, ref i, "--suffix");
                        break;

                    case "-f": case "--format":
                        {
                            string v = Next(args, ref i, "--format").ToLowerInvariant();
                            switch (v)
                            {
                                case "jpg": o.Format = OutputFormat.Jpeg; break;
                                case "tiff8": o.Format = OutputFormat.Tiff8; break;
                                case "tiff16": o.Format = OutputFormat.Tiff16; break;
                                case "tiff8+jpg": o.Format = OutputFormat.Tiff8AndJpeg; break;
                                case "jpg+tiff8": o.Format = OutputFormat.Tiff8AndJpeg; break;
                                case "tiff16+jpg": o.Format = OutputFormat.Tiff16AndJpeg; break;
                                case "jpg+tiff16": o.Format = OutputFormat.Tiff16AndJpeg; break;
                                default:
                                    throw new CliUsageException(
                                        $"Unknown format '{v}'. " +
                                        "Valid values: jpg, tiff8, tiff16, tiff8+jpg, tiff16+jpg");
                            }
                            break;
                        }

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

                bool hasPositional = rawFiles.Count > 0;
                bool hasInputFolder = inputDir != null;

                if (!hasPositional && !hasInputFolder)
                    throw new CliUsageException(
                        "No input specified. Provide RAW files as arguments or use --inputDir.");

                if (hasPositional && hasInputFolder)
                    throw new CliUsageException(
                        "--inputDir cannot be combined with positional RAW file arguments. " +
                        "Use one or the other.");

                if (hasInputFolder)
                {
                    inputDir = Path.GetFullPath(inputDir);
                    if (!Directory.Exists(inputDir))
                        throw new CliUsageException($"Input folder not found: {inputDir}");

                    rawFiles = ScanFolder(inputDir, recursive);

                    if (rawFiles.Count == 0)
                        throw new CliUsageException(
                            $"No RAW files found in '{inputDir}'" +
                            (recursive ? " (including subfolders)." : ". Use --recursive to include subfolders."));
                }
                else
                {
                    // Resolve positional paths to absolute
                    for (int i = 0; i < rawFiles.Count; i++)
                        rawFiles[i] = Path.GetFullPath(rawFiles[i]);
                }

                o.RawFiles   = rawFiles.ToArray();
                o.RecipeFile = Path.GetFullPath(o.RecipeFile);
                o.OutputDir  = Path.GetFullPath(o.OutputDir);
                if (o.Dpp4Dir != null) o.Dpp4Dir = Path.GetFullPath(o.Dpp4Dir);
            }

            return o;
        }

        /// <summary>
        /// Scans a folder for RAW files. Returns absolute paths sorted alphabetically.
        /// </summary>
        private static List<string> ScanFolder(string folder, bool recursive)
        {
            var option = recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            return Directory
                .EnumerateFiles(folder, "*.*", option)
                .Where(f => RawExtensions.All.Contains(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns one or two output paths for a given RAW file, depending on the format.
        /// Dual formats (tiff8+jpg, tiff16+jpg) return two paths.
        /// Item1 = primary type path, Item2 = secondary type path (or null).
        /// </summary>
        public (string primary, string secondary) BuildOutputPaths(string rawFile)
        {
            string baseName = Path.GetFileNameWithoutExtension(rawFile) + Suffix;
            string inDir = OutputDir;

            switch (Format)
            {
                case OutputFormat.Jpeg:
                    return (Path.Combine(inDir, baseName + ".jpg"), null);

                case OutputFormat.Tiff8:
                case OutputFormat.Tiff16:
                    return (Path.Combine(inDir, baseName + ".tif"), null);

                case OutputFormat.Tiff8AndJpeg:
                case OutputFormat.Tiff16AndJpeg:
                    return (
                        Path.Combine(inDir, baseName + ".tif"),
                        Path.Combine(inDir, baseName + ".jpg")
                    );

                default:
                    throw new InvalidOperationException("Unknown format: " + Format);
            }
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
  dpp4cli --recipe <file.dr4> --outputdir <folder> [options] file1.CR3 file2.CR3 ...
  dpp4cli --recipe <file.dr4> --outputdir <folder> --inputdir <folder> [options]

INPUT (one of the two is required):
  <file1.CR3> ...            One or more RAW files passed as arguments
  --inputdir / -i <path>     Folder containing RAW files to process
                             (cannot be combined with positional file arguments)

REQUIRED:
  --outputdir / -o <folder>  Output folder (created automatically if it does not exist)
  <file1.CR3> ...            One or more Canon RAW files (.CR2, .CR3, .CRW, .CRF, ...)

OPTIONS:
  --recipe / -p <path>       DPP4 recipe file (.dr4) with the development parameters
                             If not specified, dpp4cli looks for dpp4_recipe_default.dr4 in the executable folder.
                             If that is also missing, an error is raised.
  --format / -f <fmt>        Output format (default: jpg)
                               jpg         JPEG
                               tiff8       TIFF 8-bit
                               tiff16      TIFF 16-bit
                               tiff8+jpg   TIFF 8-bit + JPEG (two files per RAW)
                               tiff16+jpg  TIFF 16-bit + JPEG (two files per RAW)
  --suffix / -s <text>       Suffix inserted between the base name and .jpg
                             e.g. --suffix _edit  ->  IMG_001.CR3 becomes IMG_001_edit.jpg
  --quality / -q <1-100>     JPEG quality (default: 100)
  --dpp4dir <folder>         Override the DPP4 installation path from the config file
  --verbose / -v             Detailed diagnostic logging
  --help    / -h             Show this message

EXAMPLES:
  :: Single file
  dpp4cli --recipe portrait.dr4 --outputdir C:\export IMG_001.CR3

  :: Multiple files
  dpp4cli --recipe portrait.dr4 --outputdir C:\export IMG_001.CR3 IMG_002.CR3 IMG_003.CR3

  :: With suffix and quality
  dpp4cli --recipe params.dr4 --outputdir C:\export --suffix _edit --quality 95 *.CR3

  :: PowerShell glob
  dpp4cli --recipe params.dr4 --outputdir C:\export (Get-Item C:\raw\*.CR3)

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
