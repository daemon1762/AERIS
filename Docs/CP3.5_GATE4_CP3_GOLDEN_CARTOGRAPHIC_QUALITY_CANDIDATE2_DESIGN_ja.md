# AERIS v0.18.0.0 CP3.5 Gate 4 — CP3 Golden Cartographic Quality Candidate 2

## 目的
Gate 4 Candidate 1は、LOWがREAL33をそのまま表示し、MIDDLEも実質RenderTexture拡大だけだったため、地形が巨大なポリゴン塊に見え、地図としての可読性がCP3最終盤より大きく後退した。またHIGHの一時REAL65完了ごとに`terrainGeneration`を進めることでExact FRONTを連続失効させ、FAR基盤完成後にも青一色/BUILDINGへ落ちる経路があった。

Candidate 2では、ユーザー提示のCP3最終盤スクリーンショットを**Golden Visual Reference**とし、その品質をLOWを含む全モードの下限とする。

## CP3技術の直接転用
CP3 Gate 4Cで実績のある、FAR REAL33から追加PQSなしで作る純粋データ再構成を復活させる。

- VIRTUAL ROUTE: 33x33 -> 65x65
- VIRTUAL LOCAL: 33x33 -> 97x97
- 海/陸はcategorical。境界を高さ平均で混ぜない。
- 4隅が同一classの場合のみbilinear elevation補間。
- 海岸近傍ではnearest same-class heightを使い、架空の海岸・山を作らない。
- coastlineはfill meshと同じtriangle diagonalから生成。
- 海面を跨ぐ辺は`AERISTerrainCoastlinePolicy.CrossingFraction`でsub-cell交点を求める。
- contourは仮想再構成後のgeometryから生成する。

## 品質モード
### LOW
- 実地形authority: REAL33
- <=20 km: CP3 VIRTUAL LOCAL97
- 20..80 km: CP3 VIRTUAL ROUTE65
- >80 km: CP3 FAR DIRECT相当 + 1.25x Hi-DPI surface
- 追加PQSなし

LOWは「計算用地形解像度が低いモード」であって、「粗い地図を許すモード」ではない。

### MIDDLE
- 実地形authority: REAL33
- <=20 km: VIRTUAL LOCAL97 + 1.25x surface
- 20..80 km: VIRTUAL ROUTE65 + 1.30x surface
- >80 km: VIRTUAL ROUTE65 + 1.30x surface
- 追加PQSなし

### HIGH
- 全画面fallbackは常にCP3 Golden品質（near=97、far=65）。
- 重要FAR tileのみREAL65をbounded transient refinementとして生成。
- complete REAL65のみVIRTUAL129へ再構成。
- partial REAL65は表示にcommitしない。
- REAL65はRAM-onlyで既存33x33 preload DBへ保存しない。
- 40 km超は最大3 tile、80 km超は最大2 tile、通常最大4 tile。
- AERIS自身のMain Thread/PQS/worker負荷が高い場合は縮退/停止する。

## Candidate 1からの重要修正
Candidate 1ではtransient REAL65が完成するたびに`terrainGeneration++`が走り、Exact FRONTのworld-generation authorityまで変化していた。Candidate 2ではtransient refinementを地理世代変更とみなさない。

- 標準REAL33更新: `terrainGeneration++`
- transient REAL65更新: `terrainGeneration`を変更しない
- GPU側の新しいdetail到着: 既存`gpuContentRevision`でBACK rebuildを要求

これにより、品質向上用の局所tileがExact FRONTを連続破棄して青一色になる経路を切る。

## 凍結する安全境界
- Exact FRONTのみpresentation authority。
- Temporal PresentationはShadow/Quarantineのまま。
- `GUI.matrix` temporal warpは禁止。
- Ownship live anchorは固定。
- Prediction vector/tickはownship-relative。
- 異なるrangeの古いFRONTを現在rangeへ流用しない。
- 全可視FARのREAL65/129 PQS化は禁止。

## 品質の合格基準
ユーザー提示のCP3最終盤画像を基準とし、LOWでも以下を満たすこと。

- 20 kmで密な地形輪郭/等高線が読める。
- 40/80 kmで巨大なセル塊ではなく地図として地形が連続して見える。
- 160 kmで海岸線・島・陸塊形状が明確に読める。
- FAR ready後に青一色へ落ちない。

MIDDLE/HIGHはこの下限からさらに精細化する。LOWを粗くして差を作ることは禁止する。


## 同梱Golden Reference
`Evidence/CP3_GOLDEN_VISUAL_REFERENCE/` に受入基準の原画像3枚を同梱する。runtime判定ではこの原画像と同一rangeの画面を直接比較する。
