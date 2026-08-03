# AERIS v0.18.0.0 CP2 Runway / Terrain Safety Hotfix 1 実機試験カード

対象パッケージ：`AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayTerrainSafetyHotfix1_Source.zip`

## 0. 試験原則

- 画面録画を必須とする。
- `AERISFlightControl.log`、FDR、Performance CSVを保存する。
- MOD空港のLAND表示は、滑走路実景と漏斗方向が一致するまで自動着陸用途へ使用しない。
- LANDはこの版でも観測専用である。

## 1. ビルド表示

ゲーム内表示が次であること。

`AERIS Flight Control v0.18.0.0 DEV CP2 RUNWAY TERRAIN SAFETY HOTFIX 1`

## 2. 認証キャッシュ更新

起動ログで認証アルゴリズム1680による再評価が行われること。旧キャッシュをそのままCERTとして採用しないこと。

## 3. MOD空港方向試験

対象例：KolaIsland RWY 34。

1. 空港と滑走路方向を選択する。
2. LAND UIの`HDG`、`Geometry bearing`、`heading error`を記録する。
3. 誤差が10度以内であること。
4. 自動修正された場合は`RECIPROCAL ENDPOINT ORDER AUTO-CORRECTED`が表示されること。
5. 実景の進入側とNDのLOC漏斗が一致すること。
6. 滑走路反対側へ回った場合、`LOC N/A / GS N/A / NOT ON APPROACH SIDE`となること。
7. 不整合方向ではARMボタンが無効化され、Track Tokenを取得できないこと。

合格条件：漏斗が滑走路の正しい進入側へ伸び、逆側で数値誘導を出さない。

## 4. GPU REL高度追従試験

KSC周辺など標高の低い陸地を使用する。

1. `TERR AUTO`または`REL`、`GPU AUTO`で開始する。
2. 地表付近、地表+100m、+400m、+700m、+1500mを通過する。
3. 同じ低地が概ね赤→黄→緑→暗緑へ変化すること。
4. 高度を上げても赤のまま固定されないこと。
5. ログの`[ND/TERRAIN_ALIGN]`に`colourSource=EXPLICIT_VERTEX`、`effectiveMode=Relative`、機体ASLが記録されること。

## 5. GPU TOPO試験

1. `TOPO`へ固定する。
2. 低地・山地を含む範囲を5/20/80/160kmで表示する。
3. 標高に応じて緑・黄土・茶・白系へ連続変化すること。
4. REL用の赤警戒色へ固定されないこと。
5. Range変更中もCPU fallbackまたはPreview下敷きが残ること。

## 6. 陸海・海岸線試験

1. KSC海岸、Island Airfield、MOD島嶼空港を表示する。
2. 水面が常に青であること。
3. 海岸線より水面側へ赤・黄・緑・茶色がにじまないこと。
4. 等高線が水面へ突き抜けないこと。
5. 海岸線が通常等高線より明確に太いこと。
6. Tile境界で海岸線が大きく途切れたり二重化しないこと。

## 7. GPUモード試験

- `AUTO`：対応環境ではGPU表示。能力不足時はCPU fallback。
- `ON`：GPUを強制試行。
- `OFF`：CPU表示。

各切替後も滑走路・自機・LAND・航法記号が消えないこと。

## 8. 既存回帰

- `CLR SEL`で空港・滑走路選択とLAND表示を解除できる。
- `TERR Y DIRECT`で地形と自機・滑走路が一致する。
- LOC/GS線は画面外端点を含んでもクリッピング表示される。
- AP/BANK等の操縦挙動に変化がない。

## 9. 提出物

- 画面録画
- `AERISFlightControl.log`
- FDR/CVR
- Performance CSV
- 使用空港・滑走路・GPUモード・地形モード・レンジの時系列

