# FUSE Narrow Gauge Documentation

A FUSE companion module for narrow-gauge and dual-gauge track, generated narrow
ghost routing, and custom trackwork rendering.

## Players

| Doc | What it covers |
| --- | --- |
| [Getting Started](GETTING_STARTED.md) | Install, requirements, and verifying it loaded |

Narrow Gauge adds no content by itself — it gives route packages the ability to
author narrow and dual-gauge track. On a route that doesn't use it, you will see
no change.

## Route Authors

| Doc | What it covers |
| --- | --- |
| [Authoring Guide](AUTHORING.md) | Gauge tags, dual-gauge rules, and selecting special work |
| [Special-Work Catalog](special-work-catalog.md) | Every switch, crossing, slip, stub, and wye preset |
| [Special-Work Extension Example](special-work-extension-example.json) | A complete extension block |

## Design And Investigation Notes

Background material, kept for contributors rather than authors:

- [Dual-Gauge Special Trackwork Generator Design](dual-gauge-special-trackwork-generator-design.md)
- [Special-Work Preset Library Design](special-work-preset-library-design.md)
- [Special-Work Geometry Workbench](SPECIAL_WORK_GEOMETRY_WORKBENCH.md)
- [Special-Work Turnout Combo Status](special-work-turnout-combo-status.md)
- [Special Trackwork Investigation](special-trackwork-investigation.md)
- [NCustom Continuity Report](ncustom-0ifg-vs-24b2-continuity-report.md)

## Offline Manual

- [Narrow Gauge User Manual](pdf/Narrow-Gauge-User-Manual.pdf) — install, authoring, and the full preset catalog

Rebuild with `python scripts/build_pdfs.py` (needs `pip install reportlab`).

## Quick Answers

**Nothing narrow-gauge appears.** The module needs a route package that authors
narrow or dual-gauge segments. It changes nothing on its own.

**Do I place the narrow ghost track?** No. A `DualGauge` segment generates its own
narrow route automatically with deterministic `fuse-ng:*` ids.

**Three-way switches?** Present in the catalog as staged topology
(`three-way.standard`, `.narrow`, `.dual`), but note that Railroader does not
support three-way switches as ordinary graph switches.

**Where are the logs?**
`%USERPROFILE%\AppData\LocalLow\Giraffe Lab LLC\Railroader\FUSE.log`

## Project

- **Repository:** <https://github.com/Hrogers-Rog/Narrow_Gauge>
- **Requires:** [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup)
