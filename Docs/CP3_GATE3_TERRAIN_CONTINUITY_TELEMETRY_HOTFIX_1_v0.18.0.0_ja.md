# CP3 Gate 3 Terrain Continuity & Telemetry Hotfix 1

## 原因

Gate 3実機ログでは、GPU地形の要求品質coverageが約76%の状態でRenderTexture未描画領域がNDへ黒い扇形として露出した。従来はCPU地形を先に描いていてもGPU RenderTextureがalpha blendなしで重ねられ、透明領域のRGB黒が表示され得た。また短時間に5/10/20/40/80/160kmを切り替えると各中間距離が実要求となり、stale結果が増加した。

## 描画継続

- GPU RenderTextureはalpha blend有効でNDへ重ねる。
- 要求品質100%の完成フレームだけをcontinuity authorityとして別RenderTextureへ保存する。
- 同一天体・同半径・同orientation・同TRACK/NORTH状態で、15秒以内、range比0.80～1.25、中心移動が最大1500mまたはrangeの20%以内、TRACK UP角度差18度以内の場合だけ前完成フレームを再利用する。
- 互換continuityがなくCPU fallbackもない場合は、黒ではなく不透明な未確定地形色を下地にする。
- viewport coverage判定は11×11から25×25へ強化する。

## range要求集約

手動rangeは350msのdebounceを持つ。連打・ホイール操作中は最新値だけを保持し、確定時に一度だけ設定保存・terrain generation更新・旧GPU mesh要求取消を行う。

## AUTO品質分離

Airfield RegistryのLoadingCache／Discovering／Surveying／Validating／Staged中はTerrain AUTO品質・rate判定を停止する。終了後も5秒間holdし、一時的なstartup負荷をTerrain固有過負荷として蓄積しない。Landing ownerのコードは変更しない。

## CP3 telemetry

Performance CSVへResident CacheのRAM、LOD別常駐数、pin、hit/miss、decode、reject、evictionと、Predictive Forward Corridorの速度、旋回率、予測時間・距離・幅、点数、request/pin、LAND demandを追加する。continuity再利用・未確定地形下地・ageも追加する。

Resident snapshotはentry走査を伴うため1Hzに制限する。人が確認する`[CP3_TELEMETRY]`は10秒間隔、partial描画の`[CP3_TERRAIN_CONTINUITY]`は5秒間隔とする。

## 境界

AA、AP、PROTECT、FlightState、LAND、Integrations、Track B滑走路データ、Map DRAM Cacheは凍結する。Render Ready／GPU ReadyはGate 4まで実装しない。FULL BOOSTおよびFlight safety laneの追加使用は禁止する。
