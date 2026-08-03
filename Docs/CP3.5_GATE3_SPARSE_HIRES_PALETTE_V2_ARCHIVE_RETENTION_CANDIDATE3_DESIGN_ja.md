# CP3.5 Gate 3 Candidate 3 設計

## 目的
CP3 Frozen系の安定したFAR Foundationを壊さず、AERIS59で残った粗い海岸線・アクセシビリティ色階調・ND毎フレーム負荷を改善し、FDR/CVR FlightData ZIP保存上限を追加する。

## 地形
Candidate 1の「可視FAR全tileを高解像度Unity Meshへ昇格」は再使用しない。FAR 33x33は常時の安全なFoundationとする。HIGH長距離ではGeometryを増やさずRenderTargetを1.25倍にし、既に存在するRoute/Local exact payloadだけを中心半径1以下でSparse Refinementとして利用する。

海岸線はland/water分類だけで固定38%位置へ置く旧方式を改め、辺両端の標高が海面を跨ぐ場合に海面1mとの線形交点を使う。これによりFARセル内部で海岸線位置をサブセル精度化する。地形データそのものを捏造する補間ではなく、既存標高サンプル間の境界位置推定である。

## Accessibility Palette v2
TOPO色は固定 -500..12000m 正規化を廃止。現在可視tileのmin/maxを250m単位に量子化し、最低1500mの表示スパンを保証する。RG/BY/HIGHは海面・低地・中地・高地を色相だけでなく輝度差でも区別できるよう再設計する。

## Presentation
Exact FRONTが引き続き表示権限を持つ。Temporal Presentationは無効のまま。Exact-only検証中に不要な9x9 temporal shadow gridを毎Repaint計算せず、旧FRONT中心の現在画面上ドリフト・heading差・ageだけでKey Frame更新判定を行う。

## FDR/CVR Archive Retention
1 flight = FDR/CVR等を含む1 verified ZIP。保存数は1..30選択式、default 10。正式ZIPをread-back検証後に `.verified` markerをatomic commitし、同markerを持つZIPだけを最古から削除する。current archive、raw folder、`.zip.tmp`、unverified ZIPは削除対象外。処理は既存Archive worker lane上で行う。

## LAND品質
Terrain qualityとしてのLANDは廃止済みを維持する。AUTO / LOW / MEDIUM / HIGHのみ。AUTOPILOT LANDは別機能であり維持する。
