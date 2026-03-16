// Converter.cs
//
// Conversion pipeline (from DAPEventSaveConvert + DAPProcessUtility):
//
//  1. DPPMWare.DppMWInitialize()          initialize the native Canon engine
//  2. DSFRecipe.Create()                  create and empty recipe handle
//     recipe.ReadFileData(recipePath)     deserialize the .dr4 file
//  3. DSFBatchScript.Create()             create the batch script in memory
//                                         (internally: SetSystemRecipeToHandle)
//  4. script.AddFile(src, dst,            add the pair RAW → JPEG to the script
//                    DppImageType.Jpeg,   with the explicit recipe
//                    recipe)
//  5. script.SetJpegQuality(quality)      set the JPEG quality
//     script.SetExifInfoLevel(All)        keep all EXIF metadata
//  6. script.SaveScriptFile(tempPath)     serialize the script to a temp file
//  7. DSFBatchProcess.Create(tempPath)    create the script batch process
//  8. process.Execute(false)              execute the async conversion
//  9. ManualResetEvent.WaitOne()          wait for the complete callback
// 10. DPPMWare.DppMWTerminate()           terminate the engine

using System;
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

        // Timeout for the conversion: 10 minutes (big files or slow machines)
        private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(10);

        public Converter(CliOptions opts)
        {
            _opts = opts;
        }

        public void Convert(string rawFile, string recipeFile, string outputFile)
        {
            // Create output folder if it does not exist
            string outDir = Path.GetDirectoryName(Path.GetFullPath(outputFile));
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            // Every variable is released in finally
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
                Log("Initialize Canon DPP4 engine...");
                dsf = new DSF();
                uint r = dsf.Initialize();
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"DppMWInitialize failed (codice 0x{r:X8}). " +
                        "Please verify that DPP4 is correctly installed.");

                // ----------------------------------------------------------
                // 2. Load recipe from .dr4 file 
                // ----------------------------------------------------------
                Log("Loading recipe: " + recipeFile);
                r = DSFRecipe.Create(out recipe);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Creation of recipe handle failed (0x{r:X8}).");

                r = recipe.ReadFileData(recipeFile);
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"Reading of the recipe '{recipeFile}' failed (0x{r:X8}). " +
                        "Please verify that the recipe file is a valid recipe DPP4 file (.dr4).");

                // ----------------------------------------------------------
                // 3. Create batch script 
                // ----------------------------------------------------------
                Log("Creation of batch script...");
                r = DSFBatchScript.Create(out batchScript);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Creation of batch script failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 4. Add file to conversion queue
                // ----------------------------------------------------------
                //    The recipe added here overwrite the "system recipe"
                //    for this specific file.
                Log($"Adding file: {Path.GetFileName(rawFile)} → {Path.GetFileName(outputFile)}");
                r = batchScript.AddFile(rawFile, outputFile, DPPMWare.DppImageType.Jpeg, recipe);
                if (DPPMWare.IsError(r))
                    throw new ConversionException(
                        $"AddFile failed (0x{r:X8}). " +
                        "Please verifica that the RAW file is supported by the installed version of DPP4.");

                // ----------------------------------------------------------
                // 5. Set JPEG quality parameter
                // ----------------------------------------------------------
                r = batchScript.SetJpegQuality((uint)_opts.JpegQuality);
                if (DPPMWare.IsError(r))
                    Log($"SetJpegQuality failed (0x{r:X8}), " +
                        "the quality defined in the recipe will be used.");

                r = batchScript.SetExifInfoLevel(DPPMWare.DppSaveExifInfoLevel.All);
                if (DPPMWare.IsError(r))
                    Log($"SetExifInfoLevel failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 6. Save the script to temporary file
                // ----------------------------------------------------------
                tempScriptPath = Path.Combine(
                    DSFUtility.GetDPP4BatchTempFolderPath(),
                    Guid.NewGuid().ToString());

                Log("Saving temporary script: " + tempScriptPath);
                r = batchScript.SaveScriptFile(tempScriptPath);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"SaveScriptFile failed (0x{r:X8}).");

                // A this point the script is on disk, we can release the object
                batchScript.Dispose();
                batchScript = null;

                // ----------------------------------------------------------
                // 7. Create the script batch process
                // ----------------------------------------------------------
                Log("Creatione of bacth process...");
                r = DSFBatchProcess.Create(tempScriptPath, out batchProcess);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Creation of batch process failed (0x{r:X8}).");

                // ----------------------------------------------------------
                // 8. Execute the conversion and wait for completion
                // ----------------------------------------------------------
                Log("Start conversion...");
                var done = new ManualResetEventSlim(false);
                Exception convException = null;
                uint finalError = 0;

                // Callback: current progress 
                batchProcess.NotifyBatchCurrentProgress +=
                    (src, dst, err, cur, tot, pct) =>
                    {
                        if (_opts.Verbose)
                            Program.Log($"  Progress {cur + 1}/{tot} — {pct}%" +
                                        (DPPMWare.IsError(err) ? $" (error 0x{err:X8})" : ""));
                        finalError = err;
                    };

                // Callback: completion (can be on any thread)
                batchProcess.NotifyBatchCompleted += () =>
                {
                    done.Set();
                };

                r = batchProcess.Execute(setSystemRecipe: false);
                if (DPPMWare.IsError(r))
                    throw new ConversionException($"Execute batch failed (0x{r:X8}).");

                // Wait for the completition with timeout
                if (!done.Wait(ConversionTimeout))
                    throw new ConversionException(
                        $"Timeout: conversion non completed within {ConversionTimeout.TotalMinutes} minutes.");

                // ----------------------------------------------------------
                // 9. Verify output
                // ----------------------------------------------------------
                if (!batchProcess.Succeeded)
                {
                    // The error in progress callback is the most precise error code we can get
                    uint errCode = finalError != 0 ? finalError : batchProcess.Error;
                    throw new ConversionException(
                        $"Conversion failed (code DPP4: 0x{errCode:X8}).\n" +
                        DescribeError(errCode));
                }

                if (!File.Exists(outputFile))
                    throw new ConversionException(
                        "Conversione was successfull but the ouptfile " +
                        "does not exist: " + outputFile);

                Log($"Generated file: {new FileInfo(outputFile).Length / 1024} KB");
            }
            finally
            {
                // Clean up in reverse order
                if (batchProcess != null) { try { batchProcess.Dispose(); } catch { } }
                if (batchScript  != null) { try { batchScript.Dispose();  } catch { } }
                if (recipe       != null) { try { recipe.Dispose();       } catch { } }

                // Delete the temporary script file (DPP4 does it automatically
                // within DSFBatchProcess.BatchCompletedAction, but we
                // do it as well for safety in case of exceptions before Execute)
                if (tempScriptPath != null && File.Exists(tempScriptPath))
                {
                    try { File.Delete(tempScriptPath); } catch { }
                }

                if (dsf != null)
                {
                    try { dsf.Terminate(); } catch { }
                }
            }
        }

        private static string DescribeError(uint code)
        {
            // Error codes in DAPSaveBatchProgressForm.MakeErroMessage
            switch (code)
            {
                case  7u: return "File format not supported by this version of DPP4.";
                case 34u: return "Source file not found or nor readable.";
                case 41u: return "Denied permission on the output folder.";
                case 42u: return "Insufficient disk space.";
                case 43u: return "Destination path is not valid.";
                case 64u: return "Insufficient memory.";
                case  3u: return "Internal error in DPPç engine.";
                default:  return "Verify that raw file and recipe file are compatible.";
            }
        }

        private void Log(string msg)
        {
            if (_opts.Verbose)
                Program.Log(msg);
        }
    }
}
