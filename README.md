# dpp4cli

A CLI utility that converts Canon RAW files to JPEG by applying a parameter recipe (.dr4), using the Canon DPP4 engine installed on the machine directly—without opening the graphical user interface.

---

## How it works

This approach utilizes the minimal API that DPP4Batch.exe uses for headless conversion:

```
DSFRecipe.Create() + recipe.ReadFileData(.dr4)
    ↓
DSFBatchScript.Create()
script.AddFile(raw, jpg, DppImageType.Jpeg, recipe)
script.SetJpegQuality(quality)
script.SaveScriptFile(tempPath)
    ↓
DSFBatchProcess.Create(tempPath)
process.Execute()
    ↓
DppMWare.dll  ←  The native Canon engine performing the actual work
```

The process is a WinExe (not Exe) featuring a STA thread and a WinForms message pump, which is required because DppMWare.dll uses COM STA internally.

---

## Prerequisites

- Windows 10 x64
- Canon DPP4 installed in `C:\Program Files\Canon\Digital Photo Professional 4\`
- .NET Framework 4.7.1
- Visual Studio 2019+ or Build Tools (for compilation)

---

## Build

Open src\dpp4cli.csproj in Visual Studio, select Release | x64, and build. Alternatively, via command line (with MSBuild in your PATH):

```cmd
msbuild src\dpp4cli.csproj /p:Configuration=Release /p:Platform=x64
```

The executable will be generated in `src\bin\Release\dpp4cli.exe`.

**If DPP4 is installed in a non-standard folder**, update the HintPath paths in the .csproj file before compiling:

```xml
<Reference Include="DapServiceFramework">
  <HintPath>D:\MyCustomFolder\Canon\DPP4\DapServiceFramework.dll</HintPath>
</Reference>
```

---

## Usage

```
dpp4cli --raw <file.CR3> --recipe <params.dr4> --output <file.JPG> [options]
dpp4cli <file.CR3> <params.dr4> <file.JPG> [options]
```

### Required Arguments

| Argument | Description |
|-----------|-------------|
| `--raw / -r <path>` | File RAW Canon (.CR2, .CR3, .CRW, ...) |
| `--recipe / -p <path>` | File recipe DPP4 (.dr4) |
| `--output / -o <path>` | File JPG (.jpg) |

### Options

| Option | Description |
|---------|-------------|
| `--overwrite` | Overwrites the output file if it already exists |
| `--quality / -q <1-100>` | JPEG Quality (default: 100) |
| `--verbose / -v` | Detailed diagnostic logging |
| `--help / -h` | Show help |

### Examples

```cmd
:: Basic conversion
dpp4cli --raw IMG_001.CR3 --recipe portrait.dr4 --output IMG_001.JPG

:: Output with quality 95
dpp4cli --raw foto.CR3 --recipe params.dr4 --output risultato.jpg --quality 95

:: Output with overwrite enabled
dpp4cli --raw foto.CR3 --recipe params.dr4 --output risultato.jpg --overwrite

:: Detailed logging for debugging
dpp4cli --raw foto.CR3 --recipe params.dr4 --output IMG_001.JPG --verbose
```

---

## How to obtain a recipe file (.dr4)

1. Open DPP4.
2. Open an image and adjust the development parameters.
3. Save recipe to file
4. Save the .dr4 file.

The recipe contains all parameters: white balance, exposure, sharpness, noise reduction, color profile, tone curves, etc. The same recipe can be applied to any compatible RAW file.

---

## Exit Codes

| Code | Meaning |
|--------|-------------|
| `0` | Conversion completed successfully |
| `1` | Error |

---

## Troubleshooting

### "DppMWInitialize failed"
DPP4 is not installed or the DLL cannot be found. Verify that DapServiceFramework.dll exists in C:\Program Files\Canon\Digital Photo Professional 4\.

### "AddFile failed"
The RAW file format is not supported by the installed version of DPP4. Try opening the same file directly in DPP4 to verify compatibility.

### "Timeout"
Converting very large files on slow machines may exceed 10 minutes. This limit is configurable in Converter.cs (ConversionTimeout).

### "Conversion failed (code 7)"
Unsupported file format. Ensure that DPP4 can open the file correctly.

### "Conversion failed (code 41)"
Permission denied on the output folder. Check your folder write permissions.
