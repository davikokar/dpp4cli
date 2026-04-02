# dpp4cli

A CLI utility that converts one or more Canon RAW files to JPEG|TIFF by applying a
DPP4 recipe (.dr4), using the Canon DPP4 engine installed on the machine —
without opening the graphical interface.

---

## How it works

All RAW files are added to a single `DSFBatchScript` and processed in one
`Execute()` call, exactly as DPP4Batch.exe does internally:

```
DSFRecipe.Create() + recipe.ReadFileData(.dr4)
    ↓
DSFBatchScript.Create()
for each RAW file:
    script.AddFile(raw, jpg, DppImageType.Jpeg, recipe)
script.SetJpegQuality(quality)
script.SaveScriptFile(tempPath)
    ↓
DSFBatchProcess.Create(tempPath)
process.Execute()              <- all files in a single engine run
    ↓
DppMWare.dll  ←  native Canon engine performing the actual work
```

The process is `WinExe` with an STA thread and a WinForms message pump,
required because `DppMWare.dll` uses COM STA internally.

---

## Prerequisites

- Windows 10 x64
- Canon DPP4 installed (default: `C:\Program Files\Canon\Digital Photo Professional 4\`)
- .NET Framework 4.7.1
- Visual Studio 2019+ or MSBuild Tools (to compile)

---

## Build

Copy these DPP4 dlls (DapCommon.dll, DapServiceFramework.dll, DapStrings.dll) from
the DPP4 installation folder to the src/lib folder of this project.
Open `src\dpp4cli.csproj` in Visual Studio, select **Release | x64** and build.
Or from the command line:

```cmd
msbuild src\dpp4cli.csproj /p:Configuration=Release /p:Platform=x64
```

The executable is generated in `src\bin\Release\dpp4cli.exe`.

The three Canon managed DLLs (`DapServiceFramework.dll`, `DapCommon.dll`,
`DapStrings.dll`) are stored in `src\lib\` and are automatically copied next
to the exe at every build. The native engine (`DppMWare.dll` and its
dependencies) is loaded at runtime from the DPP4 installation folder —
no recompile needed if DPP4 is reinstalled to a different location;
just update `dpp4cli.config`.

---

## Configuration

Create (or edit) `dpp4cli.config` in the same folder as `dpp4cli.exe`:

```ini
# dpp4cli.config
# Path to the Canon DPP4 installation folder.
dpp4dir=C:\Program Files\Canon\Digital Photo Professional 4
```

This file is copied automatically to the output folder at build time.

---

## Usage

```
dpp4cli --recipe <file.dr4> --outdir <folder> [options] file1.CR3 file2.CR3 ...
```

### Required

| Argument | Description |
|----------|-------------|
| `--recipe / -p <path>` | DPP4 recipe file (.dr4) |
| `--outdir / -d <folder>` | Output folder (created automatically if missing) |
| `file1.CR3 ...` | One or more Canon RAW files (.CR2, .CR3, .CRW, .CRF, ...) |

### Options

| Option | Description |
|--------|-------------|
| `--suffix / -s <text>` | Suffix added before `.jpg`; e.g. `_edit` turns `IMG_001.CR3` into `IMG_001_edit.jpg` |
| `--quality / -q <1-100>` | JPEG quality (default: 100) |
| `--dpp4dir <folder>` | Override the DPP4 path from the config file |
| `--verbose / -v` | Detailed diagnostic logging |
| `--format / -f` | Output format (default: jpg) accepted values: jpg, tiff8, tiff16, tiff8+jpg, tiff16+jpg |
| `--help / -h` | Show help |

### Examples

```cmd
:: Single file
dpp4cli --recipe portrait.dr4 --outdir C:\export IMG_001.CR3

:: Multiple files
dpp4cli --recipe portrait.dr4 --outdir C:\export IMG_001.CR3 IMG_002.CR3 IMG_003.CR3

:: With suffix and quality
dpp4cli --recipe params.dr4 --outdir C:\export --suffix _edit --quality 95 IMG_001.CR3 IMG_002.CR3

:: PowerShell — convert all CR3 files in a folder
dpp4cli --recipe params.dr4 --outdir C:\export (Get-Item C:\raw\*.CR3)

:: cmd.exe — using a for loop
for %f in (C:\raw\*.CR3) do dpp4cli --recipe params.dr4 --outdir C:\export %f
```

### Output naming

For each input file `<name>.<ext>` the output will be `<outdir>\<name><suffix>.jpg`.

| Input | Suffix | Output |
|-------|--------|--------|
| `IMG_001.CR3` | *(none)* | `IMG_001.jpg` |
| `IMG_001.CR3` | `_edit` | `IMG_001_edit.jpg` |
| `IMG_001.CR3` | `_portrait_2024` | `IMG_001_portrait_2024.jpg` |

---

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | All files converted successfully |
| `1` | One or more files failed, or a fatal error occurred |

Per-file results are always printed to stdout regardless of `--verbose`,
so you can detect partial failures even in non-verbose mode.

---

## How to obtain a recipe file (.dr4)

1. Open DPP4 and adjust the development parameters on any image.
2. **Edit → Save recipe to file** (`Ctrl+Shift+C`).
3. Save the `.dr4` file.

The same recipe can be applied to any number of compatible RAW files.

---

## Troubleshooting

### "DPP4 folder not found"
Update `dpp4dir` in `dpp4cli.config` or use `--dpp4dir <path>`.

### "DppMWInitialize failed"
DPP4 is not installed or the DLL cannot be found. Verify that
`DapServiceFramework.dll` exists in the configured DPP4 folder.

### "AddFile failed (code 0x...)"
The RAW format is not supported by the installed DPP4 version, or the file
is corrupt. Try opening it directly in DPP4 to confirm.

### File shows as FAILED in output with code 7
File format not supported. Only Canon RAW formats recognised by your DPP4
version are valid inputs.

### File shows as FAILED with code 41
Permission denied on the output folder. Check write permissions.

### Timeout
The default timeout is `5 + 2 × N` minutes (where N is the number of files).
For very large batches on slow machines this may not be enough; you can
increase `ConversionTimeout` in `Converter.cs`.
