# CP3 Gate 4C — Virtual Detail / Exact Microtile / Runway End Labels

## 目的

Gate 3.1以降の「FARを唯一の常設Terrain base payloadとする」方針を完成させる。
通常巡航ではRoute/Local payloadを新規生成せず、FAR height fieldから表示用の
VIRTUAL ROUTE / VIRTUAL LOCALを再構成する。既存SSD上のexact detailとLAND要求で
生成されるLocal/Landだけはauthoritative microtileとして上書きする。

## Virtual Detail

- LOW / 遠距離: FAR DIRECT
- MEDIUM 80 km以下: VIRTUAL ROUTE（FAR 33x33 → 最大65x65）
- HIGH 20 km以下: VIRTUAL LOCAL（FAR 33x33 → 最大97x97）
- HIGH 20–80 km: VIRTUAL ROUTE
- 80 km超: FAR DIRECT
- LAND profileは40 km以下でVIRTUAL LOCAL

再構成はGeneralCompute worker内のpure-data処理で、CPUによるND描画ではない。
海陸分類はcategoricalとして扱い、海岸境界でheightを混合しない。全4隅が同一分類で
有効なcellだけbilinear補間し、海岸・invalid境界は最寄りの同一分類authoritative sample
を使う。新しい山・谷・海岸線を推測して作らない。

## Exact Detail Microtile

Route/Localの常設・全球生成は復活させない。

- 既にRAM/SSDに存在するexact Route/Localはviewport bridgeから利用可
- LAND ARM中の選択滑走路端はLocal/Land exact要求を許可
- exact tileはFAR virtual detailより後に描画し、地形authorityとして上書き

## GPU-only契約

- CPU terrain draw = 0
- CPU safety fallbackなし
- Temporal GUI.matrix warpは表示authorityに戻さない
- 現在projectionで完成したGPU FRONTのみ表示

## 滑走路端ラベル

旧36px端点ラベルにfull direction nameを描くことで発生していた`...`を廃止する。

- 5 / 10 / 20 km: 各端に滑走路designationのみ表示（例 09 / 27 / 09L）
- 40 / 80 / 160 km: 端点文字は非表示
- 空港名中央ラベル、滑走路線、色、選択状態は変更しない

## 凍結境界

AA/AP/PROTECT、LAND制御本体、Track B runway data、Map DRAM、Preload Database、
起動時airport/runway NONE、Gate 4B Geometry Integrityのexact projection authorityは変更しない。
