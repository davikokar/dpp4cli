// Converter.cs
//
// Multi-file conversion pipeline:
//
//  1. DPPMWare.DppMWInitialize()
//  2. DSFRecipe.Create() + recipe.ReadFileData(.dr4)
//  3. DSFBatchScript.Create()
//  4. For each RAW file: script.AddFile(src, dst, DppImageType.Jpeg, recipe)
//  5. script.SetJpegQuality() + script.SetExifInfoLevel(All)
//  6. script.SaveScriptFile(tempPath)
//  7. DSFBatchProcess.Create(tempPath)
//  8. process.Execute()           <- all files are processed in a single Execute()
//  9. Wait for NotifyBatchCompleted
// 10. DPPMWare.DppMWTerminate()
//
// All files share one DSFBatchScript, so DppMWare processes them in a single
// engine run — more efficient than calling Execute() once per file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Canon.Dpp.Common.Utility;
using Canon.Dpp.Dsf;
using Canon.Dpp.Dsf.Batch;
using Canon.Dpp.Dsf.Image;
using Canon.Dpp.Dsf.Utility;

namespace DPP4Cli
{
    internal sealed class Converter
    {
        private readonly CliOptions _opts;

        // 5 minutes per file, minimum 10 minutes total
        private static readonly TimeSpan TimeoutPerFile = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan MinTimeout = TimeSpan.FromMinutes(10);

        public Converter(CliOptions opts)
        {
            _opts = opts;
        }

        /// <summary>
        /// Converts all RAW files one at a time.
        /// Returns the number of files that failed (0 = full success).
        /// </summary>
        public int Convert(string recipeFile)
        {
            if (!Directory.Exists(_opts.OutputDir))
                Directory.CreateDirectory(_opts.OutputDir);

            var (primaryType, secondaryType) = GetImageTypes(_opts.Format);
            bool hasJpeg = _opts.Format == OutputFormat.Jpeg ||
                           _opts.Format == OutputFormat.Tiff8AndJpeg ||
                           _opts.Format == OutputFormat.Tiff16AndJpeg;

            int totalFiles = _opts.RawFiles.Length;
            int successCount = 0;
            int failureCount = 0;
            var failedFiles = new List<string>();

            DSF dsf = null;
            try
            {
                // Initialize the engine once for all files
                Log("Initializing Canon DPP4 engine...");
                dsf = new DSF();
                uint r = dsf.Initialize();
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"DppMWInitialize failed (code 0x{r:X8}). " +
                        "Please verify that DPP4 is correctly installed.");

                // Process one RAW file at a time
                for (int fileIndex = 0; fileIndex < totalFiles; fileIndex++)
                {
                    string rawFile = _opts.RawFiles[fileIndex];
                    var (primaryPath, secondaryPath) = _opts.BuildOutputPaths(rawFile);

                    Program.Log($"[{fileIndex + 1}/{totalFiles}] {Path.GetFileName(rawFile)}");

                    bool ok = ConvertSingleFile(
                        rawFile, recipeFile,
                        primaryPath, primaryType,
                        secondaryPath, secondaryType,
                        hasJpeg);

                    if (ok)
                        successCount++;
                    else
                    {
                        failureCount++;
                        failedFiles.Add(Path.GetFileName(rawFile));
                    }
                }
            }
            finally
            {
                if (dsf != null)
                    try { dsf.Terminate(); } catch { }
            }

            Program.Log($"Done: {successCount} succeeded, {failureCount} failed " +
                        $"(out of {totalFiles} file(s)).");
            if (failedFiles.Count > 0)
                Program.Log("Failed: " + string.Join(", ", failedFiles));

            return failureCount;
        }

        // ------------------------------------------------------------------
        //  Single-file conversion
        // ------------------------------------------------------------------

        private bool ConvertSingleFile(
            string rawFile, string recipeFile,
            string primaryPath, DPPMWare.DppImageType primaryType,
            string secondaryPath, DPPMWare.DppImageType secondaryType,
            bool hasJpeg)
        {
            DSFRecipe recipe = null;
            DSFBatchScript batchScript = null;
            string tempPath = null;
            DSFBatchProcess batchProcess = null;

            try
            {
                // Create batch script
                uint r = DSFBatchScript.Create(out batchScript);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch script creation failed (0x{r:X8}).");

                // Set recipe at script level.
                // Passing null to AddFile() lets the engine read EXIF orientation
                // directly from the RAW file instead of using the recipe's rotation.
                r = batchScript.SetRecipePath(recipeFile);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"SetRecipePath failed (0x{r:X8}).");


                // Primary output
                Log($"  -> {Path.GetFileName(primaryPath)}");
                r = batchScript.AddFile(rawFile, primaryPath, primaryType, null);
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"AddFile failed for '{Path.GetFileName(rawFile)}' (0x{r:X8}). " +
                        "Verify that the file is a RAW format supported by this DPP4 version.");

