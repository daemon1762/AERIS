# 滑走路別・認証結果と対応法

作成日: 2026-07-23  
対象: Stock、DLC、現行KK/SLEカタログ41レコード

## 1. この表の読み方

復旧できた実行データはv0.17.0.2ホットフィックス前であり、最終DBが旧ID衝突により全体拒否された。したがって、MOD滑走路の方向別合否は一件も確定していない。

`snapshot警告`は候補取得が動いた証拠と性能情報であり、認証失敗ではない。`警告なし`も合格や未検出を意味しない。

対応コード:

| コード | 必須対応 |
|---|---|
| `STOCK-DIR` | 設定済み幾何を起点に、方向別三次元回廊を再認証 |
| `SB` | `StaticBounds`を実行時に強制し、得た物理軸のA/Bを別認証 |
| `SB-RL` | `SB`に加え、設置ごとのrotation/scaleと左右配置を再確認 |
| `SB-SNOW` | `SB`に加え、雪面Collider/PQS、表面連続性、モデル指紋を再確認 |
| `SB-LARGE` | `SB`に加え、大型Meshを指紋キャッシュ/オフライン抽出 |
| `MR-X` | 自動認証禁止。X字の各物理軸へ両閾値を手動定義 |
| `MR-SPAWN` | 自動認証禁止。汎用SpawnPointから滑走路を推測しない |
| `PAIR` | PairKeyで2閾値を一つの物理滑走路へ結合し、09/27を別認証 |
| `DLC-SURVEY` | Discovery only。現物の両閾値、幅、表面を取得するまでARM禁止 |
| `REJECT` | 固定翼滑走路ではないためLAND候補から除外 |
| `PERF-P0` | 10 ms超。安全判定前に主スレッド長時間停止を隔離 |
| `PERF-P1` | 5～10 ms。反復時はキャッシュ/抽出を適用 |
| `MONITOR` | 1.50 ms超過あり。再試験で傾向監視 |

角度欄の`3.0→6.0`は0.1°刻みで最小の安全角を選ぶ意味。幾何が曖昧な間は角度探索を開始しない。

## 2. Stock / DLC

| No. | 滑走路・方向 | 現在確認できる状態 | 対応 | 角度 |
|---:|---|---|---|---|
| S1 | KSC Main RWY 09 | 設定値: 2526.057 m × 70 m、heading 90.377°、TCH 15 m。`FoundationValidated`により設定読込み時に認証扱い。今回の旧DB全体拒否では画面へcommitされず | `STOCK-DIR`。09進入回廊、人工障害物、復行を単独検査 | 暫定3.0°、必要時3.0→6.0 |
| S2 | KSC Main RWY 27 | 設定値: heading 270.377°、TCH 15 m。09とは独立した進入方向 | `STOCK-DIR`。27側だけを単独検査し、09の結果を流用しない | 暫定3.0°、必要時3.0→6.0 |
| S3 | Island RWY 09 | 設定値: 1283.384 m × 30 m、heading 89.384°、TCH 12 m。設定認証扱い | `STOCK-DIR`。短く狭いため機体別停止距離と横誤差も必須 | 暫定3.0°、必要時3.0→6.0 |
| S4 | Island RWY 27 | 設定値: heading 269.381°、TCH 12 m。09とは独立 | `STOCK-DIR`。27側回廊、復行、機体別停止距離を単独検査 | 暫定3.0°、必要時3.0→6.0 |
| D1 | Dessert Airfield | DLC導入時にDiscovery only。精密閾値未確定 | `DLC-SURVEY`。物理滑走路、両閾値、heading、幅、表面を実測 | 幾何確定後に方向別3.0→6.0 |
| D2 | Woomerang Launch Site | LaunchPad。固定翼滑走路ではない | `REJECT`。一覧上の施設分類だけ保持し、LAND/ILSから除外 | 対象外 |

注意: KSC/Islandは現行コードでは「信頼済み設定」として実測スナップショットを省略する。新しい方向別回廊認証を通すまでは、`FoundationValidated`を精密安全認証と同一視しない。

## 3. KK / SLE カタログ41レコード

全行共通の現状: ホットフィックス前のステージ全体拒否により、方向別認証結果は未確定。

