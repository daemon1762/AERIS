# AERIS Flight Control v0.17.0.4 — Cache Round-Trip / Provider Determinism Hotfix

## 実機証拠で確定した残存不具合

v0.17.0.3実機試験では、STARTUPとManual再走査のProvider件数・滑走路件数・登録／認証結果は一致し、自動FlightData archiveも`queued → scheduler accepted → ZIP verified`まで完走した。

一方、全走査でcache保存が次の理由により拒否された。

```text
[AIRFIELD_CACHE] save failed; live database remains valid:
InvalidDataException: temporary cache full round-trip failed
```

さらに、Provider件数157・runway件数67が同じ二回のKSP起動間で、Provider signatureが変化した。

## 根本原因

### Cache

ランタイムの`StableRecordId`は、Body／Provider UUID／Provider site／Modelを改行で分離した構造化文字列だった。ConfigNodeの単一行valueへそのまま保存すると改行が平坦化され、異なるIDが同じ文字列へ衝突し得る。保存直後の製品parser再読込ではDictionary件数が減り、完全往復検証が正しく失敗していた。

### Provider signature

Provider signatureが`StableRecordId`を使用しており、安定したsite/path/modelを持つKK/SLEレコードにもruntime UUIDを含めていた。Provider集合と最終DBが同一でも、再起動時にUUIDが変わるとsignatureだけが変化した。

## 修正

- Cache schemaを4へ更新
- `StableRecordId`をUTF-8 Base64で可逆保存
- 人間向け`stableRecordId`は改行をescapeした一行表示として併記
- schema 3のcertified recordは保存済みBody／UUID／site／modelから構造化IDを再構築
- schema 3のfailure hintは必要な逆変換情報が不足するため、安全側で破棄して再測量
- schema 4 failure recordへBody／UUID／siteを追加
- 保存後の不一致ログへexpected／actualのcertified・failure件数を追加
- Provider signatureは安定したbody/source/site/group/model/pathと量子化された位置・姿勢・寸法から計算
- 安定したProviderフィールドがある場合、runtime UUIDをsignatureから除外
- 安定フィールドがないレコードだけUUIDをfallbackとして使用

## 不変条件

- BANK/AP制御則は変更しない
- LANDは観測・表示・認証のみで操舵権限なし
- 旧NAVは削除済みのまま
- 新NAVはLAND完成までBLOCKED
- Archive scheduler修正はv0.17.0.3のまま維持
