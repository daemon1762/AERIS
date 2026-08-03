# AERIS Flight Control v0.17.0.5 — Provider Identity / Cache Fingerprint Determinism Hotfix

## v0.17.0.4実機結果

次は合格した。

- schema 3→4移行
- schema 4 cacheファイルの読込み
- `save verified ... fullRoundTrip=True`
- STARTUP／Manual再走査の同一起動内整合
- 自動FlightData archiveの`queued → scheduler accepted → ZIP verified`
- ZIP検証成功後のraw削除

一方、再起動後も次の二点が未合格だった。

```text
Provider records/runways: 157 / 67 → 157 / 67
最終DB: 43 RWY / 86 APP → 43 RWY / 86 APP
identity兼geometry signature: 9F70AFEC175CC002 → 3D17B5405B3014FB
STARTUP exact cache hits: 0 → 0
```

cacheファイルは正常に読み込めていたが、再起動後STARTUPでは全対象のinput fingerprintが一致せず、認証済み16レコードを再測量していた。Manual再走査では同一プロセス内で`CACHE 16`となるため、永続cacheの完全な再起動受入には未達だった。

## 根本原因

### Provider signature

安定したProvider identityと、KK／Unityが再構築するtransform由来の緯度・経度・標高・方位・寸法・scaleを一つのハッシュへ混在させていた。量子化後でも再起動間に境界をまたぐ値があり、Provider集合が同じでもsignatureだけが変化した。

### Cache input fingerprint

fingerprintは次の再起動変動要素を含んでいた。

- 安定Provider情報が存在してもruntime UUIDを常時使用
- world transform由来の値を小数6桁で直接使用
- Unity component列挙順に依存するprimitive／point順序
- 列挙順に依存する`SourceGroup`
- 順序依存のbounded point sample

このため同じ物理滑走路でも再起動後に別fingerprintとなった。

## 修正

### Provider署名分離

- `signature`をProvider集合の安定identity署名へ限定
- Body／Source／FacilityKind／Site／Group／Model／SourcePathを使用
- 安定フィールドがないレコードだけruntime UUIDをfallbackとして使用
- runtime幾何値は別の`geometrySignature`として診断表示

### Canonical cache fingerprint

- 認証アルゴリズム版を`1650`へ更新し、旧fingerprintを安全に再測量
- Provider fingerprint identityから不要なruntime UUIDを除外
- 緯度／経度は`1e-5°`、標高は`0.25m`、方位は`0.02°`で量子化
- primitive／point座標は`0.05m`単位で量子化
- primitive／pointを順序非依存multiset集約へ変更
- `SourceGroup`とcomponent列挙順をfingerprintから除外
- XOR／SUM／非線形MIXの三集約をSHA-256入力へ含め、重複数も保持
- 10cm級の実形状変更はfingerprint差として検出する回帰試験を追加

geometry fingerprint、cache完全往復検証、認証再測量、安全側のLast-Known-Good保持は維持する。

## 不変条件

- BANK/AP制御則は変更しない
- LANDは観測・表示・認証のみで操舵権限なし
- 旧NAVは不在
- 新NAVはLAND完成までBLOCKED
- v0.17.0.4で合格したcache schema 4と自動Archiveを維持
