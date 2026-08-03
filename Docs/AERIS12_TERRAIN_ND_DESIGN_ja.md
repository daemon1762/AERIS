# AERIS12 Terrain Moving Map ND 設計 — v0.16.2.0

## 目的

NDを通常飛行、将来NAV、独立LAND/ILSで共有する航空用Moving Mapとする。地形は共通ベースレイヤーで、水面は青。NAVおよびLANDはOverlay/Focusを切り替えられる。

## 表示優先順位

```text
LAND/ILS guidance active  LAND
future NAV active         NAV
otherwise airborne        TERRAIN
```

旧NAVコード、旧NAV制御、Waypoint着陸は復元しない。

## レイヤー構造

```text
Terrain Layer      地形、水面、等高線、海岸線、静的施設
Navigation Layer   経路、滑走路、LOC、GS
Symbology Layer    自機、Track/Heading、偏差、短い状態表示
```

Terrainは低頻度でもよいが、航法と飛行シンボルは高頻度を優先する。

## 品質プリセット

- ECO：13×9、最大25 PQS点/s
- BALANCED：17×11、最大60 PQS点/s
- HIGH：21×15、最大120 PQS点/s
- ULTRA：25×17、最大240 PQS点/s
- AUTO：実測負荷で上記を段階選択

PQS取得はメインスレッドの時間ベース・トークン予算で制御し、各プリセットの1フレーム上限を超えない。Pending grid完成後にActive gridへ一括交換する。

## 適応制御

AUTOは以下を指数移動平均で観測する。

```text
Frame time
ND main-thread time
PQS sample time
Raster worker time
Latest-request replacement/backlog
```

負荷悪化では更新率を先に下げ、その後品質を下げる。回復は更新率25秒、品質55秒程度の安定を要求する。PC型番やGPU名は判断に使わないため、巨大機体、大量MOD、場面負荷も反映できる。

## スレッド境界

### Main Thread

- Vessel、CelestialBody、PQS、TerrainAltitude
- KSP/Unity API
- Texture2D upload
- IMGUI draw

### Raster Worker

- float/byte snapshotのみ受領
- Relative terrain color
- Water blue
- Contour/coastline raster
- Focus threat raster

ワーカーはBelowNormalの専用Background Thread。最新1要求だけを保持し、古い世代の完成結果は破棄する。飛行制御スレッドやAA学習スレッドと共有しない。

## 相対地形表示

```text
clearance > 600 m   low terrain
300–600 m           near terrain
30–300 m            caution
<= 30 m             warning
water                blue
```

Focusでは十分低い地形を隠し、危険地形のみを残す。

## Look-aheadと警告

Ground Speed、Vertical Speed、前方地形から最小予測クリアランスを算出する。NDには警告文を重複表示せず、FDIへ最重大状態を赤文字表示する。

```text
TERRAIN
TERRAIN AHEAD
PULL UP
```

本機能は警告・観測のみで、自動回避操縦は行わない。

## UI

ND本体は地図主体・低文字量を維持する。

```text
header    TERR/LAND + TRK/N + RWY
controls  [view] [range] [-] [+]
```

SYSTEM > OPTIONSで品質と更新FPSを選択し、AUTO時は現在の実効品質と各レイヤーFPSを確認できる。
