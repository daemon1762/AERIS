# AERIS13 精密コーディング終了・最終引継ぎ

作成日: 2026-07-23  
基準版: v0.17.0.2 Runway Registry Identity Hotfix Source RC  
工程判断: 今回の精密コーディングを終了し、実機証拠取得と次世代認証設計へ引き渡す。

## 1. 確定した状態

| 項目 | 状態 | 引継ぎ上の扱い |
|---|---|---|
| AP | 完成済み | 制御則を変更しない |
| BANK | 完成済み | 20°捕捉図をゴールド回帰基準にする |
| NAV | 未搭載 | 旧NAVは削除済み。独立LAND完了後に新規構築する |
| LAND | 観測・表示・認証基盤 | `FlightCtrlState`やAPの操舵権限を与えない |
| Performance Runtime | Source RCで受入済み | 実KSP性能値は別途測定する |
| 滑走路ID修正 | ソース・モデル試験合格 | 実KSPコミット成功は未実証 |
| 方向別進入認証 | 不十分 | 自動着陸の安全認証には使用しない |

## 2. 基準物

基準ソースZIP:

```text
AERISFlightControl-v0.17.0.2_RunwayRegistryIdentityHotfix_Source.zip
SHA-256 0c20572af23d47741c82bed7eaaa2e5d737ca861d68ff7b9a1b243abf7f00db7
```

再実行したソース受入:

- 12 / 12スクリプト合格
- 561 / 561アサーション合格
- 配布記録上のC#構文: 108 / 108
- 配布記録上の内部マニフェスト: 183 / 183

ここで証明されるのはソース、モデル、静的契約である。KSP参照DLLを使った実ビルド、実ゲーム内レジストリ更新、滑走路ごとの進入安全性は証明範囲外。

## 3. 添付結果から確定した障害

破損アーカイブから救出した最新CVRでは、手動再走査generation 2と3がどちらも次の理由で失敗した。

```text
STAGED DATABASE INVALID:
DUPLICATE AIRFIELD Kerbin / DISC_STOCK_KSP
previous committed database revision 0 retained
```

これは個々の滑走路認証失敗ではなく、旧版のID生成衝突によりステージ全体が原子的に拒否された結果である。大量の`AIRFIELD_SNAPSHOT`警告が先に記録されているため、候補検出自体がゼロだったわけではない。

v0.17.0.2はこのID衝突を修正する。ただし添付`KSP(4).log`はv0.17.0.2とカタログの読込みを記録した後、ModuleManagerロード中に終了している。飛行シーン、`AIRFIELD_RELOAD`、database commitの記録がないため、修正後の実機成功は未確認。

## 4. コード監査で判明した認証上の未解決事項

### P0: 進入角が全方向3.0°固定

`AERISOperationalRunwayResolver.BuildDirection`は、実測滑走路の両方向へ無条件に`GlidePathAngleDeg = 3.0`を設定している。地形に応じた角度探索はない。

### P0: 進入回廊が中心線16点だけ

現行`ValidateApproach`は、閾値から250～8000 mの中心線上を16点だけ調べ、最低余裕10 m以上なら合格とする。

- 横幅を持つ回廊ではない
- 点と点の間を保証しない
- Static、建造物、樹木等の上端を保証しない
- 進入復行経路を検査しない
- 機体の降下率、AoA、推力、フレア能力を検査しない

### P0: 粗障害ゲートが実質常時許可

スナップショット生成時に`ApproachAAvailable`と`ApproachBAvailable`が無条件で`true`になっている。この経路では`ApproachObstacleBlocked`の粗判定が実質機能しない。

### P0: カタログの測量方式が実行時に強制されない

`ManualRequired`、`StaticBounds`、`PairedThresholds`は読み込まれ、指紋にも入るが、測量ワーカーの分岐条件として使用されていない。

そのため本来は禁止されるべきX字滑走路や汎用SpawnPointも、自動合意測量へ流れ得る。`TSC_MAIN_RUNWAY`のPairKeyも実行時に対向閾値を結合する用途へ使われていない。

