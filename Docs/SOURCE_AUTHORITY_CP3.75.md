# AERIS CP3.75 Source Authority

## Golden ND authority

CP3.75 ND recovery/rebase uses the following historical source as its known-good visual/geographic authority:

- Archive: `AERISFlightControl-v0.18.0.0_DEV_CP3_Gate5_IntegratedAcceptanceCandidate14_SolidSurfacePreloadExclusionHotfix1_Source.zip`
- SHA-256: `350758594c3bb8ab36eed5c096515dfd20e2989b96a2089c96c52a624093b140`
- Git tag: `cp3-gate5-candidate14-golden`

The tagged commit is an exact import of the source archive contents. Do not rewrite, squash, force-move, or reuse this tag.

## CP3.75 rule

CP3.75 is an ND-only recovery/rebase. It is not a whole-project rollback to CP3 and is not a continuation of the rejected CP3.5 ND presentation line.

Protected non-ND areas must remain at the current validated AERIS baseline unless a concrete dependency finding requires a narrowly documented change.
