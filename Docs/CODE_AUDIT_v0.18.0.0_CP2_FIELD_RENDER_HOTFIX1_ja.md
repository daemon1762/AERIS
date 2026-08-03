# AERIS v0.18.0.0 CP2 Field Render Hotfix 1 コード監査

## 1. 監査対象

基準：

`AERISFlightControl-v0.18.0.0_DEV_CP2_PreloadStatusToolbar_CompileHotfix1_Source`

修正版：

`AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source`

セマンティックVERSIONは`0.18.0.0`を維持し、DEVチェックポイント名で区別する。

## 2. 製品コード差分

製品C#差分は11ファイル、309行追加、86行削除。

| 領域 | ファイル | 目的 |
|---|---|---|
| metadata | `Properties/AERISBuildVersion.generated.cs` | 現在のDEVラベル |
| tile contract | `Terrain/AERISTerrainTileContracts.cs` | `SamplingComplete` |
| block pipeline | `Terrain/AERISTerrainBlockPipeline.cs` | authoritative request、active refresh |
| tile system | `Terrain/AERISTerrainTileSystem.cs` | generation／preview／coverage修正 |
| preload | `Terrain/AERISTerrainPreloadBuilder.cs` | callback契約追従 |
| preload codec | `Terrain/AERISTerrainPreloadCodec.cs` | 完了状態復元 |
| preload DB | `Terrain/AERISTerrainPreloadDatabase.cs` | 未完了Tileの永続化拒否 |
| legacy cache | `Terrain/AERISTerrainTileCache.cs` | 既存完成Tileの明示 |
| GPU renderer | `Terrain/AERISTerrainGpuTileRenderer.cs` | fallback合成、triangle coverage |
| AUTO | `Terrain/AERISTerrainPerformance.cs` | backlog latch、遷移ログ |
| ND UI | `UI/AERISNavigationDisplay.cs` | mode／rangeログ |

その他の変更は、受入回帰、文書、版表示、ビルド表示、AERIS13基準同梱である。

## 3. 境界監査

### AP／BANK

- `Source/AERISFlightControl/Autopilot`は基準版とバイト単位で同一。
- `AERISBankDirector.cs` SHA-256：
  `bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7`
- 提示された20°右BANKの先行制動、zero roll rate capture、no micro-wobbleを回帰基準として維持。

### LAND

- `Source/AERISFlightControl/Landing`は基準版とバイト単位で同一。
- FlightCtrlState、throttle、steeringへの書込みを追加していない。
- 現在も観測、Registry、認証基盤、表示準備だけ。

### NAV

- legacy NAV classは不在。
- new NAV capability、route command、LAND handoffは追加していない。
- CP2合格かつLAND設計ゲート完了前の新NAV実装は禁止。

### Thread／I/O

- 専用ThreadPoolを追加していない。
- PQSはMain Thread境界を維持。
- workerへUnity／KSP objectを新たに渡していない。
- 同期Disk readをND描画経路へ追加していない。

## 4. 互換性監査

- C# sourceはKSP/Mono世代を意識し、switch expression、record、nullable reference等の新構文を追加していない。
- GPU entryのcoverage regionはvalue typeであり、viewport sampleごとの不要なheap allocationを避ける。
- 既存DB format、codec version、cache keyの永続意味を変更していない。
- `SamplingComplete`はruntime object状態で、既存永続フォーマットを同じ番号のまま破壊しない。

## 5. 回帰

専用回帰は次を固定する。

- display mode変更でheight Tile generationを無効化しない。
- mergeがTerrainGenerationを保持。
- active workが最新generationへ更新される。
- partial PreviewをFinalへ昇格しない。
- completed PreviewだけFinalへ進む。
- partial FinalがFinalとして継続。
- style変更時に旧完成entryをfallback描画。
- partial current entryがfallbackを早期削除しない。
- coverageは実triangle validityを使用。
- backlogは評価窓内でlatch。
- ND range／mode、AUTO transitionが診断可能。

## 6. 残余リスク

- この環境ではKSP参照DLLを使う実コンパイルを実施できない。
- tree-sitter構文合格は型解決、Unity API、Mono BCL互換性を証明しない。
- fallback entry選択とmesh描画順はUnity runtimeで目視確認が必要。
- 実GPU coverageのCPU readbackを追加したものではなく、CPU側で保持するmesh validityに基づく推定である。
- CP2実機合格までは本成果物を正式版／RCと呼ばない。
