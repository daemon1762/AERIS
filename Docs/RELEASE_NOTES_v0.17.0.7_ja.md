# AERIS Flight Control v0.17.0.7 リリースノート

## 目的

v0.17.0.6実機試験では、STARTUP・Manual 1・Manual 2は`MEASURED 0 / CACHE 16`まで成立した一方、Flightシーン再生成後にDull SpotとMahiだけexact fingerprintが変化し、`MEASURED 2 / CACHE 14`となった。

実キャッシュの`.bak`比較から、Geometry点数・Primitive数・Collider可読性は同一で、Provider基準点だけが次の範囲で揺れていた。

- Dull Spot：約0.4m
- Mahi：約0.2m
- 高度差：約0.02m

緯度・経度の揺れがexact fingerprintの量子化境界を跨いだことが直接原因である。

## 修正

- `InputFingerprint`を従来どおり厳密なexact keyとして維持
- 配置を除くmodel／source path／Provider version／Survey definition／canonical source geometryを`SourceFingerprint`として分離
- cache schemaを5へ更新し`sourceFingerprint`を保存
- schema 4およびschema 3を読込み可能なまま維持
- algorithmVersionを1670へ更新
- exact miss後、以下がすべて一致する場合だけ既存認証を再利用
  - strict source fingerprint
  - Provider version
  - Geometry point count
  - Geometry primitive count
  - Collider readable state
  - 水平差0.50m以下
  - 垂直差0.10m以下
  - 方位差0.02°以下
  - model scale差0.0005以下
- 閾値を超える配置変更、source/config/model変更、Geometry構成変更は再測量

## 安全境界

- tolerant cache hitはexact miss後にのみ評価する
- source fingerprintが空、旧schema、algorithm不一致では使用しない
- LANDの操舵権限は追加しない
- AP/BANK制御則は変更しない
- 旧NAVは復活させない
- FlightData自動Archive修正を維持する

## 静的・モデル受入

`python3 Tools/run_v01707_acceptance.py`

配布前確定値は`ACCEPTANCE_v0.17.0.7.txt`を参照。
