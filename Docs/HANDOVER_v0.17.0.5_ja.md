# AERIS v0.17.0.5 引き継ぎ

## 正式ベース

v0.17.0.4 Cache Round-Trip / Provider Determinism Hotfixから派生。

## v0.17.0.4で合格

- schema 3→4 migration
- schema 4 load／save完全往復
- STARTUP／Manualの同一起動内整合
- 登録／認証／失敗件数の再起動間一致
- 自動FlightData ZIP、内容検証、raw削除

## v0.17.0.4で未合格

- 再起動間Provider signature一致
- 再起動後STARTUP exact cache hit

実測では両起動ともSTARTUPが`MEASURED 16 / CACHE 0`、Manualが`MEASURED 0 / CACHE 16`だった。cacheファイル読込み成功だけでは永続cache合格としない。

## v0.17.0.5の変更

- Provider identity署名とruntime geometry署名を分離
- cache input fingerprintを順序非依存・量子化済みcanonical multisetへ変更
- algorithmVersionを1650へ更新
- stable provider fieldsがある場合はruntime UUIDを除外
- component順序、SourceGroup、raw浮動小数点をfingerprint根拠から除外

## 次の実機ゲート

### 1回目

旧algorithmVersion 1640 cacheを読み込み、STARTUP＋Manual 2回を実施する。アルゴリズム更新後のためSTARTUP再測量は正常である。各走査でsave `fullRoundTrip=True`が必要。

### 2回目

同じGameDataで再起動し、STARTUP＋Manual 1回を実施する。

必須条件:

- `signature`が1回目と2回目で一致
- records／runwaysと最終DB件数が一致
- 2回目STARTUPで`CACHE > 0`
- 通常の合格想定は`MEASURED 0 / CACHE 16`
- `geometrySignature`は診断値として記録
- archiveが両起動で完走

## 禁止事項

- cache一致を作るためgeometry fingerprintや再測量を無効化しない
- 量子化幅を滑走路安全不確実性より粗くしない
- runtime UUIDを安定Providerの唯一のidentityへ戻さない
- AP/BANK、LAND権限境界、旧NAV削除状態を変更しない
