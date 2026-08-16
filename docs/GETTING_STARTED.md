# Getting Started

## Requirements

- Railroader `2025.1.x`
- Unity Mod Manager
- [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup) — required, and Narrow
  Gauge loads after it

## Install

1. Install Unity Mod Manager for Railroader.
2. Install FUSE and confirm it loads.
3. Place the `FUSE.NarrowGauge` folder in `Railroader/Mods`.
4. Start Railroader and load a map.

## Verify

```
/fuse.loaded
```

Both `FUSE` and `FUSE.NarrowGauge` should be listed and applied.

## What It Does

Narrow Gauge is a capability module, not a content pack. It lets route packages
author narrow-gauge and dual-gauge track, and it renders the specialised trackwork
that goes with them: frogs, guard rails, wing rails, blades, and crossings.

**On a route that doesn't use narrow gauge, nothing changes.** If you installed it
expecting to see narrow-gauge track appear, that is why nothing looks different.

What it handles for the route author:

- **Generated narrow routing.** A dual-gauge segment automatically gets a real
  native narrow-gauge ghost route. Authors do not place ghost track.
- **Measured trackwork geometry.** Validated plans replace fixed dual-gauge
  turnout rails with calculated running rails, frog noses, wing rails, and guard
  rails. Invalid plans fall back to the existing geometry rather than breaking.
- **Automatic diamonds.** Two standard-gauge segments crossing at the same grade
  without being joined are compiled into a fixed diamond crossing on their own.
- **Hidden generated scaffolding.** Ghost rails, roadbed, masks, and bumpers stay
  hidden; a generated ghost switch shows only when it owns a real
  narrow-to-dual transition.

## Debug View

The Unity Mod Manager panel can enable a special-work debug view, which colours
the compiler's understanding of the track:

| Colour | Meaning |
| --- | --- |
| Blue | Standard routes and physical rails |
| Cyan | Narrow routes and physical rails |
| Green | Shared rails |
| Orange | Intersections |
| Red | Frog candidates |
| Purple | Blades and stub assemblies |

This is an authoring and diagnostic tool — leave it off during normal play.

## Multiplayer And Saves

Narrow Gauge builds on FUSE, so the same expectations apply: every player needs
the same mods and package list installed locally. Back up saves before adding or
removing any world-changing mod.

## Reporting Problems

Include `FUSE.log`, `Player.log`, `/fuse.loaded` output, the route package
involved, and a screenshot of the trackwork if it is a geometry problem. Enabling
the debug view before the screenshot makes a geometry report far more useful.

Issues: <https://github.com/Hrogers-Rog/Narrow_Gauge/issues>

## Next

- [Authoring Guide](AUTHORING.md) — building narrow and dual-gauge track
- [Special-Work Catalog](special-work-catalog.md) — the preset list
