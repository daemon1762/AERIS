# AERIS CP3.75 Source Authority

## Golden ND authority

- Archive: `AERISFlightControl-v0.18.0.0_DEV_CP3_Gate5_IntegratedAcceptanceCandidate14_SolidSurfacePreloadExclusionHotfix1_Source.zip`
- SHA-256: `350758594c3bb8ab36eed5c096515dfd20e2989b96a2089c96c52a624093b140`
- Git tag: `cp3-gate5-candidate14-golden`
- Golden commit: `3a653b5b7adce8a790026c6c756953c875ea89ad`

## Current AERIS20 import

- Archive: `AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate4_CP3GoldenCartographicQuality_Candidate2_Source.zip`
- SHA-256: `aa473f1f77d0b3c5356290ecdfc1b0a07d120616eeba7307229bff8b6722656e`
- Git tag: `aeris20-cp3.5-gate4-candidate2-rejected`

Candidate2 is preserved only as the latest AERIS20 non-ND/current-state reference and as rejected ND failure evidence. It is not active ND authority.

## CP3.75 rule

CP3.75 is an ND-only recovery/rebase. It is neither a whole-project rollback to CP3 nor a continuation of the rejected CP3.5 ND presentation line. Protected non-ND runtime code remains at the current AERIS20 baseline unless a concrete dependency finding is documented.
