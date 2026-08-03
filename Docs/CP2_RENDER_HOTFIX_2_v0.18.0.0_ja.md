# AERIS v0.18.0.0 DEV CP2 Render Hotfix 2

## 実機証拠

提出されたCP2実機環境ではGPU backend自体は正常で、`terrain_gpu_failures=0`だった。しかし次の時点で、完成していないtile集合がHD layerとして全画面へ昇格していた。

- 5km TRACK UP：RAM tile 1、pending 35、sampling remaining 18前後
- 20km PLAN：RAM tile 3、pending 60、sampling remaining 912前後

透明RenderTexture上に1～3枚だけ描かれ、残りがNDの黒背景になったため、回転したtile境界が大きな黒い三角形として見えた。GPU故障やpalette異常ではない。

## 修正

1. 11×11のviewport coverage gateを追加。
2. TRACK UPでは表示回転の逆変換後に、全sampleが準備済みtileで被覆されることを確認。
3. coverage未完成のHD layerは描画へ昇格させない。
4. 現在地表示では、完成済みbounded CPU terrainを維持。
5. 遠方PLANでは現在地中心のlegacy gridを誤表示せず、`TERRAIN TILE COVERAGE LOADING`を表示。
6. Global tileをCritical bootstrapとして最初に計画。
7. 同一priorityではGlobal→Far→Route→Local→LANDの順に広域coverageを先行。
8. `NavigationDisplayProfiles.cfg`のConfigNode generic rootを正式受理し、temporary round-trip failureを修正。

## 境界

- AP/BANK変更なし
- LAND制御権限なし
- 旧NAV不在
- 新NAV BLOCKED
- GPU結果を安全判定へ使用しない
