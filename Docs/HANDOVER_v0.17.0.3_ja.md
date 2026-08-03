---
artifact: AERISFlightControl-v0.17.0.3_StartupCacheArchiveHotfix_Source
original_baseline: AERISFlightControl-v0.17.0.2_RunwayRegistryIdentityHotfix_Source.zip
original_sha256: 0c20572af23d47741c82bed7eaaa2e5d737ca861d68ff7b9a1b243abf7f00db7
date: 2026-07-23
status: source RC; static/model acceptance complete; native KSP build and field acceptance pending
---

# AI向け引継ぎ — v0.17.0.3 Startup / Cache / Archive Hotfix

## 固定された製品状態

- APは完成済み。BANK制御を含む制御則は凍結・回帰保護対象。
- 旧NAVは完全削除済み。
- 新NAVは未搭載。独立LAND総合受入前に着手しない。
- LANDは観測・認証基盤であり、FlightCtrlState、MainThrottle、AP directorへ書き込まない。
- CPUのmain/safety pathが最終判断を所有し、GPUは補助だけに使う。
- Logging/FDR/CVR/FlightData archiveはbounded background architectureを維持する。

## v0.17.0.2実機受入で合格したもの

- 正式原本SHA256とbuild前受入
- AERIS v0.17.0.2 load
- `DISC_STOCK_KSP`重複の消滅
- STARTUP atomic commit
- MANUAL atomic commit 2回
- database revision `1 → 2 → 3`
- Manual 1とManual 2の件数一致

## v0.17.0.2実機受入で残ったもの

1. STARTUPが44 RWY/88 APP、Manualが43 RWY/86 APPで不一致。
2. `CACHE ROOT MISSING`によりdisk cacheを読めず、memory recordだけを継続使用。
3. FlightData rawはあるがauto archiveは`pending=1/completed=0`。提出ZIPは手動圧縮。

## 根本原因と修正契約

### A. Provider runtime gate

STARTUP要求はMain Menuで受け付けるが、実走査はFlight + Active Vessel + unpack済み + 同一Vessel 1.5秒安定後に限る。Manualも同じregistry pathを使う。Scene/Vessel/packed変化で安定タイマーをリセットする。

禁止:

- Startupだけ別Provider pathを復活させない。
- 件数差を隠すためcache recordを無条件に足さない。
- 重複／invalid database検査を緩和しない。

### B. Cache root resolver

primary、`.bak`、temporary readbackで同じ`ResolveCacheRoot`を使用する。named-root、wrapped-root、direct-rootを受理するが、`schemaVersion`がない無関係rootは拒否する。

保存はnode数だけでなく、製品parserでtemporary全体を再解析し、certified/failure record数が一致した後だけ置換する。

禁止:

- 解析不能cacheを空cacheとして黙って上書きしない。
- save失敗時に現行cacheを削除しない。
- backup recoveryを削除しない。

### C. Archive scene-idle drain

`!inFlight && PendingCount > 0 && !landActive`では、遷移直後のstale P95 backoffよりarchive drainを優先してarchive laneをunpauseする。実行本数はbounded schedulerのactive permitsとSafety reserved permitsに従う。

禁止:

- Flight中の負荷保護を解除しない。
- Main ThreadでZIP、hash、verify、deleteを行わない。
- ZIP verify前にrawを消さない。
- KSP終了時に同期waitしない。

## 変更された製品コード

- `Core/AERISBootstrap.cs`
- `Landing/AERISAirfieldRegistry.cs`
- `Landing/AERISRunwayCertificationCache.cs`
- `Performance/AERISPerformanceRuntime.cs`
- `Performance/AERISWorkerScheduler.cs`
- `Recording/AERISFlightDataArchive.cs`
- version metadata

AP/BANK、LAND controller boundary、旧NAV削除対象ファイルは変更していない。

## 診断契約

次のログを実機で必ず回収する。

```text
[AIRFIELD_PROVIDER_SNAPSHOT] cause=STARTUP ... signature=...
[AIRFIELD_PROVIDER_SNAPSHOT] cause=MANUAL ... signature=...
[AIRFIELD_CACHE] load accepted ...
[AIRFIELD_CACHE] save verified ... fullRoundTrip=True.
[FDR][ARCHIVE] queued ...
[FDR][ARCHIVE] scheduler accepted ...
[FDR][ARCHIVE] ZIP verified ...
```

STARTUP、Manual 1、Manual 2でProvider `records/runways/signature`とcommit summaryが完全一致すること。

## 次ゲート

`Docs/RUNWAY_REGISTRY_TEST_CARD_v0.17.0.3_ja.md`に従い、次を一回のKSP起動で行う。

1. 起動時走査
2. 手動再走査2回
3. cacheファイル保全
4. FlightからMain Menuへ通常遷移
5. auto archive完了までKSPを終了しない
6. KSPを再起動し、永続cache読込と同一件数を確認

このゲートPASS後も、全滑走路対応・可変進入角・三次元障害物回廊・独立LAND完成は別工程である。
