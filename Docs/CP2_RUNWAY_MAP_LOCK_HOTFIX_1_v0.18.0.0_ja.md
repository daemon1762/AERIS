# AERIS v0.18.0.0 DEV CP2 Runway Map Lock Hotfix 1

## 目的

CP2 Closure Candidate 1の実機録画で、Island Airfieldの滑走路記号が島の地形輪郭に固定されず、飛行・旋回・LOD更新に伴って地形上を移動して見える不具合が再発した。本Hotfixは、地形と滑走路を同一の球面地理座標系で描画し、位置関係を固定する。

## 根本原因

GPU地形Tileは、Tileの四隅から求めた画面上の長方形へ正規化メッシュを押し込んでいた。粗いGlobal／Far Tileでは球面上の内部点が長方形補間位置と一致せず、島などの地形内部形状が本来の緯度・経度から数十m～数kmずれる。Tileの置換、ND中心移動、TRACK UP回転、レンジ変更により誤差の見え方が変わるため、正確な地理座標で描かれる滑走路記号が地形上を動いて見えた。

加えて滑走路記号は、準備Snapshotの接平面座標から現在中心の接平面座標を減算しており、Frame更新境界で残留誤差や段差が生じる余地があった。

## 実装

- 各地形頂点の正規化Tile座標を緯度・経度へ復元し、単位球面座標をキャッシュする。
- 描画時に現在のND中心を基準とする方位等距離投影で、各頂点を画面座標へ再投影する。
- Global／Far／Near／Finalの全LODで同じ投影を使う。
- 地形メッシュの四隅長方形TRSを描画変換として使用しない。
- ND中心が0.25px相当以上移動、レンジ変更、anchor変更、天体変更のときだけ投影頂点を更新する。
- 動的更新するMeshへ`MarkDynamic()`を適用し、CPUアクセスを維持する。
- 滑走路両端を実緯度・経度のままPrepared Snapshotへ保持する。
- 描画とマウス選択ヒットテストの両方で、現在のND中心から滑走路端点を毎回測地投影する。
- 地形、滑走路、LAND漏斗、滑走路選択を同じ中心・同じ地理基準へ揃える。
- 診断ログへ`geometryProjection=SPHERICAL_VERTEX`を出力する。

## 維持事項

- GPU TOPO／REL明示頂点色、水面固定青、陸海分離、太い海岸線を維持。
- MOD滑走路Heading整合、安全拒否、正しい進入側判定を維持。
- `CLR SEL`、ILS線分クリッピング、`TERR Y DIRECT`を維持。
- AP／BANK／HDG／PITCH／V/S／ALT／ACC／VEL／Ground Stabilityは変更しない。
- LANDは観測・計画・表示専用で、操縦権限を追加しない。
- legacy NAVは復活させない。

## CP2状態

Closure Candidate 1は撤回。Runway Map Lock Hotfix 1の実機録画・ログ受入が完了するまでCP2はOPEN、CP3 Gate 6–8と新NAVはBLOCKED。
