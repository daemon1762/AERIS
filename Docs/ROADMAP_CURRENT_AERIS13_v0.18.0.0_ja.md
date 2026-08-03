# AERIS13準拠 現行ロードマップ — v0.18.0.0

基準日：2026-07-25

## 1. 現在地

| Gate | 内容 | 現在の判定 | 次の解除条件 |
|---|---|---|---|
| 0 | Physical Runway Federation | 静的PASS、起動commit一回実証、field gate一部保留 | 手動RELOAD二回とDB revision／identity再確認 |
| 1 | ND計測・レイヤー分離 | CP1静的完了 | CP2総合で回帰確認 |
| 2 | 滑走路表示・選択・PLAN | CP1静的完了 | CP2総合で実画面回帰 |
| 3 | Terrain Tile／LOD／Cache | 修正ソース完成、実機再試験待ち | Preview→Final、Range、steady収束 |
| 4 | GPU Terrain表示 | 修正ソース完成、実GPU再試験待ち | 黒欠損なし、fallback連続性、REL追従 |
| 5 | ND操作・品質 | 修正ソース完成、実機再試験待ち | mode／range／AUTO遷移ログと実画面 |
| 6 | Approach Procedure Registry | BLOCKED | CP2合格後にCP3開始 |
| 7 | 可変Glide Profile | BLOCKED | Gate 6契約確定後 |
| 8 | 3D Obstacle Corridor | BLOCKED | Gate 6–7と同時成立 |
| 9 | LAND表示統合 | BLOCKED | Gate 6–8合格後 |
| 10 | 総合受入 | 未着手 | CP4 |
| 11 | 新NAV | BLOCKED | 独立LAND完成・総合受入後 |

## 2. 今回更新された判定

### 滑走路Registry

- startup atomic commitは`databaseRevision=1`で一回成功。
- `43 RWY / 86 APP`登録。
- `13 RWY / 24 APP`は現行認証器でcertified。
- `32 RWY / 62 APP`はfailed。
- `DISC_STOCK_KSP`再発なし。
- 手動RELOAD二回は未実施。

このcertified判定は最終的な安全進入認証ではない。AERIS13の41施設個別表を継承し、CP3で方向別に次を成立させる。

- 3.0°から6.0°を0.1°刻みで評価。
- 最小の安全角を方向別に採用。
- 片方向だけ安全なら反対方向を無効化。
- X字・曖昧geometryは自動認証しない。
- 3D corridor幅、人工障害物、turn、missed approach、機体性能を同時評価。
- 2.5～2.9°は例外的な手動定義に限定。

### CP2 Terrain

入力版はField Render gateでFAIL。今回、generation、sampling state、fallback composition、coverage、AUTO判定を修正した。

静的受入合格後も次の実KSP試験がCP2解除条件である。

1. native Mono build。
2. non-Flight Preload継続とFlight read。
3. Range／mode高速切替。
4. 未生成地域のPreview→Final。
5. GPU failure／OFF fallback。
6. steady viewでqueue／cancellation収束。
7. 60分以上のRAM／VRAM／Disk boundedness。
8. scene／vessel／body遷移。

## 3. 凍結条件

- APは完成済み。制御則を変更しない。
- BANK基準SHAを変更しない。
- LANDは観測・計画・表示だけ。Flight control authorityなし。
- legacy NAVを戻さない。
- new NAVを先行実装しない。
- CP2の実機FAILを文書合格で上書きしない。
- 3°固定認証を最終安全仕様とみなさない。

## 4. 次の工程

### 次の一手

Field Render Consistency Hotfix 1をUbuntu/KSPでbuildし、改訂試験カードのRange／mode／Preview／AUTO試験を行う。

### CP2 PASS後

CP3を開始する。

- Gate 6：Physical runway directionとprocedure registry接続。
- Gate 7：方向別adaptive glide profile。
- Gate 8：terrain／static obstacle／missed approachの3D corridor。

### CP3 PASS後

CP4でLAND平面・縦断表示と総合受入を行う。操縦権限追加は別審査とする。

### 最後

独立LAND完成・総合受入後にのみ、新NAVを完全新規開発する。