### P1: スナップショットの主スレッド超過

復旧CVRでは98件、33施設に1.50 ms超過警告があった。これは認証失敗ではないが、次の大きな外れ値を放置しない。

| 施設 | 最大slice |
|---|---:|
| Goldpool Runway | 105.226 ms |
| Sandy Island | 23.641 ms |
| South Field | 19.018 ms |
| Glacier Lake Runway | 18.433 ms |
| Sea's End Runway | 15.271 ms |
| Glacier Lake Long Runway | 11.958 ms |
| Dununda | 11.509 ms |

安全判定を緩めて高速化してはならない。モデル指紋単位のキャッシュ、オフライン抽出、1施設ずつの測量、再試行間隔の制御で処理時間を下げる。

## 5. 次工程で守る不変条件

1. AP/BANK制御ソースを変更しない。
2. 20°BANKは、滑らかなロールイン、目標前の先行制動、オーバーシュートなし、捕捉時ロールレートほぼ0、保持時マイクロウォブルなしを維持する。
3. 旧NAVを戻さない。新NAVはLANDと分離して新規構築する。
4. LANDへ操舵権限を追加しない。
5. 認証DBはfail-closed、全体commitはatomicのまま維持する。
6. 物理滑走路と進入方向を分離する。09が不合格でも27を自動的に失格にしない。
7. KSC、Island等の設定値も、方向別の新回廊検査なしに精密認証済みとみなさない。
8. ユーザー設定、FlightPlans、Airfields、FlightData、Logsを更新処理で削除しない。

## 6. 次の実装順序

精密コーディングを再開する場合は、次の順序を崩さない。

1. **v0.17.0.2実機証拠取得**  
   起動1回、手動再走査2回のatomic commitと件数安定を証明する。
2. **測量方式のfail-closed強制**  
   `ManualRequired`は明示閾値なしで自動認証禁止。`PairedThresholds`はPairKey結合を必須化。
3. **方向別三次元回廊**  
   地形、Static/Collider、横方向幅、サンプル間、進入復行を検査する。
4. **可変角度選択**  
   原則3.0°から開始し、安全を満たす最小角を方向別に選ぶ。
5. **機体適合判定**  
   角度に必要な降下率、速度、姿勢、AoA、推力、フレア余裕を別ゲートにする。
6. **性能隔離**  
   高負荷施設を指紋キャッシュまたは手動定義へ倒し、主スレッド長時間停止を防ぐ。
7. **表示語の是正**  
   旧`CERTIFIED`と精密認証済みを混同させない。少なくとも`GEOMETRY ONLY`、`CORRIDOR VALIDATED`、`AIRCRAFT ELIGIBLE`を区別する。

## 7. 次の合格ゲート

### レジストリ・ゲート

- v0.17.0.2実行中に`STATE COMPLETE`
- `DATABASE REV >= 1`
- 起動および手動2回で`atomic commit`
- 3回のairfield/runway件数が安定
- `DISC_STOCK_KSP`が一度も出ない
- KSC Main、Islandを別airfieldとして保持
- KK/SLEの同一空港内複数滑走路だけを同一airfieldへ束ねる

### 進入方向ゲート

- 閾値、反対端、中心線、幅、使用可能長、表面が確定
- 測量方式が実行時に守られている
- 方向別の地形・障害物・復行回廊が合格
- 選択角度と最低余裕がログへ残る
- 反対方向の結果と独立
- 機体適合が別判定

### AP/BANK回帰ゲート

滑走路側を変更した場合でも、提示画像のBANK挙動を完全維持する。AP/BANK差分が発生した時点で受入不能。

## 8. 現時点の最終判断

ユーザーの「3度パスにこだわらず、安全範囲で角度を変更する」という判断は正しい。ただし、角度を上げるだけで安全認証は成立しない。方向別の三次元回廊、復行、機体能力を同時に満たし、その中で最小の安全角を選ぶ。

現在版はレジストリ基盤のSource RCとして引き継ぐ。自動着陸認証済み、全滑走路認証成功、またはv0.17.0.2実機合格とは表現しない。

