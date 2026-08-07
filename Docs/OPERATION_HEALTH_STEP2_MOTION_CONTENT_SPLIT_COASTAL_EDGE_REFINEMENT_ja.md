# Operation Health Step 2 / Motion-Content Split + Coastal Edge Refinement

Hotfix 3/4で確立した固定10 Hzの正確なND presentationとLOADING/READY分離を維持しながら、毎10 Hz tickで不要だったterrain content maintenanceを分離する。

## Motion / presentation clock
- fixed 10 Hzを維持
- 現在位置・heading・rangeに対するexact projection
- BACK render / atomic FRONT swap
- terrain FRONTとownship/vector/fanの同期

## Content maintenance
以下の場合だけ実行する。
- content snapshot未成立
- TerrainGeneration変更
- style / range / orientation / anchor変更
- TRK UP headingが3度以上変化
- cached content centerから `max(100 m, range×2%)` 以上移動
- worker completed resultが存在
- BUILDING/pending中の0.20秒 bounded retry

通常のmotion-only tickでは、`CaptureVisible / DrainCompleted / Entry resolve / schedule / foundation readiness / prune`を再実行せず、既存のvisible tile / prepared Entry snapshotをfresh projectionで再描画する。

## Coastal Edge Refinement
- persisted HD payloadは129×129 / format v2のまま
- PQS追加サンプルなし
- land/water class signは一切変更しない
- 3×3近傍からpresentation-only boundary confidenceを作成
- 既にland/waterが交差するedgeのsub-cell crossingだけを局所平滑化
- coastline lineとsparse land/water fillが同一crossing authorityを共有
- persisted Candidate11 coastline segmentsはfallbackとして保持

## Frozen authorities
- Candidate11 contour level budget = 96
- sparse coastal parent safety rail = 256
- RenderTexture = ARGB32 / Bilinear
- fixed authoritative ND = 10 Hz
- Hotfix 4 stale-FRONT loading backdrop / requested-view READY分離

KSP runtime validation前はRelease候補にしない。
