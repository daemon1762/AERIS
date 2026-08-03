# CP3.75 Pure ND Rebase Candidate 1 — Design Record

Candidate 1 is the first recovery build after abandoning CP3.5 as the active ND line.

## What is restored exactly from Candidate14

The exact file list is authoritative in `Docs/ND_REBASE_SCOPE_MAP.md`. It covers the changed Terrain dependency chain, four ND Performance files, the project compile list and `UI/AERISNavigationDisplay.cs`.

## What is deliberately preserved from AERIS20

- AA/FBW / Autopilot / PROTECT / FlightState / Integrations
- Landing control/observation and runway authority
- Airfield default/calibration data
- later UI work outside the ND terrain-quality authority
- verified FDR/CVR archive retention

## Mixed merge rule

`AERISSettings.cs`, default settings cfg, bootstrap and `AERISWindow.cs` are merged narrowly. Candidate14 terrain authority wins; later independent non-ND behavior wins.

## Not in Candidate 1

No FPS optimization, new quality preset architecture, CP3.5 virtual world-surface reconstruction, temporal terrain warp, or new projection worker design is introduced. Runtime visual/geographic recovery must pass first.
