# AERIS v0.18.0.0 DEV CP2 Field Render Consistency Hotfix 1

## 1. 位置づけ

本チェックポイントは、`DEV CP2 PRELOAD STATUS TOOLBAR COMPILE HOTFIX 1`の実KSPログで確認された、ND地形レイヤーの表示整合性不具合を修正するDEV版である。

正式版、RC、新NAV、LAND自動制御ではない。修正後の実KSP再試験が完了するまでCP2は未合格であり、CP3へ進んではならない。

## 2. 添付ログで確認した現象

対象セッション：

- `2026-07-24_114853_session.log`
- `2026-07-24_114853_520_performance_runtime.csv`
- `2026-07-24_115714_365_000001_ARA-2_95d62e2f.zip`

Performance CSVは902行で、11:48:54から12:04:28までを記録している。

最終行では次を確認した。

| 項目 | 値 |
|---|---:|
| first tile visible | 18.6768 ms |
| viewport coverage | 1.0000 |
| GPU coverage | 0.9917 |
| desired / visible / pending | 18 / 10 / 2 |
| sampling remaining | 2 |
| preview / final generated | 43 / 28 |
| generated | 433 |
| stale / obsolete cancelled | 1353 / 1341 |
| stale results discarded | 51 |
| generation fallback | 19 |
| GPU failure | 0 |
| DB CRC / hash failure | 0 / 0 |

この値は、GPU故障やDB破損ではなく、Range・表示モード変更中の世代管理、途中Tileの完成判定、旧描画との合成、coverage判定に問題があったことを示す。

## 3. 根本原因

### 3.1 表示モードだけで地形生成を無効化

TOPO／REL／AUTO等の色・表示方式変更が、body-fixed高さTileのterrain generationまで更新していた。高さデータ自体は共通であるため、表示切替のたびに有効な生成を捨てる必要はない。

### 3.2 Request統合からTerrainGenerationが欠落

同じTile keyへ新しい要求を統合する際、view、range等は更新される一方、terrain generationが引き継がれなかった。結果commit時の世代判定が古い要求を基準にする可能性があった。

### 3.3 実行中Block workを更新できない

Pipelineが満杯の場合、同一Tileの実行中workへ最新世代を反映できなかった。取消しと再投入が反復し、steady viewでもstale／obsolete cancellationが増える条件になった。

### 3.4 途中PreviewをFinalとして扱う

RAM上にPreviewが存在するだけでFinal stageへ昇格していた。Sampling未完了でもFinal keyを持てたため、「Preview表示後にFinalへ置換する」という契約が崩れていた。

### 3.5 Sampling完了状態が独立していない

`IsPreview`が品質段階と処理完了状態を兼ねていた。25／50／75%の途中commitと、全sample完了済みPreviewを区別できなかった。

### 3.6 Range・style変更時に旧GPU entryを利用しない

Rangeが変わりcontour style keyが変化すると、同じ地形の旧entryをfallbackに使わず、一時的に地形が消える経路があった。さらにpartial Finalが完成Previewを無条件削除していた。

### 3.7 Coverageが実三角形を見ていない

viewport coverageがTile矩形の存在を基準にしており、sampling未完了や欠損triangleでも100%と判定し得た。

### 3.8 AUTO backlogを後続のfalseで消す

同一評価窓内で一度worker backlogを観測しても、後続報告がfalseなら上書きされていた。AUTO品質低下の判断が実負荷を取りこぼす可能性があった。

## 4. 修正

### Tile／Pipeline

- `SamplingComplete`をTile契約へ追加。
- 途中commitと完成Previewを明示的に分離。
- 全sample完了済みPreviewだけをFinalへ昇格。
- partial FinalはFinalのまま進捗を継続。
- 実行中workへ最新のterrain／view／range／plan／database／vessel generationを統合。
- commit callbackへ、実行時に確定したauthoritative requestを渡す。
- display mode変更では高さTile世代を進めない。
- request mergeへ`TerrainGeneration`を追加。

### GPU合成／Coverage

- exact style entryと、同じTileの互換fallback entryを分離。
- 新styleまたはFinalが途中の場合、完成済み旧entryを先に描画し、その上へ新entryを重ねる。
- 新entryの実coverageが完成するまで、完成済みfallbackを削除しない。
- index／vertex／triangleの境界を検証してからentryを採用。
- resolution、valid vertex mask、coverage fractionをentryへ保持。
- viewport sample点が実際に有効triangleへ含まれるかでGPU coverageを計算。
- CPU側visible coverageもTile存在ではなく`Quality`の平均で算出。

### AUTO／診断

- backlogは一秒の評価窓内でOR latchする。
- AUTO rate tier、quality degradation、recoveryをログへ記録。
- NDのmode／range操作をログへ記録。

## 5. 変更禁止領域

- AP／BANK control lawは変更していない。
- `AERISBankDirector.cs`のSHA-256は基準値を維持する。
- LANDは観測・表示・認証基盤のみで、操縦権限を持たない。
- legacy NAVは不在のまま。
- new NAVはBLOCKEDのまま。
- Adaptive glide path／3D obstacle corridorは本Hotfixへ混入していない。

## 6. 合否

静的回帰、C#構文解析、マニフェスト、ZIP再展開試験に合格しても、Unity GPU、KSP Mono build、実PQS、実画面の連続性は代替できない。

次のCP2再試験では最低限、次を同時に確認する。

- `5 → 20 → 160 → 10 → 40 → 80 → 160km`
- `TOPO → REL → OFF → AUTO → REL`
- 未生成地域でPreviewからFinalまで待つ
- `sampling_remaining > 0`の間にcoverageが常時1.0000へ張り付かない
- 旧地形が下敷きとして残り、黒い三角形・矩形・全面消失がない
- steady viewでstale／obsolete cancellationが一定周期で増え続けない
- AUTO負荷判定と回復がログに残る

