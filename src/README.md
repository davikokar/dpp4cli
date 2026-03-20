# dpp4cli

A CLI utility that converts one or more Canon RAW files to JPEG by applying a
DPP4 recipe (.dr4), using the Canon DPP4 engine installed on the machine —
without opening the graphical interface.


## Setup

Copy these DPP4 dlls (DapCommon.dll, DapServiceFramework.dll, DapStrings.dll) from
the DPP4 installation folder (usually C:\Program Files\Canon\Digital Photo Professional 4) 
to the same folder with the dpp4cli.exe file.


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


