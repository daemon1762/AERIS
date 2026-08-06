# Operation Health Pass 3 Cadence Hotfix 3 / Motion Commit

Hotfix 2の10 Hz authoritative pipelineを維持しつつ、実移動中のFRONT projectionを各authoritative tickでcommitする。停止中は従来のdirty/age fallbackへ戻り、不要な10 Hz BACK renderを発生させない。

- moving map: exact FRONT commit target = 10 Hz
- parked map: no forced commit
- worker/tile generation cadence: unchanged
- non-tick Repaint: cheap FRONT reuse only
- Candidate11 visual authority: unchanged
- 0.50s projection fallback and 8deg safety threshold: retained
