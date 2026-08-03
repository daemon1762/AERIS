# AERIS Flight Control v0.17.0.3 リリースノート

状態: **Startup / Cache / Archive Field Hotfix Source RC**  
日付: 2026-07-23  
基準: v0.17.0.2 Runway Registry Identity Hotfix Source

## 実機証拠から確定した三件

v0.17.0.2のID衝突修正後、起動時・手動2回のatomic commit自体は成功しました。しかし、実機ログと動画から次の未達を確認しました。

```text
STARTUP generation=1 revision=1: 44 RWY / 88 APP, CERTIFIED 19 / 32
MANUAL  generation=2 revision=2: 43 RWY / 86 APP, CERTIFIED 13 / 24
MANUAL  generation=3 revision=3: 43 RWY / 86 APP, CERTIFIED 13 / 24
```

また、手動再走査時に次が記録されました。

```text
[AIRFIELD_CACHE] CACHE LOAD FAILED — ... CACHE ROOT MISSING
```

FlightDataはrawフォルダまで生成されましたが、ユーザーが提出したZIPは手動圧縮でした。性能テレメトリは`archive_pending=1 / archive_completed=0 / active_archive=0`で終了しており、自動アーカイブは未実行でした。

## 修正1 — StartupとManualを同じProvider状態で走査

旧版はMain Menu常駐Bootstrapの起動約2秒後にSTARTUP走査を開始していました。実機では飛行シーンへ入る数分前に走査され、Manualはunpack済み機体・FlightシーンのProvider状態で走査されたため、同一KSP起動中でも入力集合が異なりました。

v0.17.0.3ではSTARTUP要求自体は一度だけ保持し、次の条件をすべて満たすまで実行しません。

- Flightシーンである
- Active Vesselが存在する
- Vesselがunpack済みである
- 同じActive Vesselの状態が1.5秒以上継続している

Vesselが変わる、packedへ戻る、Flightシーンを離れる場合は安定タイマーを破棄します。これによりSTARTUPとManualを同じruntime Provider条件で比較できます。

各走査では次の診断を追加しました。

```text
[AIRFIELD_PROVIDER_SNAPSHOT]
cause=<STARTUP|MANUAL>
generation=<n>
records=<n>
runways=<n>
signature=<16桁hex>
KSP=<provider status>
KK=<provider status>
```

件数だけでなくProvider identity集合の決定論的signatureを比較できます。

## 修正2 — ConfigNodeキャッシュroot互換と完全往復検査

`ConfigNode.Save`したnamed nodeは、KSP/Mono環境により次のいずれでも読み戻され得ます。

1. 読み込んだroot自身が`AERISAirfieldCertificationCache`
2. generic root配下に同名nodeを持つ
3. generic root自身に`schemaVersion`と`Record`が直置きされる

v0.17.0.2は1と2だけを受理したため、実機で生成された3を`CACHE ROOT MISSING`として拒否しました。

v0.17.0.3ではprimary、backup、保存直後の検証に同一resolverを使用し、三形式を受理します。保存時は以下を完了するまで現行cacheを置換しません。

- 一時ファイルのrootとschemaを再読込
- `Record`／`FailureRecord` node数の一致
- 製品版parserによる全レコード再解析
- 認証／失敗record数の完全一致
- 以上が成功してからatomic replaceまたは安全なcopy/move fallback

成功時は次を記録します。

```text
[AIRFIELD_CACHE] load accepted; certified=...; failures=...
[AIRFIELD_CACHE] save verified; certified=...; failures=...; fullRoundTrip=True.
```

## 修正3 — Main MenuでのFlightData archive排出

Archive jobは共有bounded schedulerへ正常に投入されても、FlightからMain Menuへの遷移で残った高いframe P95により`ArchivePaused`が継続し得ました。短い受入では回復条件を満たす前にKSPが終了し、rawだけが残りました。

v0.17.0.3では、次の条件でarchive laneを1本だけ実行可能にします。

- Flightシーン外
- pending archiveが存在
- LAND activeではない

Safety/LAND予約、worker上限、有界queue、ZIP内容の完全比較、検証成功後だけrawを削除する契約は維持します。Flight中の高負荷時には従来どおりarchiveを停止します。

ライフサイクル証拠として次を追加しました。

```text
[FDR][ARCHIVE] queued; pending=...; folder=...
[FDR][ARCHIVE] scheduler accepted; folder=...
[FDR][ARCHIVE] ZIP verified: ...
```

## 変更していないもの

- `AERISBankDirector.cs`を含むAP/BANK制御則
- PROTECT、Ground Assist、Auto Takeoffの制御則
- LANDの観測専用／認証済み方向限定／ARM中geometry freeze
- 旧NAV完全削除状態
- 新NAV未搭載・LAND完成までBLOCKEDという開発順序
- 重複airfield、無効geometry、不正certificationをatomic rejectする安全検査
- GPUを安全判断の唯一の根拠にしない契約

## ソース受入

```text
12 / 12 pre-manifest scripts PASS
606 / 606 pre-manifest assertions PASS
13 / 13 final scripts PASS
609 / 609 final assertions PASS
```

最終確定値は`ACCEPTANCE_v0.17.0.3.txt`を正とします。

## 未実証

この作業環境にはKSP参照DLL、Mono/xbuild、実KSPがありません。したがって次は実機ゲートです。

- KSP 1.12.5参照DLLでのRelease build
- STARTUP／MANUAL 1／MANUAL 2の件数とProvider signature完全一致
- 既存v0.17.0.2 cacheの読込成功
- KSP再起動後の永続cache hit
- Main Menu待機中の自動ZIP生成、内容検証、raw削除
- 各滑走路の認証方式完成と可変進入角
