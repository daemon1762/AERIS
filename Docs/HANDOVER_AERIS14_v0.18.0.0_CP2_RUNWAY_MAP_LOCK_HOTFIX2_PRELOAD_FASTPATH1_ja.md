# AERIS14 引き継ぎ — CP2 Runway Map Lock Hotfix 2 + Preload Fast Path 1

## 現在地

CP2はOPEN。前版Runway Map Lock Hotfix 1は、実機動画再確認により滑走路と地形の相対滑りが残存していたため不合格。

## 今回の根本原因

NDは横方向と縦方向の縮尺が異なる。GPU地形は縮尺適用後の座標を通常回転し、GUI滑走路はメートル空間で回転していたため、TRACK UPのheading変化で相対位置が変化した。

## 今回の実装

- `AERISNdMapProjection`を新設。
- GPU地形とGUI滑走路を共通の球面・メートル・異方性補正投影へ統一。
- 旧`ResolveMapRotation`とterrain専用`ProjectionContext`を廃止。
- 選択滑走路端点でGPU／GUI投影誤差を計測。
- 1px超ならGPU terrain commitを拒否。
- Preload速度にBALANCED／FAST／MAXIMUMを追加。
- 実測PQS sample costによる動的budget。
- Preload Builderの25/50/75%中間Tile複製を省略。
- 隣接Tile境界のPQS Sampleを最大131,072点の有界Cacheで再利用。
- Cache hitはPQS query tokenを消費せず、重複照会を削減。
- 同一Chunk完成Tileを最大8枚／350msでBatch保存。
- manifest保存をBatchあたり一回へ削減。
- 解析済みChunkを64MiB RAM Cacheへ保持。
- CSVとUIへFast Pathテレメトリを追加。

## 凍結確認対象

```text
Autopilot/AERISBankDirector.cs
Autopilot/AERISHeadingDirector.cs
Autopilot/AERISPitchDirector.cs
Autopilot/AERISVerticalSpeedDirector.cs
Autopilot/AERISAltitudeDirector.cs
Autopilot/AERISAccelerationDirector.cs
Autopilot/AERISVelocityDirector.cs
Protect/AERISGroundStability.cs
```

## 未実施

- Native Mono C# compile
- KSP実GPU描画
- Island Airfield／MOD空港の録画付きMap Lock試験
- BALANCED／FAST／MAXIMUMの実測比較
- KSP再起動後のBatch DB再利用

## 再開位置

1. ソースZIPのSHA-256確認。
2. 全CP2静的受入。
3. Native build。
4. テストカードに従い録画・ログを取得。
5. 全PASSならCP2 CLOSE、CP3 Gate 6へ進む。