                // Secondary output (dual formats only)
                if (secondaryPath != null)
                {
                    Log($"  -> {Path.GetFileName(secondaryPath)}");
                    r = batchScript.AddFile(rawFile, secondaryPath, secondaryType, null);
                    if (DPPMWare.IsError(r))
                        throw new ConversionException(
                            $"AddFile (secondary) failed for '{Path.GetFileName(rawFile)}' (0x{r:X8}).");
                }

                // JPEG quality (only when output includes JPEG)
                if (hasJpeg)
                {
                    r = batchScript.SetJpegQuality((uint)_opts.JpegQuality);
                    if (DPPMWare.IsError(r))
                        Log($"  Warning: SetJpegQuality failed (0x{r:X8}), recipe value will be used.");
                }

                r = batchScript.SetExifInfoLevel(DPPMWare.DppSaveExifInfoLevel.All);
                if (DPPMWare.IsError(r))
                    Log($"  Warning: SetExifInfoLevel failed (0x{r:X8}).");

                // Save to temp file
                tempPath = Path.Combine(
                    DSFUtility.GetDPP4BatchTempFolderPath(),
                    Guid.NewGuid().ToString());

                r = batchScript.SaveScriptFile(tempPath);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"SaveScriptFile failed (0x{r:X8}).");

                batchScript.Dispose();
                batchScript = null;

                // Create and run process
                r = DSFBatchProcess.Create(tempPath, out batchProcess);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch process creation failed (0x{r:X8}).");

                var done = new ManualResetEventSlim(false);
                bool anyFailure = false;

                batchProcess.NotifyBatchCurrentProgress +=
                    (src, dst, err, cur, tot, pct) =>
                    {
                        if (pct == 100)
                        {
                            bool ok = !DPPMWare.IsError(err) && err != 5u;
                            if (ok)
                                Log($"  OK: {Path.GetFileName(dst)}");
                            else
                            {
                                anyFailure = true;
                                Program.Log($"  FAILED: {Path.GetFileName(dst)} " +
                                            $"(0x{err:X8} - {DescribeError(err)})");
                            }
                        }
                        else if (_opts.Verbose)
                        {
                            Program.Log($"  {Path.GetFileName(src)} {pct}%");
                        }
                    };

                batchProcess.NotifyBatchCompleted += () => done.Set();

                r = batchProcess.Execute(setSystemRecipe: false);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch execute failed (0x{r:X8}).");

                TimeSpan timeout = TimeoutPerFile > MinTimeout ? TimeoutPerFile : MinTimeout;
                if (!done.Wait(timeout))
                    throw new ConversionException(
                        $"Timeout after {timeout.TotalMinutes:0} minutes " +
                        $"for '{Path.GetFileName(rawFile)}'.");

                return !anyFailure;
            }
            catch (ConversionException ex)
            {
                Program.Log($"  ERROR: {ex.Message}");
                return false;
            }
            finally
            {
                if (batchProcess != null) { try { batchProcess.Dispose(); } catch { } }
                if (batchScript != null) { try { batchScript.Dispose(); } catch { } }
                if (tempPath != null && File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { }
            }
        }

        // ------------------------------------------------------------------
        //  Helpers
        // ------------------------------------------------------------------

        private static (DPPMWare.DppImageType primary, DPPMWare.DppImageType secondary)
            GetImageTypes(OutputFormat fmt)
        {
            switch (fmt)
            {
                case OutputFormat.Jpeg:
                    return (DPPMWare.DppImageType.Jpeg, DPPMWare.DppImageType.Unknown);
                case OutputFormat.Tiff8:
                    return (DPPMWare.DppImageType.Tiff8, DPPMWare.DppImageType.Unknown);
                case OutputFormat.Tiff16:
                    return (DPPMWare.DppImageType.Tiff16, DPPMWare.DppImageType.Unknown);
                case OutputFormat.Tiff8AndJpeg:
                    return (DPPMWare.DppImageType.Tiff8, DPPMWare.DppImageType.Jpeg);
                case OutputFormat.Tiff16AndJpeg:
                    return (DPPMWare.DppImageType.Tiff16, DPPMWare.DppImageType.Jpeg);
                default:
                    throw new InvalidOperationException("Unknown format: " + fmt);
            }
        }

        private static string DescribeError(uint code)
        {
            switch (code)
            {
                case 7u: return "file format not supported by this DPP4 version";
                case 34u: return "source file not found or not readable";
                case 41u: return "permission denied on output folder";
                case 42u: return "insufficient disk space";
                case 43u: return "destination path is not valid";
                case 64u: return "insufficient memory";
                case 3u: return "internal DPP4 engine error";
                default: return "unknown error";
            }
        }

        private void Log(string msg)
        {
            if (_opts.Verbose)
                Program.Log(msg);
        }
    }
}