# CP2 Closure Candidate 1 実機試験カード

対象：`AERISFlightControl-v0.18.0.0_DEV_CP2_ClosureCandidate1_Source.zip`

## 1. MOD滑走路LAND

1. KolaIsland RWY 34を選択する。
2. 空中でLAND ARMする。
3. `CERT`とARM受理後、`RUNWAY GEOMETRY INVALID`が出ないこと。
4. 正しい進入側でLOC漏斗とGS縦断漏斗が滑走路中心線へ一致すること。
5. 反対側へ回った場合だけ`LOC N/A / GS N/A / NOT ON APPROACH SIDE`となること。
6. `CLR SEL`で選択とLAND ARMが解除されること。

## 2. 地形塗り品質

AUTO、TOPO、RELの順に確認する。

- 水面は固定青色。
- 陸警戒色・TOPO色が海岸線の水側へ明瞭にはみ出さない。
- 海岸線と塗り境界が同じ位置を通る。
- 旧版より三角形状の濃淡むらが減っている。
- 海岸線は通常等高線より太い。
- RELは上昇に応じて赤→黄→緑→暗緑へ変化する。
- 地形位置追従、Preview→Final、レンジ切替に退行がない。

## 3. 証拠

画面録画と次を提出する。

- `AERISFlightControl/Logs/AERISFlightControl.log`
- 最新`FlightData/*.zip`
- `PluginData/AirfieldCertificationCache.cfg`

## 合格条件

- MOD滑走路の正しいLOC／GS表示：PASS
- 誤方向・不正Geometryの安全拒否：PASS
- 地形の機能要件：PASS
- 塗りむら・はみ出しが旧版より明確に改善：PASS
- GPU例外、全面欠損、操縦系退行：なし

全条件を満たせばCP2を閉じ、CP3 Gate 6–8へ進む。
