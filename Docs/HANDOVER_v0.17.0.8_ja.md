# AERIS v0.17.0.8 引き継ぎ

## 正式ベース

v0.17.0.7から派生。AP/BANK、LAND、新NAV境界は変更しない。

## 修正理由

TSC Runway 09/27のKK Provider UUIDがKSP再起動ごとに変化し、failure hintが2件ずつ増殖した。認証DB件数とcache hitは正常だったが、長時間・多数回起動でcacheが無制限に肥大化する。

## 修正内容

1. StableRecordIdを共有ヘルパーで生成する。
2. site/path/modelがあればUUIDを除外する。
3. schema 6ロード時に旧UUID別名を正規化・圧縮する。
4. certified recordの埋込みrunway/direction StableIdも新IDへ移行する。
5. survey algorithm 1670と認証結果は維持する。

## 実機ゲート

- 既存schema 5 cacheを削除しない。
- 初回ロードで`canonical identity migration compacted`を確認する。
- 現証拠からは`0 certified alias(es) / 4 failure alias(es)`が期待値。
- load/save後のfailure countは51。
- 初回STARTUPから`MEASURED 0 / CACHE 16`。
- Manual 2回、シーン再生成後Manual、二回目STARTUPでも16/16。
- 二回目起動でfailure countが51から増えない。
- identity signatureと最終DB件数が全走査で一致する。

## 未完

Snapshot sliceの単発スパイクはPC2性能課題として分離。LAND認証器本体の未完項目と新NAV BLOCKED状態も継続する。
