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

        // Timeout scales with the number of files: 5 minutes base + 2 minutes per file
        private TimeSpan ConversionTimeout =>
            TimeSpan.FromMinutes(5 + 2 * _opts.RawFiles.Length);

        public Converter(CliOptions opts)
        {
            _opts = opts;
        }

        /// <summary>
        /// Converts all RAW files in opts.RawFiles to JPEG in opts.OutputDir.
        /// Returns the number of files that failed (0 = full success).
        /// </summary>
        public int Convert(string recipeFile)
        {
            // Create output folder if it does not exist
            if (!Directory.Exists(_opts.OutputDir))
                Directory.CreateDirectory(_opts.OutputDir);

            DSF dsf = null;
            DSFRecipe recipe = null;
            DSFBatchScript batchScript = null;
            string tempScriptPath = null;
            DSFBatchProcess batchProcess = null;

            try
            {
                // ----------------------------------------------------------
                // 1. Initialize the Canon engine
                // ----------------------------------------------------------
                Log("Initializing Canon DPP4 engine...");
                dsf = new DSF();
                uint r = dsf.Initialize();
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"DppMWInitialize failed (code 0x{r:X8}). " +
                        "Please verify that DPP4 is correctly installed.");

                // ----------------------------------------------------------
                // 2. Load recipe from .dr4 file
                // ----------------------------------------------------------
                Log("Loading recipe: " + recipeFile);
                r = DSFRecipe.Create(out recipe);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Recipe handle creation failed (0x{r:X8}).");

                r = recipe.ReadFileData(recipeFile);
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"Reading recipe '{recipeFile}' failed (0x{r:X8}). " +
                        "Please verify that the file is a valid DPP4 recipe (.dr4).");

                // ----------------------------------------------------------
                // 3. Create batch script
                // ----------------------------------------------------------
                Log("Creating batch script...");
                r = DSFBatchScript.Create(out batchScript);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch script creation failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 4. Add all RAW files to the script
                // ----------------------------------------------------------
                foreach (string rawFile in _opts.RawFiles)
                {
                    string outputFile = _opts.BuildOutputPath(rawFile);
                    Log($"  Adding: {Path.GetFileName(rawFile)} -> {Path.GetFileName(outputFile)}");

                    r = batchScript.AddFile(rawFile, outputFile, DPPMWare.DppImageType.Jpeg, recipe);
                    if (DPPMWare.IsError(r))
                        throw new ConversionException(
                            $"AddFile failed for '{Path.GetFileName(rawFile)}' (0x{r:X8}). " +
                            "Verify that the file is a RAW format supported by the installed DPP4 version.");
                }

                // ----------------------------------------------------------
                // 5. Set JPEG parameters
                // ----------------------------------------------------------
                r = batchScript.SetJpegQuality((uint)_opts.JpegQuality);
                if (DPPMWare.IsError(r))
                    Log($"Warning: SetJpegQuality failed (0x{r:X8}), " +
                        "the quality defined in the recipe will be used.");

                r = batchScript.SetExifInfoLevel(DPPMWare.DppSaveExifInfoLevel.All);
                if (DPPMWare.IsError(r))
                    Log($"Warning: SetExifInfoLevel failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 6. Save script to temporary file
                // ----------------------------------------------------------
                tempScriptPath = Path.Combine(
                    DSFUtility.GetDPP4BatchTempFolderPath(),
                    Guid.NewGuid().ToString());

                Log("Saving batch script to: " + tempScriptPath);
                r = batchScript.SaveScriptFile(tempScriptPath);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"SaveScriptFile failed (0x{r:X8}).");

                batchScript.Dispose();
                batchScript = null;

                // ----------------------------------------------------------
                // 7. Create batch process
                // ----------------------------------------------------------
                Log("Creating batch process...");
                r = DSFBatchProcess.Create(tempScriptPath, out batchProcess);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch process creation failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 8 & 9. Execute and wait — collect per-file results
                // ----------------------------------------------------------
                Log($"Starting conversion of {_opts.RawFiles.Length} file(s)...");

                var done          = new ManualResetEventSlim(false);
                int successCount  = 0;
                int failureCount  = 0;
                var failedFiles   = new List<string>();

                batchProcess.NotifyBatchCurrentProgress +=
                    (src, dst, err, cur, tot, pct) =>
                    {
                        if (pct == 100)
                        {
                            // A file just finished
                            bool ok = !DPPMWare.IsError(err) && err != 5u; // 5 = cancelled
                            if (ok)
                            {
                                successCount++;
                                Program.Log($"[{cur + 1}/{tot}] OK: {Path.GetFileName(dst)}");
                            }
                            else
                            {
                                failureCount++;
                                string desc = DescribeError(err);
                                failedFiles.Add(Path.GetFileName(src));
                                Program.Log($"[{cur + 1}/{tot}] FAILED: {Path.GetFileName(src)} " +
                                            $"(0x{err:X8} - {desc})");
                            }
                        }
                        else if (_opts.Verbose)
                        {
                            Program.Log($"  [{cur + 1}/{tot}] {Path.GetFileName(src)} {pct}%");
                        }
                    };

                batchProcess.NotifyBatchCompleted += () => done.Set();

                r = batchProcess.Execute(setSystemRecipe: false);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Batch execute failed (0x{r:X8}).");

                if (!done.Wait(ConversionTimeout))
                    throw new ConversionException(
                        $"Timeout: conversion did not complete within " +
                        $"{ConversionTimeout.TotalMinutes:0} minutes.");

                // ----------------------------------------------------------
                // Summary
                // ----------------------------------------------------------
                Program.Log($"Done: {successCount} succeeded, {failureCount} failed " +
                            $"(out of {_opts.RawFiles.Length}).");

                if (failedFiles.Count > 0)
                    Program.Log("Failed files: " + string.Join(", ", failedFiles));

                return failureCount;
            }
            finally
            {
                if (batchProcess != null) { try { batchProcess.Dispose(); } catch { } }
                if (batchScript  != null) { try { batchScript.Dispose();  } catch { } }
                if (recipe       != null) { try { recipe.Dispose();       } catch { } }

                if (tempScriptPath != null && File.Exists(tempScriptPath))
                    try { File.Delete(tempScriptPath); } catch { }

                if (dsf != null)
                    try { dsf.Terminate(); } catch { }
            }
        }

        private static string DescribeError(uint code)
        {
            switch (code)
            {
                case  7u: return "file format not supported by this DPP4 version";
                case 34u: return "source file not found or not readable";
                case 41u: return "permission denied on output folder";
                case 42u: return "insufficient disk space";
                case 43u: return "destination path is not valid";
                case 64u: return "insufficient memory";
                case  3u: return "internal DPP4 engine error";
                default:  return "unknown error";
            }
        }

        private void Log(string msg)
        {
            if (_opts.Verbose)
                Program.Log(msg);
        }
    }
}
