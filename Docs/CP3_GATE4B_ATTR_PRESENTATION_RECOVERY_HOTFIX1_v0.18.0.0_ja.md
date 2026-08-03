# CP3 Gate 4B — ATTR Presentation Recovery Hotfix 1

## 目的

Gate 4B実機受入で、FAR foundationが完成し`ready==required`かつ`back_foundation=1.000`であるにもかかわらず、Temporal history拒否後にBACK refreshが再開されず`TERRAIN GPU BUILDING`が長時間継続する不具合を修正する。

## 根本原因

Gate 4BのBACK refresh判定は主に`TerrainGeneration / ViewGeneration / gpuContentRevision`へ依存していた。一方、DIRECT presentationは位置・range・TRACK UP headingへ厳しい互換条件を持ち、Temporal historyはviewport全域coverageを要求した。そのため、tile setが変わらない範囲の移動・旋回では`ViewGeneration`が変化しないままDIRECTとhistoryの両方が拒否され、BACK refreshが停止する状態が成立した。

## 修正契約

1. **Ready-foundation recovery invariant**
   - DIRECT不可、history不可、かつFAR foundation完成時は`ViewGeneration`の変更を待たない。
   - 同一RepaintでGPU BACKを強制再描画する。
   - FAR authorityが完成していればFRONTへatomic swapし、同じRepaintで再presentationする。

2. **Overscan GPU history surface**
   - 可視rangeの1.35倍をhistory surface rangeとしてFAR foundationを取得する。
   - 内部overscan rangeはUIの5/10/20/40/80/160kmステップへ再量子化せず、1～250kmのbounded planner rangeとしてそのまま使用する。
   - 最大250kmを超えない。
   - 画面外の実在FAR地形をGPU historyに保持し、通常の機体移動・TRACK UP旋回で履歴coverageが即失効する頻度を下げる。
   - 可視rangeそのものは変更しない。ND表示rangeはユーザー指定値のまま。

3. **Projection refresh authority**
   - tile set generationだけでなく、range、TRACK UP heading、中心移動、FRONT age、orientation、anchorからBACK refresh要否を判定する。
   - 通常更新は最大5Hzの既存throttleを維持する。
   - ready-foundation recoveryだけは表示不能時の緊急経路としてthrottle待ちを行わない。

4. **1秒watchdog**
   - FAR foundation完成済みなのに表示可能なFRONTが1秒以上存在しない場合、`[CP3_GATE4B_READY_BUILDING_VIOLATION]`をERROR出力する。
   - 実機受入では0件を必須とする。

5. **GPU-only維持**
   - CPU terrain drawingは0。
   - CPU safety fallback、UNKNOWN_TERRAIN、CPU raster workerを復活させない。
   - 大規模zoom-out等でoverscan historyにも地形が存在せず、新FAR foundationも未完成の場合は地形を創作しない。初回／真に未準備の`TERRAIN GPU BUILDING`のみ許容する。

## Telemetry

`[CP3_GATE4B_TEMPORAL]`へ以下を追加する。

- `forced_recovery`
- `ready_build_violation`
- `history_surface_range`

正常な安定飛行では`ready_build_violation=0`を維持する。

## 非対象

- CPU terrain fallback復活
- Route/Local常設payload復活
- LAND safety authorityのVirtual化
- AI/ML upscaler
- 他天体payload常時RAM常駐
