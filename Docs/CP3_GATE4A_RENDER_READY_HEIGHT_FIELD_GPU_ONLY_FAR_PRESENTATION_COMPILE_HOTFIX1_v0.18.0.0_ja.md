# AERIS v0.18.0.0 CP3 Gate 4A Compile Hotfix 1

## 修正理由

Gate 4AでCPU terrain raster workerをC# projectから除外した際、`AERISTerrainAwareness`に旧CPU raster専用のgrid-snapshot APIが残り、非コンパイルtombstone側にしか存在しない型を参照していた。Mono/xbuildでは`CS0246`となる。

## 修正

旧CPU raster専用`TryCaptureGridSnapshot` APIをcompiled sourceから削除した。退役workerやsnapshot型をruntimeへ復活させない。Gate 4Aの描画authorityは引き続きGPU-onlyであり、CPUはtile payloadのdecode／render-ready height field構築までを担当する。

## 境界

- CPU terrain presentationは復活させない。
- FAR FRONT/BACK、Render Ready、GPU Readyの契約は変更しない。
- Map DRAM、Preload DB、AA/AP/PROTECT/LAND/Track Bは変更しない。
- build/UI/AVC表記をCompile Hotfix 1へ更新する。
