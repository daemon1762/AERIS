# CP3.5 Gate 3 Candidate 2 — CP3 Frozen Visual Path Recovery

## 目的
Gate 3 Candidate 1 の可視タイル全面Hi-Res昇格を破棄し、CP3凍結版で成立していた描画品質をGolden Visual Referenceとして復元する。性能技術はCP3.5のmulticore / Exact Key Frame / overscan temporal reprojectionを維持する。

## 安全境界
- FAR foundationは常時の表示authority。
- VIRTUAL ROUTE=65x65、VIRTUAL LOCAL=97x97のCP3再構成を使用。
- Route/Local exact payloadはRAM/SSDに既に存在する場合のみviewportへ昇格。
- 欠落Route/Localを画質目的でflight viewportから新規生成しない。
- 40km超ではnear LODをFARへ戻し、160kmでRoute生成を要求しない。
- Candidate 1の129x129 adaptive local、screen-space promotion、合成coast分類、可視Hi-Res生成を廃止。

## 維持するCP3.5機能
- worker projection / main-thread minimal commit
- Exact Key Frame / 1.25x overscan / temporal reprojection
- accessibility palette generation invalidation
- Unified World Surface Phase 1
- compact responsive UI / TAKEOFF | FLIGHT | NAV | LAND
- Terrain quality LAND完全撤去（AUTO/LOW/MEDIUM/HIGHのみ）

## 今回の意図
画質と性能を同時に新造せず、まずCP3凍結画質へ戻して安定性を回復する。高解像度化は次段でSparse Refinement Overlayとして再設計し、Base working setを置換しない。
