# Narrow Gauge Mod

Custom Railroader mod work for narrow gauge and dual-gauge track experiments, including custom switch rendering, shadow narrow-gauge graph support, and related track visualization patches.

## Requirements

- Windows
- Railroader installed locally
- .NET SDK that can build `net48` projects

## Project Layout

- `src/`: mod source files
- `Info.json`: Unity Mod Manager mod manifest
- `NarrowGaugeMod.csproj`: build configuration
- `Directory.Build.props.example`: optional local machine overrides

## Local Setup

This project references Railroader's shipped assemblies directly from your game install.

You can configure the game path in any of these ways:

1. Copy `Directory.Build.props.example` to `Directory.Build.props` and edit `RailroaderDir`.
2. Set the `RAILROADER_DIR` environment variable.
3. Pass `/p:RailroaderDir=...` on the command line.

If you want the build to copy the mod directly into the game's `Mods` folder, set `EnableModDeploy=true` in `Directory.Build.props` or pass `/p:EnableModDeploy=true`.

## Build

Build only:

```powershell
dotnet build .\NarrowGaugeMod.csproj
```

Build and deploy into the game mod folder:

```powershell
dotnet build .\NarrowGaugeMod.csproj /p:EnableModDeploy=true
```

## Notes

- Gauge metadata can come from legacy StrangeCustoms graph JSON or FUSE data files. In FUSE, set `"gauge": "Narrow"` or `"gauge": "DualGauge"` on `tracks.segments` entries.
- Game-managed DLLs are not included in this repository.
- Local cache, IDE, and build output folders are excluded via `.gitignore`.
- This repository is set up for source upload to GitHub, not for shipping compiled releases.
