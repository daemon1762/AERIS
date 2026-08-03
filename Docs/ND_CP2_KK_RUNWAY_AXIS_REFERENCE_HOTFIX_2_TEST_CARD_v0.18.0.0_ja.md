# ND CP2 KK Runway Axis Reference Hotfix 2 実機試験カード

## 対象

`AERISFlightControl-v0.18.0.0_DEV_CP2_KKRunwayAxisRegistrationHotfix2_AutoPreloadProgression1_Source.zip`

CP2はOPEN。旧`AERISFlightControl(15).zip`と今回の`AERISFlightControl(16).zip`はFAIL比較証拠であり、PASS証拠には昇格しない。

## 1. SHA・静的受入・native build

1. 配布SHA-256を確認する。
2. ZIPを新規ディレクトリへ展開する。
3. `Tools/run_v01800_cp2_acceptance.py`を実行する。
4. `build_ubuntu.sh <KSP path>`を実行する。
5. 起動ログのbuild identity末尾に`AXIS REFERENCE HOTFIX 2`があることを確認する。

## 2. 初回再測量

1. KSPを完全終了する。
2. 本Hotfixを導入する。
3. Flightへ入り、Airfield reloadのatomic commitまで操作を待つ。
4. 次の対象が`[AIRFIELD_CACHE] exact miss`またはrevision再測量対象になることを確認する。
   - Dundard's Edge Runway
   - Kola Island
   - Mahi Runway
   - Goldpool Runway
   - Uberdam Airfield
   - Cape Kerman
   - Kojave Sands
   - Polar Research Alpha
   - Sandy Island
5. 各対象で`[RUNWAY_AXIS]`を保存する。

## 3. Axis判定PASS

代表空港ごとに次を確認する。

- `axisReference=LAUNCH_ANCHOR`
- `axisReferenceErrorDeg <= 15.00`
- `axisRegistrationValid=True`
- `absolutePlacementValid=True`
- `launchCrossAfterM=0.00`

特にDundard's Edgeは、mesh headingが約`133.57°`でも静的モデルorientation `0°`を理由に拒否されないこと。

## 4. 認証集計

FAIL再現値：

- `CERTIFIED 3 RWY / 6 APP`
- `FAILED 40 RWY / 80 APP`

合格値：

- 最低`CERTIFIED 14 RWY / 24 APP`以上へ回復。
- 代表6空港が`AbsolutePlacementInvalid`としてFailureRecordへ再保存されない。
- `MEASURED`がAxis Revision 2対象を処理したことを示す。

## 5. ND/LAND照合

Kola Island、Dundard's Edge、Mahi、Uberdam、Kojave Sands、Sandy Islandで以下を確認する。

1. LAND/NDの空港選択一覧に表示される。
2. 両方向滑走路名が物理方位と一致する。
3. ND滑走路線が実舗装中心線へ一致する。
4. runway map lock errorが許容値内である。
5. LAND ARMは観測専用のままで操縦権限を得ない。

## 6. キャッシュ再起動試験

1. KSPを終了する。
2. 再起動して同一Flightへ入る。
3. Axis Revision 2で保存した正常レコードが`CACHE`利用されることを確認する。
4. 同じ対象が毎回`AbsolutePlacementInvalid`へ戻らないことを確認する。

## 7. Auto Preload継続確認

このHotfixはAuto Preloadアルゴリズムを変更しない。別系統として次を保存する。

- Kerbinの`[PRELOAD_AUTO] event=COMPLETE target=FAR_GLOBAL`
- 他固体天体への自動選択
- 全固体天体Far完了後の`event=PROMOTE`
- Point-only Land高精細化

空港認証PASSだけでAuto Preload全体をPASSにしない。

## ログ抽出

```bash
rg '\[(AIRFIELD_RELOAD|AIRFIELD_CACHE|RUNWAY_AXIS|RUNWAY_PLACEMENT|PRELOAD_AUTO)\]' \
  GameData/AERISFlightControl/Logs/AERISFlightControl.log
```
