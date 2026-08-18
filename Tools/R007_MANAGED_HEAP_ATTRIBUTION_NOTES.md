# AERIS27 REV3.5 R007 — Managed Heap Attribution

R007 is measurement-only. It follows R006 HF3 after the 2026-08-18 runtime showed that the infinite BLACK/foundation regression was repaired, while visible micro-freezes remained at approximately the historical Full-GC cadence.

Observed runtime facts from the R006 HF3 Desktop test:

- complete foundation/READY recovered;
- Gen0/Gen1/Gen2 collections commonly advanced together;
- visible freezes aligned with Full-GC windows around 100–170 ms;
- steady intervals were typically about 17–20 s, briefly shortening under heavier ND commit activity;
- no explicit `GC.Collect()` or `UnloadUnusedAssets` path was found in AERIS;
- Main Commit can be idle while periodic Full GC continues, so throughput tuning alone is no longer the active diagnosis.

R007 therefore attributes positive managed-heap movement in bounded windows around renderer content maintenance and TileSystem planning/maintenance, and logs passive Gen2 intervals. It never forces GC and changes no visual, authority, worker, 10 Hz, or 160 km semantics.
