# v0.18.0.0 Gate 0 実KSP試験カード（将来用）

本カードはネイティブコンパイル後の実機試験用です。現DEVパッケージ自体は実KSP受入済みではありません。

## 準備

- v0.17.0.8のcacheを削除しない
- KSP.log、AERIS session log、Performance CSV、FlightDataを保存
- 手動ZIPを作らず、自動Archiveを観測
- LAND ARM／自動着陸／NAVは使用しない

## シーケンス

1. KSP起動
2. Flightへ入り、安定unpacked後STARTUP完了を待つ
3. Manual reloadを2回
4. Space Centerへ戻る
5. 同じKSP processで再びFlightへ入る
6. Manual reload
7. KSPを通常終了
8. KSP再起動
9. Flightへ入りSTARTUP
10. Manual reload

## 必須ログ

```text
[PHYSICAL_RUNWAY]
[AIRFIELD_PROVIDER_SNAPSHOT]
[AIRFIELD_CACHE] physical runway alias migration
[AIRFIELD_CACHE] save verified ... fullRoundTrip=True
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
```

## 合格条件

- 全走査でRaw Provider countが一致
- 全走査でCanonical physical runway countが一致
- Physical identity signatureが一致
- Provider identity signatureが一致
- REGISTERED／CERTIFIED／FAILED／PENDING／REVALIDATEが一致
- cache Record／FailureRecord件数がManual、scene再生成、KSP再起動で不変
- 同じphysical runwayに有効certified recordが一つ
- PSystem／SLE／KK aliasが別runwayとして二重表示されない
- 09／27は同一physical runwayの二方向として保持
- 09／10など別滑走路は統合されない
- `DISC_STOCK_KSP`、duplicate database、cache load/save failureなし
- 自動ArchiveのCRC検証後だけraw削除

## 失敗時に保存するもの

- `GameData/AERISFlightControl`全体
- KSP.log
- 可能なら動画
- 走査前後のAIRFIELDS画面

Provider count、PhysicalRunwayId、alias一覧、cache件数が分かる状態を残します。
