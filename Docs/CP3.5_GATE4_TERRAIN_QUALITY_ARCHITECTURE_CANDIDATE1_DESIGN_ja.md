# AERIS v0.18.0.0 DEV CP3.5 Gate 4 Terrain Quality Architecture Candidate 1

## 目的
Gate 3 Hotfix 1で確定したOwnship / Prediction / Range AuthorityとPalette V3を維持したまま、ND地形品質を「見た目の改善」と「負荷抑制」を両立する三段階へ再編する。

## 品質モデル

| 品質 | 実地形データ | 仮想/表示品質 | PQS方針 |
|---|---|---|---|
| LOW | REAL 33×33 | Native | 現行基盤。追加取得なし |
| MIDDLE | REAL 33×33 | VIRTUAL 65 class | LOWと同じ実データを再利用。品質目的の追加PQSなし |
| HIGH | REAL 33×33 foundation + bounded REAL 65×65 | VIRTUAL 129 class + sparse exact | 近い可視FARだけREAL65。負荷時は縮退 |

### LOW
33×33を標準基盤としてそのまま描画する。プリロード資産、RAM resident基盤、既存の安定性をそのまま利用する。

### MIDDLE
33×33の実データを維持し、GPU側のRenderTexture解像度を軽く引き上げて輪郭・海岸線・contourなどのラスタライズ品質を改善する。33→65のためにPQSを再問い合わせしない。全FAR tileを65×65 meshへ補間してCPU/worker負荷を4倍化する方式も採用しない。

Candidate 1のRenderTargetScaleは1.25。これは「65×65という論理解像度を、そのままNDピクセル数2倍で力任せに描く」ことを避けるための負荷制限である。実機結果を見て1.25～1.5範囲の再調整は可能。

### HIGH
全域をREAL65/REAL129にしない。まず33×33 FAR foundationを確実に表示し、完全な33×33 tileがRAMに存在する場合だけ、近い可視FARの小数tileをREAL65へ昇格する。

上限は最大4 tile、40 km超は最大3、80 km超は最大2。AERIS自身のMain Thread/PQS/worker負荷が上がった場合は最大1またはREAL65生成自体を停止する。

完成したREAL65 tileだけをworkerでVIRTUAL129へ再構成する。33×33しか無いtileを129×129 geometryへ膨らませてHIGH扱いにはしない。既存Route/Local exact payloadがある場所は従来通りSparse Exactとして重ねる。

REAL65はruntime transientとし、標準preload DBへ保存しない。これによりpreload容量・SSD I/O・起動時resident資産を肥大化させない。また65生成途中の25/50/75% tileはRAMの完全33 tileを置換せず、65が完成した瞬間だけatomicに置換する。

## `2^n + 1` 系列
33→65→129はセル数32→64→128となり、LOD、補間、downsample、境界整合に扱いやすい。LOW/MIDDLE/HIGHの設計値としてこの系列を採用する。

## Unified ND World Surface
Terrain、coastline、contour、およびworld-locked navigation geometryは同じexact world surfaceへ描画する既存Gate 3.5系経路を維持する。Ownshipとpredictionは高速live overlayであり、world surface中心の遅延によって移動させない。

Candidate 1ではTemporal Presentation Authorityは再有効化しない。まず品質体系とreal65 sparse refinementをruntimeで安全確認した後、描画更新率改善を次候補として評価する。

## 性能原則
- LOWは現行33×33基盤より重くしない。
- MIDDLEはPQS負荷をLOWとほぼ同等に保つ。
- HIGHは選択しても全域の計算量が16倍になる設計にしない。
- HIGH real65はvisible nearest / range / runtime loadで必ずbounded。
- preload DBは33×33基盤のまま。
- `virtual_builds`、`high_refine_requested/completed/safety_skips`をログで確認可能にする。

## Candidate 1でまだ合格扱いにしない項目
- 実KSPでのFPS改善量
- RenderTargetScale 1.25/1.50の最終値
- HIGH real65の最適tile数
- range変更時BUILDING時間の最終短縮
- temporal/keyframe間の滑らかな60fps級presentation

これらはruntime実測に基づいて調整する。
