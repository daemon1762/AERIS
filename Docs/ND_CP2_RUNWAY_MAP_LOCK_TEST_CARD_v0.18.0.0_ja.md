# CP2 Runway Map Lock Hotfix 1 実機試験カード

対象：`AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayMapLockHotfix1_Source.zip`

## 必須証拠

画面録画を開始してから試験し、終了後に以下を提出する。

- `AERISFlightControl/Logs/AERISFlightControl.log`を含むMODログZIP
- 最新FlightData ZIP
- 可能ならKSP.log

## 1. 地形・滑走路Map Lock

1. KSC付近で飛行を開始する。
2. NDは`TRK UP`、`TERR Y DIRECT`、GPUは`AUTO`または`ON`。
3. Island Airfieldの島と滑走路を同時に表示する。
4. レンジを`20 → 40 → 80 → 160 → 80 → 40 km`へ変更する。
5. Island Airfieldへ接近し、島と滑走路が見える状態で90°以上旋回する。
6. Preview／Final切替やTile更新中も録画を継続する。

合格条件：

- 滑走路両端が島の実滑走路位置へ固定される。
- 滑走路記号が島の輪郭上を平行移動、回転、周回、段差移動しない。
- 地形Tile更新やレンジ切替で位置関係が飛ばない。
- 自機、地形、滑走路、選択ハイライトが同じ動きをする。
- ログに`geometryProjection=SPHERICAL_VERTEX`が存在する。

## 2. 滑走路選択・LAND

1. 動いていない滑走路記号をマウスで選択する。
2. 選択ヒット位置が表示記号と一致する。
3. KolaIsland RWY 34を選択してLAND ARMする。
4. 正しい進入側ではLOC／GS漏斗が滑走路中心線へ一致する。
5. 反対側だけ`LOC N/A / GS N/A / NOT ON APPROACH SIDE`となる。
6. `CLR SEL`で選択とLAND ARMが解除される。

## 3. 地形描画退行

AUTO、TOPO、RELを切り替え、次を確認する。

- REL色が高度差へ追従する。
- TOPO標高色が表示される。
- 水面は固定青色。
- 海岸線は通常等高線より太い。
- 黒い全面欠損、GPU例外、地形停止がない。
- 海岸塗り改善が維持される。

## CP2合格条件

上記をすべて満たし、ログ・録画に位置ずれ再発や操縦系退行がなければ、CP2閉鎖を再判定してCP3 Gate 6–8へ進む。
