# AERIS v0.17.0.4 引き継ぎ

## 正式ベース

v0.17.0.3 Startup/Cache/Archive Hotfixから派生。実機証拠によりSTARTUP／Manual整合と自動Archiveは合格、cache保存と再起動間Provider signatureだけが未合格と判明したため、その二点を修正した。

## 実機で既に合格した項目

- STARTUP、Manual 1、Manual 2のProvider records=157、runways=67
- 同一起動内のProvider signature一致
- 登録71 RWY / 142 APP
- 認証13 RWY / 24 APP
- 失敗32 RWY / 62 APP
- 再認証28 RWY / 56 APP
- 自動archiveのqueued／accepted／verified
- ZIP CRC正常、検証後raw削除

## v0.17.0.4で再確認が必要な項目

1. 初回起動でschema 3 certified recordが移行される
2. schema 3 failure hintが安全に破棄され再生成される
3. STARTUP、Manual 1、Manual 2で`save verified ... fullRoundTrip=True`
4. `CACHE SAVE FAILED`が出ない
5. KSP再起動後にschema 4 cacheを読み込める
6. 二回のKSP起動間でProvider signatureが一致する
7. 再起動後も登録／認証／失敗／再認証件数が一致する
8. 自動archiveが引き続き完走する

## 禁止事項

- cache検証を無効化して保存を通さない
- 重複キーを黙って上書きして件数不一致を隠さない
- runtime UUIDだけを永続identityやsignatureの唯一の根拠にしない
- AP/BANK、LAND権限境界、旧NAV削除状態を変更しない
