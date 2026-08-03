# AERIS12 MOD Runway Survey Design

## データ境界

`02_Current_Mod_Runway_Survey_Catalog.cfg`は、提供された現行`ModuleManager.ConfigCache`から抽出した固定翼Runway候補の許可リストである。KK全施設を滑走路とみなすデータベースではない。

## 測量手法

### StaticBounds

1. KSP-RO Kerbal Konstructsを反射で検出する。
2. カタログのUUID、LaunchSite名、Group、パス、モデルを照合する。
3. `Category = Runway`を再確認する。
4. `LaunchPadTransform`のforwardを局地水平面へ投影する。
5. 対象Staticが既に読込済みなら非Trigger Collider/Renderer boundsを使う。未読込ならモデルPrefabのMesh boundsをinstance座標へ変換する。Registry走査で全Staticを強制Activateしない。
6. 長さ、幅、縦横比を制限値と照合する。
7. 中心線上の両端を緯度・経度・標高へ変換する。
8. 両方向を別Approachとして生成する。

### PairedThresholds

同一Pair Keyを持つ2つのLaunch Transformだけを対向Thresholdとして使う。現在はKSC ExtendedのTSC Runway 09/27に使用する。

### ManualRequired

X字形滑走路、汎用SpawnPoint、形状が一意に定まらないモデルは自動測量しない。明示した両Thresholdが追加されるまでLAND ARMを許可しない。

## ILS Runway-Only

非Runway施設はProvider分類と誤分類防止のためRegistry内部に残す。一方、LAND UIとLAND/ILS NDからは完全に除外する。ILS中に描画する施設Geometryは現在選択中の滑走路だけである。

## 将来の昇格

Runtime Survey Geometryは第一関門の目視検証用。実制御へ進む前に各物理滑走路の両方向を確認し、必要なら手動Threshold補正を追加する。
