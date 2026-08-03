# My ADOFAI Mod

A Ponder-style interactive guide engine for A Dance of Fire and Ice (ADOFAI), based on [Create Ponder](https://github.com/Creators-of-Create/Ponder). It supports Unity Mod Manager, MelonLoader, and BepInEx.

## Project Structure

```
ProjectRoot/
├── core/
│   ├── Ponder.Core.csproj   -- Shared mod logic
│   ├── IHandler.cs                         -- Loader abstraction interface
│   ├── Main.cs                             -- Entry point (Initialize)
│   ├── Settings.cs                         -- Serializable settings
│   ├── Patches.cs                          -- Harmony patches
│   └── ResourceLoader.cs                   -- File loading utilities
├── loaders/
│   ├── umm/                                -- Unity Mod Manager adapter
│   ├── melon/                              -- MelonLoader adapter
│   ├── bepinex/                            -- BepInEx adapter
├── scripts/
│   ├── pack.csx                            -- Distribution zip packer
│   ├── pack.cmd                            -- Windows pack script
│   ├── pack.ps1                            -- PowerShell pack script
│   └── pack.sh                             -- Linux/macOS pack script
├── Resources/                              -- Mod assets (text, images, etc.)
├── ADOFAIMod.targets                       -- MSBuild targets (copy, deploy)
└── Info.json                               -- UMM manifest (only if UMM selected)
```

## Architecture

Each mod loader has its own adapter project that references the shared `core/` project.
The `IHandler` interface abstracts logging, settings, and lifecycle events so the
core mod code never depends on a specific loader.

```
Loader project (e.g. loaders/umm/)
  └── implements IHandler
      └── calls Main.Initialize(handler)
          └── core/ code runs loader-agnostic
```

## Prerequisites

- .NET SDK 6.0 or later
- A Dance of Fire and Ice (Steam)
- One or more of the supported mod loaders installed in the game directory

## Install the Template

```bash
# From a local NuGet package
dotnet new install path/to/Ponder.0.6.0.nupkg

# Or from a local copy of the repository
dotnet new install path/to/Ponder
```

## Create a Project

### Command Line

```bash
# All supported loaders (default)
dotnet new adofaiml -n MyMod -g "C:\Games\ADOFAI\A Dance of Fire and Ice.exe"

# Select specific loaders (disable the ones you don't need)
dotnet new adofaiml -n MyMod --bepinex false -g "C:\Games\ADOFAI\A Dance of Fire and Ice.exe"

# Specify author and description
dotnet new adofaiml -n MyMod -a "YourName" -d "My first ADOFAI mod" -g "path/to/game.exe"
```

### Visual Studio / JetBrains Rider

After installing the template, create a new project and search for "ADOFAI" or
"adofaiml". The new project wizard shows checkboxes for each loader.

### Parameters

| Short | Long | Description |
|---|---|---|
| `-n` | `--name` | Project name (becomes the mod name) |
| `-g` | `--game-path` | Game executable path (required for building) |
| `-a` | `--author` | Author name |
| `-d` | `--description` | Mod description |
| `-v` | `--version` | Initial version (default: 0.6.0) |
| `-um` | `--umm` | Include UMM loader (default: true) |
| `-ml` | `--melon` | Include MelonLoader (default: true) |
| `-bx` | `--bepinex` | Include BepInEx (default: true) |

## Build and Deploy

Set `GameExePath` in the `.csproj` file (or pass it with `-p`), then:

```bash
# Debug: build + deploy to game directory + launch
dotnet build -p:Loader=UMM
dotnet build -p:Loader=ML
dotnet build -p:Loader=BepInEx

# Release: build only, outputs to out/
dotnet build -c Release
```

Deploy paths by loader:

| Loader | Target Directory |
|---|---|
| `UMM` | `GameDir/Mods/{ModName}/` |
| `ML` | `GameDir/Mods/` |
| `BepInEx` | `GameDir/BepInEx/plugins/{ModName}/` |

Each project in the solution can be built independently. The `out/` directory
collects all output files in a flat layout, regardless of which loader was built:

```
out/
├── {ModName}.Core.dll
├── {ModName}.Loader.UMM.dll        (if built)
├── {ModName}.Loader.Melon.dll      (if built)
├── {ModName}.Loader.BepInEx.dll    (if built)
├── Info.json                        (only if UMM built)
└── Resources/
```

## Create Distribution Packages

Requires `dotnet script` (install with `dotnet tool install -g dotnet-script`).

```bash
# Build release binaries, then pack
scripts/pack.cmd      # Windows
scripts/pack.sh       # Linux/macOS
scripts/pack.ps1      # PowerShell
```

This produces per-loader ZIP archives in `dist/`:

| File | Structure (relative to game root) |
|---|---|
| `{ModName}_umm.zip` | `Mods/{ModName}/` flat |
| `{ModName}_melon.zip` | `Mods/` flat |
| `{ModName}_bepinex.zip` | `BepInEx/plugins/{ModName}/` flat |

Each archive is self-contained: extract directly into the game directory.

## Development

1. Write your mod logic in `core/`. It has no dependency on any specific loader.
2. Use `Main.Handler` for logging, `Main.Settings` for configuration.
3. Add Harmony patch classes under `core/` — they apply automatically when the
   mod is enabled.
4. Place assets in `Resources/` and load them with `ResourceLoader`.
5. Build with your chosen loader to test in-game.

## GitHub Template

This repository can also be used as a GitHub template. Click "Use this template"
on the repository page to create a new repository, then clone it and run the
init script to rename the project to match your repository name:

```bash
# Linux / macOS
chmod +x init.sh
./init.sh

# Windows (PowerShell)
.\init.ps1
```

The init script automatically detects the repository directory name and renames
all files and namespaces accordingly. It also re-initializes the Git history
with a clean initial commit.

## Uninstall the Template

```bash
dotnet new uninstall Ponder
```

## License

GPL-3.0-or-later
