# AERIS Flight Control v0.17.0.2 リリースノート

状態: **Runway Registry Identity Hotfix Source RC**  
日付: 2026-07-23  
基準: v0.17.0.1 Performance Runtime Compile Hotfix Source RC

## 症状

添付された152.641秒の動画では、`SYSTEM > AIRFIELDS`が起動後に次の状態となっていました。

```text
STATE FAILED
DATABASE REV 0
LAST NEVER
AIRFIELDS 0
RUNWAYS 0
CERTIFIED APPROACHES 0
```

手動再読込では一時的に`DISCOVERING`へ遷移しますが、完了時に同じ0件状態へ戻りました。

添付AERISログとKSPログでは、起動時のgeneration 1、手動再読込のgeneration 2と3がすべて次の理由で失敗しています。

```text
[AIRFIELD_RELOAD] FAILED — STAGED DATABASE INVALID:
DUPLICATE AIRFIELD Kerbin / DISC_STOCK_KSP
```

大量の`[AIRFIELD_SNAPSHOT]`記録が失敗より前に存在するため、Provider検出や滑走路スナップショットが動かなかったのではありません。最終データベース検証で全ステージが拒否されていました。

## 原因

KSPの`PSystemSetup.SpaceCenterFacilities`から得た独立施設すべてに、`ProviderGroup = "KSP"`を設定していました。一方、未設定施設の発見ID生成は、Stock/DLCにも`ProviderGroup`を優先していました。

そのため、同一天体の複数施設がすべて次の同じStable IDになりました。

```text
Kerbin
DISC_STOCK_KSP
```

重複検査は設計どおり安全側に失敗しましたが、初回データベースrevisionが0だったため、同じステージに含まれる設定済みKSC Main RunwayとIsland Airfieldもコミットされず、画面では滑走路が一つも見えませんでした。

## 修正

- Stock/DLCの未設定施設は`ProviderSiteId`から発見IDを生成
- KK/SLEのみ`ProviderGroup`を飛行場単位の集約キーとして使用
- KSP施設Providerの`ProviderGroup`を施設名へ変更
- `FindDiscoveredGroup`をKK/SLE専用に制限
- データベース重複検査は緩和せず維持
- 重複ID診断を一行化し、ログとAIRFIELDS画面で判読可能に変更
- AIRFIELDS画面へ`RESULT <status>`を追加

## 回帰防止

`Tools/selftest_v01702_runway_registry_identity.py`で次を検査します。

- 旧実行時と同じ共有`KSP`グループを持つ複数Stock施設でもIDが一意
- `DISC_STOCK_KSP`を生成しない
- DLCの独立施設が一意
- Area 52などKKの複数滑走路は同一飛行場へ束ねる
- Glacier LakeなどSLEの複数滑走路は同一飛行場へ束ねる
- Provider、Registry、AIRFIELDS UIの製品コードに修正契約が存在

## 変更していないもの

- 完成済みAP
- `AERISBankDirector.cs`のロール先行制動、ゼロロールレート捕捉、保持則
- Performance Runtime、非同期記録、アーカイブ
- LANDの認証済み方向限定、ARM中の幾何凍結、観測専用境界
- 旧NAV削除状態。新NAVは未搭載・開発中

## 未実証

このソース作業環境にはKSP参照DLLとMono/xbuildがないため、v0.17.0.2のネイティブKSPビルドと実KSP再読込は未実施です。実環境の合否は`Docs/RUNWAY_REGISTRY_TEST_CARD_v0.17.0.2_ja.md`で確認してください。
