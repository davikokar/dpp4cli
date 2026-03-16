using System;
using System.IO;

namespace DPP4Cli
{
    internal sealed class CliOptions
    {
        // --- Required (positional) ---
        public string RawFile    { get; private set; }
        public string RecipeFile { get; private set; }
        public string OutputFile { get; private set; }

        // --- JPEG Parameter ---
        public int    JpegQuality { get; private set; } = 100;

        // --- Path DPP4 (override of config file) ---
        public string Dpp4Dir { get; private set; }

        // --- Diagnostics ---
        public bool Verbose  { get; private set; }
        public bool ShowHelp { get; private set; }

        // ------------------------------------------------------------------

        public static CliOptions Parse(string[] args)
        {
            var o = new CliOptions();

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

                    case "-r": case "--raw":
                        o.RawFile = Next(args, ref i, "--raw");
                        break;

                    case "-p": case "--recipe":
                        o.RecipeFile = Next(args, ref i, "--recipe");
                        break;

                    case "-o": case "--output":
                        o.OutputFile = Next(args, ref i, "--output");
                        break;

                    case "-q": case "--quality":
                    {
                        string v = Next(args, ref i, "--quality");
                        if (!int.TryParse(v, out int q) || q < 1 || q > 100)
                            throw new CliUsageException(
                                "--quality deve essere un intero tra 1 e 100, ricevuto: " + v);
                        o.JpegQuality = q;
                        break;
                    }

                    case "--dpp4dir":
                        o.Dpp4Dir = Next(args, ref i, "--dpp4dir");
                        break;

                    default:
                        // Positional arguments: raw, recipe, output
                        if (o.RawFile    == null) o.RawFile    = args[i];
                        else if (o.RecipeFile == null) o.RecipeFile = args[i];
                        else if (o.OutputFile == null) o.OutputFile = args[i];
                        else throw new CliUsageException("Unexpected argument: " + args[i]);
                        break;
                }
            }

            if (!o.ShowHelp)
            {
                if (o.RawFile    == null) throw new CliUsageException("RAW file missing.");
                if (o.RecipeFile == null) throw new CliUsageException("Recipe file missing.");
                if (o.OutputFile == null) throw new CliUsageException("Output file missing.");

                o.RawFile    = Path.GetFullPath(o.RawFile);
                o.RecipeFile = Path.GetFullPath(o.RecipeFile);
                o.OutputFile = Path.GetFullPath(o.OutputFile);
                if (o.Dpp4Dir != null) o.Dpp4Dir = Path.GetFullPath(o.Dpp4Dir);
            }

            return o;
        }

        private static string Next(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
                throw new CliUsageException("The value after " + flag + " is missing.");
            return args[++i];
        }

        public static void PrintUsage()
        {
            Console.WriteLine(@"
dpp4cli - Converts Canon RAW files to JPEG applying a DPP4 recipe

USO:
  dpp4cli <raw> <recipe> <output> [options]

REQUIRED ARGUMENTS (positional):
  <raw>      Canon RAW File (.CR2, .CR3, .CRW, .CRF, ...)
  <recipe>   Recipe file DPP4 (.dr4) with parameters to apply
  <output>   Path of the JPG file to generate

OPTIONS:
  --raw    / -r <path>       Positional alternative for the RAW file
  --recipe / -p <path>       Positional alternative for the recipe file
  --output / -o <path>       Positional alternative for the output file
  --quality / -q <1-100>     JPEG Quality (default: 100)
  --dpp4dir <cartella>       Override of the DPP4 path
  --verbose / -v             Detailed diagnostics log
  --help    / -h             Show this message

EXAMPLES:
  dpp4cli foto.CR3 portrait.dr4 output.jpg
  dpp4cli foto.CR3 params.dr4 C:\export\result.jpg --quality 95
  dpp4cli --raw foto.CR3 --recipe params.dr4 --output result.jpg -v

CONFIG FILE:
  Add the dpp4cli.config in the same folder as the executable to set
  the path to the DPP4 installation:

    dpp4dir=C:\Program Files\Canon\Digital Photo Professional 4

  The optione --dpp4dir has priority over the config file.

HOW TO OBTAIN A RECIPE FILE (.dr4):
  In DPP4: adjust the parameters, then
  Edit -> Save recipe to file  (Ctrl+Shift+C)
");
        }
    }

    internal sealed class CliUsageException : Exception
    {
        public CliUsageException(string msg) : base(msg) { }
    }
}
