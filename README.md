# FUSE Narrow Gauge

A FUSE companion module for narrow-gauge and dual-gauge track, generated narrow
ghost routing, and custom trackwork rendering.

## Requirements

- Windows
- Railroader installed locally
- FUSE source at `..\Rail\FUSE`
- .NET SDK that can build `net48` projects

## Project Layout

- `src/`: mod source files
- `src/GhostGraphSynchronizer.cs`: deterministic native ghost topology
- `src/DualGaugeLinkRegistry.cs`: standard/narrow counterpart registry and future coupler boundary
- `src/GaugeGraphValidator.cs`: generated topology validation
- `src/SpecialWorkPresetCatalog.cs`: reusable author-facing preset catalog
- `src/SpecialWorkAuthoring.cs`: FUSE extension parser and preset binding validation
- `src/SpecialWorkTopologySynchronizer.cs`: gauge-family boundary compiler
- `src/SpecialWorkRuntimeDiscovery.cs`: runtime `SpecialWorkDefinition` expansion
- `src/StandardCrossingDiscovery.cs`: same-grade standard diamond detection
- `docs/special-work-catalog.md`: topology-driven switch, transition, crossing,
  slip, stub, and wye catalog
- `src/SpecialWorkGeometryAnalyzer.cs`: physical rails, intersections, frogs,
  guards, blades, and validation
- `src/SpecialWorkDebugRenderer.cs`: colored in-world compiler debug view
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

- This module requires and loads after FUSE.
- Set `"gauge": "Narrow"`, `"gauge": "DualGauge"`, `"gauge": "DualGauge_L"`,
  `"gauge": "DualGauge_R"`, or `"gauge": "DualGauge_T"` on FUSE
  `tracks.segments` entries.
- A `DualGauge` segment automatically receives a real native narrow-gauge ghost
  route with deterministic `fuse-ng:*` IDs. Map authors do not place ghosts.
- `DualGauge_L` and `DualGauge_R` explicitly select the shared outer
  standard-gauge rail. Left/right use the physical frame authored on the track
  nodes, so reversing a segment's `startId` and `endId` does not swap the
  selected physical rail.
- Plain `DualGauge` infers the shared-rail side from narrow branch transitions
  and propagates it through connected dual segments. Explicit
  `fuse-ng:shared-rail=left|right` tags remain supported. Unconstrained
  dual-gauge components default to the right rail.
- `DualGauge_T` marks a dedicated degree-two shared-rail transition between
  `DualGauge_L` and `DualGauge_R` segments. Its generated narrow route shifts
  between the neighboring offsets, and its visible narrow rails form the fixed
  two-ended transition across the marked segment. This is catalog preset
  `dual.shared-rail-flip`; it is segment-anchored and generates automatically
  when the marked segment has exactly one `DualGauge_L` and one `DualGauge_R`
  neighbor.
- Fully dual-gauge turnouts mechanically synchronize their standard and
  generated narrow switch routes. Route mapping follows linked segments rather
  than assuming both native switches use the same normal/reversed ordering.
- A switch with two `DualGauge` main legs and one `Narrow` branch is compiled
  automatically into a fixed standard route and a real three-leg narrow switch
  on the generated narrow graph. Standard equipment cannot route onto the
  narrow-only branch.
- The special-work catalog contains ordinary, narrow, dual-gauge, crossing,
  transition, slip, stub, and staged three-way topology recipes. Node-anchored
  switch work uses the shared `SpecialWorkDefinition` analysis pipeline;
  segment-anchored fixed transitions use their catalog topology contract.
- Native FUSE packages can select a derived special-work preset through
  `extensions["narrowGauge.specialWork"]`. FUSE itself needs no NarrowGauge
  schema or patch.
- The Unity Mod Manager panel can enable a special-work debug view:
  blue standard routes/physical rails, cyan narrow routes/physical rails,
  green shared rails, orange intersections, red frog candidates, and purple
  blades/stub assemblies.
- Validated measured special-work plans replace fixed dual-gauge turnout rails
  with calculated running rails, frog noses, wing rails, and guard rails.
  Invalid plans retain the existing fallback geometry.
- Isolated same-grade intersections between two ordinary standard-gauge
  segments are compiled automatically as fixed `crossing.diamond` work. The
  source routes remain graph-disconnected; only their rail and tie rendering is
  replaced through the measured crossing envelope.
- Generated ghost rails, roadbed, masks, and bumpers are hidden. A generated
  ghost switch is visible only when it owns a real narrow-to-dual transition.
- `DualGaugeLinkRegistry.CanBridgeCoupling(...)` is the future cross-family
  coupler permission boundary. No cross-family coupling patch is active yet.
- Game-managed DLLs are not included in this repository.
- Local cache, IDE, and build output folders are excluded via `.gitignore`.
- This repository is set up for source upload to GitHub, not for shipping compiled releases.

## Special-work authoring

Place normal track, assign segment gauges, then select a preset by binding one
existing anchor node. NarrowGauge derives routes, physical rails, intersections,
frogs, guards, blades, and supported native topology from the surrounding track.
Fixed standard-gauge diamonds are the exception: two sufficiently long
standard segments that are not joined at their same-grade geometric crossing
are detected directly, so they require neither an anchor node nor ghost nodes.

```json
{
  "extensions": {
    "narrowGauge.specialWork": {
      "version": 1,
      "objects": {
        "yard-narrow-lead": {
          "preset": "dual.narrow-branch-joins-main",
          "anchorNode": "NCustom_0ifg",
          "parameters": {
            "sharedRailSide": "auto",
            "frogNumber": 8
          }
        }
      }
    }
  }
}
```

The extension can also use an array of objects with explicit `id` properties.
Unknown presets, incompatible topology, duplicate IDs, and duplicate anchor
bindings are logged as authoring errors. Parameters are retained for the
production geometry builder.