| No. | 施設 / 滑走路 | カタログ方式・モデル | 復旧CVRの性能証拠 | 滑走路固有の対応 | 角度 |
|---:|---|---|---|---|---|
| 1 | Top Secret Area 15 | `ManualRequired` / `UniversalSpawnPoint` | 1.50 ms超過警告なし | `MR-SPAWN`。Spawn姿勢から軸を推測せず、物理滑走路ごとの両閾値を手動登録 | 登録後、各方向3.0→6.0 |
| 2 | Cape Kerman | `StaticBounds` / `KK_2500m_runway` | 2 scan / 4 slice、最大2.643 ms | `SB` + `MONITOR`。物理軸を確定しA/Bを独立検査 | 各方向3.0→6.0 |
| 3 | Dununda | `ManualRequired` / `KK_2500mX_runway` | 2 / 6、最大11.509 ms | `MR-X` + `PERF-P0`。交差する各軸を手動分離し、大型取得をキャッシュ | 各軸・各方向3.0→6.0 |
| 4 | Harvester Airfield | `StaticBounds` / `KK_1700m_runway` | 2 / 3、最大2.235 ms | `SB` + `MONITOR` | 各方向3.0→6.0 |
| 5 | Hazard Shallows | `StaticBounds` / `KK_1700m_runway` | 1.50 ms超過警告なし | `SB`。警告なしを検出/合格証拠にしない | 各方向3.0→6.0 |
| 6 | Jeb's Junkyard and Spaceship Parts | `StaticBounds` / `KSC_Runway_level_1` | 1.50 ms超過警告なし | `SB`。Stock KSCと同型でも、施設ID・位置・回転を別指紋にする | 各方向3.0→6.0 |
| 7 | Kamberwick Green | `StaticBounds` / `KK_2500m_runway` | 2 / 6、最大5.662 ms | `SB` + `PERF-P1` | 各方向3.0→6.0 |
| 8 | Kerman Atoll | `StaticBounds` / `KK_2500m_RL_runway` | 2 / 5、最大2.298 ms | `SB-RL` + `MONITOR` | 各方向3.0→6.0 |
| 9 | Kojave Sands | `StaticBounds` / `KK_2500m_RL_runway` | 2 / 4、最大2.385 ms | `SB-RL` + `MONITOR` | 各方向3.0→6.0 |
| 10 | Kola Island | `StaticBounds` / `KK_2500m_runway` | 2 / 4、最大2.227 ms | `SB` + `MONITOR`。海岸/島地形を横幅付き回廊で検査 | 各方向3.0→6.0 |
| 11 | Baikerbanur Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 6、最大3.811 ms | `SB` + `MONITOR` | 各方向3.0→6.0 |
| 12 | Nye Island | `StaticBounds` / `KK_1700m_runway` | 1 / 1、最大1.537 ms | `SB` + `MONITOR`。短い滑走路として機体別停止距離を必須化 | 各方向3.0→6.0 |
| 13 | Polar Research Alpha | `StaticBounds` / `KK_1700m_runway` | 1 / 1、最大1.784 ms | `SB` + `MONITOR`。極域の座標/heading安定性を確認 | 各方向3.0→6.0 |
| 14 | Round Range | `StaticBounds` / `KK_2500m_RL_runway` | 2 / 4、最大2.433 ms | `SB-RL` + `MONITOR` | 各方向3.0→6.0 |
| 15 | Sandy Island | `StaticBounds` / `KK_1700m_runway` | 2 / 4、最大23.641 ms | `SB` + `PERF-P0`。島地形回廊と取得負荷を分離、指紋キャッシュ必須 | 各方向3.0→6.0 |
| 16 | South Field | `StaticBounds` / `KK_2500m_runway` | 2 / 6、最大19.018 ms | `SB` + `PERF-P0`。主スレッド取得を分割/キャッシュ | 各方向3.0→6.0 |
| 17 | Meeda Naval Air Station | `ManualRequired` / `KK_2500mX_runway` | 2 / 5、最大4.866 ms | `MR-X` + `MONITOR`。交差軸ごとに両閾値を手動登録 | 各軸・各方向3.0→6.0 |
| 18 | Uberdam Airfield | `StaticBounds` / `KK_2500m_runway` | 4 / 7、最大2.966 ms | `SB` + `MONITOR`。ダム/構造物を人工障害物回廊へ含める | 各方向3.0→6.0 |
| 19 | TSC Runway 27 | `PairedThresholds` / `UniversalSpawnPoint` / `TSC_MAIN_RUNWAY` | 1.50 ms超過警告なし | `PAIR`。No.20と一つの物理滑走路へ結合し、27閾値として検査 | RWY27を独立3.0→6.0 |
| 20 | TSC Runway 09 | `PairedThresholds` / `UniversalSpawnPoint` / `TSC_MAIN_RUNWAY` | 1.50 ms超過警告なし | `PAIR`。No.19と相互距離・headingを検証し、09閾値として検査 | RWY09を独立3.0→6.0 |
| 21 | Cove Runway | `StaticBounds` / `KSR_1700m_runway_SNOW` | 1.50 ms超過警告なし | `SB-SNOW`。雪面Collider、PQS、表面連続性を確認 | 各方向3.0→6.0 |
| 22 | Area 52 X-Runway | `ManualRequired` / `KK_2500mX_runway` | 4 / 10、最大4.881 ms | `MR-X` + `MONITOR`。Xの各軸を分離。No.23とは同一airfield内の別物理滑走路 | 各軸・各方向3.0→6.0 |
| 23 | Area 52 Long runway | `StaticBounds` / `KK_4800m_runway` | 4 / 7、最大5.991 ms | `SB-LARGE` + `PERF-P1`。No.22と混同せず、指紋キャッシュ | 各方向3.0→6.0 |
| 24 | Black Krags GC Runway | `StaticBounds` / `KSR_1700m_runway_SNOW` | 1.50 ms超過警告なし | `SB-SNOW`。山岳/雪面の横幅付き回廊を重点検査 | 各方向3.0→6.0 |
| 25 | Dull Spot Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 6、最大3.050 ms | `SB` + `MONITOR` | 各方向3.0→6.0 |
| 26 | Dundard's Edge Runway | `StaticBounds` / `KK_1700m_runway` | 4 / 7、最大2.654 ms | `SB` + `MONITOR`。地形端の両進入を別判定 | 各方向3.0→6.0 |
| 27 | Goldpool Runway | `StaticBounds` / `KK_1700m_runway` | 3 / 4、最大105.226 ms | `SB` + `PERF-P0`。最優先性能隔離。オンライン全Mesh反復走査を禁止し、指紋キャッシュ/オフライン幾何へ移す | 各方向3.0→6.0 |
| 28 | Green Coast Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 7、最大2.388 ms | `SB` + `MONITOR`。沿岸地形を横方向にも検査 | 各方向3.0→6.0 |
| 29 | Green Peaks Runway | `StaticBounds` / `KK_1700m_runway` | 4 / 4、最大1.915 ms | `SB` + `MONITOR`。山岳側と反対側を独立判定 | 各方向3.0→6.0 |
| 30 | Guardians Basin Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 6、最大2.948 ms | `SB` + `MONITOR`。盆地出口と復行回廊を重点検査 | 各方向3.0→6.0 |
| 31 | Hanbert's Cape Runway | `StaticBounds` / `KK_1700m_runway` | 3 / 3、最大1.587 ms | `SB` + `MONITOR`。岬地形を方向別に検査 | 各方向3.0→6.0 |
| 32 | Kerbin's Bottom Runway | `StaticBounds` / `KSR_1700m_runway_SNOW` | 1.50 ms超過警告なし | `SB-SNOW`。雪面/低地地形、表面連続性を確認 | 各方向3.0→6.0 |
| 33 | Kerman Lake Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 7、最大3.263 ms | `SB` + `MONITOR`。湖岸/水面境界を回廊へ含める | 各方向3.0→6.0 |
| 34 | Lake Dermal Runway | `ManualRequired` / `KK_2500mX_runway` | 4 / 10、最大6.887 ms | `MR-X` + `PERF-P1`。交差軸を手動分離し、各閾値を明示 | 各軸・各方向3.0→6.0 |
| 35 | Lodnie Isles Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 8、最大2.818 ms | `SB` + `MONITOR`。島間/海岸回廊を横幅付きで検査 | 各方向3.0→6.0 |
| 36 | Lushlands Runway | `StaticBounds` / `KK_4800m_runway` | 4 / 6、最大5.673 ms | `SB-LARGE` + `PERF-P1`。大型モデル指紋キャッシュ | 各方向3.0→6.0 |
| 37 | Mahi Runway | `StaticBounds` / `KK_1700m_runway` | 3 / 5、最大1.759 ms | `SB` + `MONITOR`。短い滑走路として停止距離を機体別判定 | 各方向3.0→6.0 |
| 38 | Glacier Lake Runway | `StaticBounds` / `KK_2500m_runway` | 4 / 8、最大18.433 ms | `SB` + `PERF-P0`。No.39と別物理滑走路、地形/Static/負荷を別処理 | 各方向3.0→6.0 |
| 39 | Glacier Lake Long Runway | `StaticBounds` / `KK_4800m_runway` | 4 / 7、最大11.958 ms | `SB-LARGE` + `PERF-P0`。No.38と同一airfield内で別ID、指紋キャッシュ | 各方向3.0→6.0 |
| 40 | Sea's End Runway | `StaticBounds` / `KK_1700m_runway` | 4 / 8、最大15.271 ms | `SB` + `PERF-P0`。海岸端と復行回廊、取得負荷を重点処理 | 各方向3.0→6.0 |
| 41 | South Hope Runway | `StaticBounds` / `KSR_1700m_runway_SNOW` | 1 / 1、最大3.775 ms | `SB-SNOW` + `MONITOR`。雪面/地形を方向別検査 | 各方向3.0→6.0 |

## 4. 全施設共通の判定順

1. Providerの施設ID、グループID、モデルIDを確定。
2. カタログ方式を実行時に強制。
3. 一つの物理滑走路へ一つのstable IDを付与。
4. 両閾値、heading、長さ、幅、表面を確定。
5. A/Bまたは09/27を別の進入方向として生成。
6. 各方向で3.0～6.0°の三次元回廊を評価。
7. 進入復行と機体適合を評価。
8. 合格段階と失敗理由を方向別に保存。

## 5. 現時点での禁止判定

- 41件すべて検出成功
- 41件すべて認証失敗
- 33件認証失敗
- 8件未検出
- v0.17.0.2で全滑走路修正済み

これらは現在の証拠からは言えない。確定しているのは、旧版でステージ全体がID衝突により拒否されたこと、v0.17.0.2がその衝突をソース上修正したこと、そして修正版の実機commit結果がまだないことだけである。

