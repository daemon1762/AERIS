# Operation Health Pass 3 Cadence Hotfix 4 / Loading Ready State

表示可能な旧FRONTと、現在要求中のRange/ViewがREADYであることを分離する。

- range/view invalidation: requestedViewReady=false / progress=0 / Partial
- stale FRONT: continuity backdropとして保持可能
- UI: Partialの間は `TERRAIN GPU BUILDING xx%` を表示
- exact current-view FRONT swap: requestedViewReady=true / Complete
- non-tick cheap FRONT reuse: READY状態を変更しない
- 10 Hz Motion Commit / Candidate11 visual authority: unchanged
