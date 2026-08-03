# CP3.75 ND Runtime Stabilization / Settings Consolidation Candidate 2

## 目的
Candidate1で復旧したlate-CP3 Golden地図品質を維持したまま、高速飛行時に顕在化したpresentation再描画過多、経路予測線/ローカライザ線の座標不整合、無効な品質・更新設定を整理する。

## 変更境界
このCandidateはND表示・Terrain presentation・ND設定に限定する。AA/AP/PROTECT/FlightState/Landing authority/Integrations/Recording等の非ND動作は変更しない。

## 1. Terrain Qualityのリストラ
- Runtime quality authorityは `LOW` だけ。
- `AUTO / MEDIUM / HIGH` はruntime選択肢から廃止。
- 旧CFG互換のためenum/parserは残すが、読み込み直後にLOWへmigrationする。
- `LAND` terrain-quality preset/capabilityも無効化する。LAND着陸機能そのものは削除しない。
- UIは `LOW (LOCKED)` ボタンだけ残し、押せない状態とする。
- LOWは「低画質化」を意味せず、Candidate1で復旧したlate-CP3 cartographic qualityを品質下限とする。

## 2. ND Updateのリストラ
- `AUTO / 20 / 30 / 45 / 60` のユーザー選択を廃止。
- UIのND update selectorを削除。
- runtime authorityを `FixedNavigationDisplayUpdateHz = 10` に固定。
- legacy enum/parserは古いCFG migration専用として残す。
- Tile planningはユーザーND cadenceとは分離し、Candidate2では2 Hz固定。

## 3. 高速forced recovery抑制
Candidate1は160 km時に240 m以上centerが動くだけでdirect FRONT不一致となり、latched FRONTを使う前に同期BACK renderを強制していた。2100 m/sでは約0.114秒で境界を超える。

Candidate2では:
- normal recenterはscheduled BACK refreshのみが担当。
- last complete FRONTを先に継続表示し、そのFRONTのprojectionを全world-fixed layerへ公開。
- forced recoveryは「表示可能なFRONTが無いがFAR foundationはREADY」という異常時だけ。
- projection refresh ageを0.50秒へ短縮し、latched mapのcenter lagをboundedにする。
- temporal GUI.matrix warpは禁止のまま。

## 4. Track Vector座標修正
Candidate1では始点だけactual ownship、終点/tickはmap center基準だった。latched FRONTでownshipがcenterから離れると、線長が伸縮・崩壊する。

Candidate2ではownshipのpresented-center相対East/Northを先に求め、endpoint/tickへ加算してからprojectionする。

時間表示は固定60秒horizon + range clippingへ変更する。160 km / 2100 m/sでは30秒tickが表示可能で、45/60秒はrange外なら描かない。

## 5. LAND localizer/funnel座標修正
`AERISRunwayObservation`のThreshold/Opposite座標はcurrent ownship相対。Candidate1はこれをmap-center相対として直接描画していた。

Candidate2ではpresented map center→ownship offsetを加え、threshold/opposite/funnel全点を同一projection authorityへ変換する。

## 6. Coastline continuity
Golden coastline generation/mesh policy自体は変更しない。まず高速時の過剰BACK render/swapを除去し、同一complete FRONTを安定して提示することでstroke continuityを回復させる。Golden coastline生成を性能目的で粗くすることは禁止。

## Runtime合格条件
- 160 km / 2100 m/sでforced_recoveryが速度比例で増え続けない。
- 予測線がlatched map center移動で伸縮・崩壊しない。
- 160 km / 2100 m/sで30秒tickが残る。
- coastlineがフレーム単位で消失/再出現しない。
- TRK UP / NORTH UPでterrain/runway/ownship/vectorが同一projection authority。
- UIはTerrain quality=LOW LOCKEDのみ、ND update selector無し。
- Candidate1 Golden contour/coastline/map readabilityを下回らない。
