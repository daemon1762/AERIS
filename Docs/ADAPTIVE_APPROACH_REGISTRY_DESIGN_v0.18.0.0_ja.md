# AERIS v0.18.0.0 Adaptive Approach Registry 設計

## 目的

AERIS内の「ILS登録」を、固定3°の一本線ではなく、滑走路方向ごとに複数の認証済み進入方式を保持できるApproach Procedure Registryへ発展させます。名称上ILS互換表示を持っても、曲がったLocalizerは作りません。

## 不変条件

1. Final Localizerは滑走路中心線に一致する。
2. mandatory straight final内の障害物は、横へ迂回して隠さない。
3. 障害物回避はOuter Leg／Doglegで行う。
4. 進入方式とGo-Around corridorを同時に成立させる。
5. GPU terrain表示を安全判定の唯一の根拠にしない。
6. Gate 0では飛行制御権限を持たない。

## グライドパス候補

- 通常候補：2.5～4.0°
- 障害物対応：4.0～5.0°
- 条件付き：5.0～6.0°。機体がsteep approach対応の場合のみ
- 既定優先値：3.0°
- 探索刻み：0.25°

探索は3°に最も近い候補から開始します。障害物がないのに不必要な急角度を選ばず、低い角度が安全でなければ節度ある範囲でのみ増角します。

## 縦方向profile

```text
Outer Descent
→ Continuous Transition
→ Constant-angle Stabilized Final
→ Flare Gate（表示・計画のみ）
```

低高度で角度を急変させず、最終進入は一定角を優先します。将来、機体別V/S、pitch余裕、失速余裕、flare距離をPlanningLimitsへ追加します。

## 横方向候補

```text
DIRECT
STEEP DIRECT
DOGLEG LEFT
DOGLEG RIGHT
```

Doglegはmandatory straight finalより外側に障害物が存在し、実際のouter-to-join segment corridorが地形・障害物clearanceを満たす場合だけ生成します。

## Obstacle Snapshot契約

各sampleは滑走路方向座標で表します。

- `AlongTrackMeters`: thresholdから外向き、reciprocal final方向を正
- `CrossTrackMeters`: inbound localizer右側を正
- `TopElevationMeters`
- `HorizontalRadiusMeters`
- `IsTerrain`
- `SourceId`

SnapshotはDirectionStableId、TerrainSignature、ObstacleSignature、BodyRadius、missed-approach安全情報、generationを保持します。Unity objectは含めません。

## Gate 0と後続Gateの境界

Gate 0:

- データモデル
- 純CPU候補生成
- Registry snapshot
- safety invariantのモデル試験

後続Gate:

- Terrain／Static／建造物の3D corridor snapshot生成
- Performance RuntimeのSafety/LAND laneへの投入
- ND平面図・縦断profileへの表示
- 機体性能Provider
- Shadow Guidance
- 限定制御

Shadow Guidance／制御接続は、LAND総合受入前には有効化しません。
