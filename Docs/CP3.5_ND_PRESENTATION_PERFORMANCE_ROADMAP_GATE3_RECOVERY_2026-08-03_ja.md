# AERIS CP3.5 ND Performance Roadmap — Gate 3 Recovery

## 現在地
- Gate 0: DONE — 負荷分解
- Gate 1: DONE — forced recovery/cadence診断
- Gate 2: PARTIAL PASS — multicore exact projection / Exact Key Frame / temporal architecture確立
- Gate 3 Candidate 1: REJECTED — 可視Hi-Res全面昇格によるbuild/upload/evict thrash
- Gate 3 Candidate 2: CURRENT — CP3 Frozen Visual Path Recovery / Existing-only exact refinement

## Candidate 2終了条件
1. 試験開始不能・2 FPS化・FRONT BUILDING停滞がない。
2. CP3凍結版をGolden Visual Referenceとして地形/海岸/等高線の劣化を認めない。
3. 160 kmでmissing Route/Localを生成しない。
4. accessibility異常なし。
5. Terrain quality LAND関連は復活しない。

## 次段 Gate 4
Unified World Surfaceの残り（range ring / route等）とIMGUI offload。ND Repaint残存コストを削る。

## 将来の高解像度化
Sparse Refinement Overlayとして再設計する。FAR Baseはpinし、refinementは別budget・生成前admission・main-thread commit budget・emergency abortを必須とする。BaseをHi-Res Meshで置換しない。
