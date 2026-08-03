# AERIS v0.18.0.0 — CP3 Gate 4B
## AERIS Terrain Temporal Reconstruction (ATTR)

### 目的
Gate 4Aで確立したGPU-only FAR presentationを維持しつつ、完成済みGPU FRONTを地理座標に基づいて現在viewportへ再投影し、次のFAR BACKが完成するまで表示を連続させる。同時に、毎RepaintでBACKを全面再構築していた経路を廃止し、view generationまたはGPU content revisionが変化した時だけ再構築する。

### Terrain payload方針
- GLOBAL: bootstrap用。
- FAR: 通常NDで常設する唯一のTerrain base payload。
- VIRTUAL ROUTE: MEDIUM品質でFARのGPU補間＋temporal reprojectionにより得る表示品質。exact Route payloadではない。
- VIRTUAL LOCAL: HIGH/LAND品質でFARの高解像度GPU presentation＋temporal reprojectionにより得る表示品質。exact Local payloadではない。
- EXACT LOCAL / LAND: 滑走路・LAND回廊など正確な地形値が必要な領域だけ既存exact bridgeを使用する。

VIRTUAL ROUTE／VIRTUAL LOCALは表示品質名であり、LAND安全判定のauthorityにはしない。

### Temporal reprojection
完成済みFRONTは以下を保持する。
- body / body radius
- terrain generation
- center latitude / longitude
- range
- map heading
- TRACK UP状態
- anchor
- render target orientation
- commit age

履歴再利用時は、旧FRONTのGUI基準点を旧AERISNdMapProjectionで緯度経度へ戻し、現在AERISNdMapProjectionへ再投影する。4隅の非線形歪みを監査し、3点から作るaffine近似が現在viewport全体を覆う場合だけGPU FRONTを再利用する。

履歴拒否条件:
- body / radius / terrain generation不一致
- TRACK/NORTH UPまたはorientation不一致
- anchor不一致
- 履歴20秒超
- 許容外range比
- 大きすぎるheading差・中心移動
- affine determinant異常
- 球面投影歪み超過
- 現在viewport全域を旧FRONTが覆えない場合
- confidence < 0.35

拒否時はCPU terrainへfallbackしない。完全な新GPU FAR FRONTがなければ従来通り`TERRAIN GPU BUILDING`を表示する。

### Differential GPU presentation
Gate 4AではFAR BACKが完成していると毎RepaintでFRONT/BACK swapできたため、安定飛行でも30回/秒級のswapが発生し得た。

Gate 4BではBACK更新要求を以下に限定する。
- FRONT未作成
- terrain generation変更
- viewport view generation変更
- Render-Ready/GPU content revision変更

同一view/content revisionの再試行は0.20秒以上間隔を空ける。現在FRONTが表示可能な間、BACK再構築は表示authorityにならない。

### GPU-only契約
- CPU terrain draw count = 0
- CPU safety fallbackなし
- UNKNOWN_TERRAIN塗り潰しなし
- FRONT historyはGPU RenderTextureのみ
- BACKはFAR foundation 100%完成まで非表示
- exact Route/Local欠落時はGPU FARを維持
- LAND安全判定はreconstructed displayを使用しない

### 品質表示
- LOW: FAR DIRECT
- MEDIUM: VIRTUAL ROUTE
- HIGH / LAND: VIRTUAL LOCAL

本GateのVIRTUAL品質はFARからのGPU空間補間とtemporal reprojectionを意味する。AI生成、DLSS/FSRバイナリ、frame generationは使用しない。

### 既知の制約
大きなzoom-outでは旧FRONTに存在しない外側地形をtemporal historyから生成しない。この場合は履歴を拒否し、新FAR BACK完成まで`TERRAIN GPU BUILDING`となる。地形の創作より正確性を優先する。

### Gate 4B受入基準
- CPU terrain draw = 0
- FAR以外の通常常設payloadを要求しない
- 同一view/content revisionでBACK全面再描画を連続しない
- 安定飛行時front/back swapがRepaint頻度へ張り付かない
- TRACK UPで小～中旋回中に履歴再投影が成立する
- zoom-in／中心移動中に旧FRONTがviewportを完全に覆う場合は黒画面へ落ちない
- stale body／terrain generation historyを絶対に再利用しない
- FAR BACK 100%未満をFRONTへswapしない
- GPU/SSD/decode failure 0
- Flight safety lane追加使用 0
