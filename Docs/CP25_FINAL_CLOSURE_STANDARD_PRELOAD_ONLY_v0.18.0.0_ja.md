# AERIS v0.18.0.0 CP2.5 最終クローズ

## 決定

手動FULL BOOSTは、標準Preloadより低速でFPS低下と制御複雑化を招いたため廃止する。本版では機能を停止するだけでなく、実行コード、UI、状態、テレメトリ、Scheduler方針、公開・内部操作入口から完全に削除した。

CP2.5のTerrain Preloadは、実機で安全復帰が確認されたSTANDARD経路へ一本化する。これをもってCP2.5 Track Aを閉じ、CP3 Current Body Resident Cacheへ移行する。

## STANDARD Preload

- 非Flightでのみ動作する。
- workerは通常CP2.5上限だけを使用する。
- Terrain Blockはactive 64、tileごとのpending block 4、全体outstanding 96。
- PQS budgetは基本8～24ms。FAST／MAXIMUM設定は既存のbounded上限内で増加する。
- CPU Encodeはcommit-requiredで最大32。
- SSD commit jobはcommit-requiredで最大1、1 super-batchは最大32 chunk。
- Scheduler満杯時はjob/resultを捨てず、データを保持して再試行する。
- `required-drop=0`を必須とする。

## 安全復旧

FULL専用failsafeは削除し、STANDARD共通の復旧へ置き換えた。

次を検出した場合、保存済みTerrain Databaseを削除せず、Preload Builderの一時状態だけを再構築する。

- commit-required result drop
- outstandingが残り、PQS・Encode・SSDが停止した状態が6秒継続

ログ：

```text
[PRELOAD_RECOVERY] reason=PIPELINE_STALL; recoveredTiles=...; mode=STANDARD; databasePayload=UNCHANGED
```

通常試験ではこのログが出ないことが望ましい。出た場合も自動復旧後にtile進捗が再開することを確認する。

## 維持機能

- Map DRAM Cache metadata-only契約
- Altitude Gate
- Terrain品質AUTO／LOW／MEDIUM／HIGH
- LAND demand separation
- ND／FDIのAUTO／ALWAYS／OFF
- ACC／VELだけのSPEED専用FDI
- AA control-surface lifecycle修正
- AIRFIELDS全カテゴリの0件無効化
- CPU Encode／SSD Write commit-required保護
- Track B滑走路データの完全凍結

## CP3境界

本版にCurrent Body Resident Cache、decode済み高度配列の常駐、mesh常駐、RenderTexture常駐、GPU-ready payloadは含まない。CP3は本版のsource treeとmanifestを原本として開始する。
