# AERIS12 第一関門 実機試験カード

対象：AERIS Flight Control v0.16.0.1以降

## この版で確認するもの

本版は独立LANDの基盤試験版である。滑走路を選び、進入幾何をNDで観測するが、機体の操縦は行わない。

```text
CONTROL  PILOT
LOC      WAIT
GS       WAIT
```

## 事前条件

- KSP 1.12.5
- KSP-RO Kerbal Konstructs導入済み
- Stock Launchsites Expansion 2.1導入済み
- 固定翼機のVessel Typeが`Plane`

## A. 起動・既存機能回帰

1. KSPを起動し、固定翼機でFlightへ入る。
2. AERISが対象ビルドのVersionとして読み込まれることを確認する。
3. 通常AP、AA FBW、Protect、Auto Takeoff、Ground Assistが従来どおり使用できることを確認する。
4. NAVが旧Guidanceを持たず、安全無効のままであることを確認する。

## B. Airfield Registry

1. LAND欄を開く。
2. Provider状態にKSP-RO KKがACTIVEとして表示されることを確認する。
3. Stock、DLC、KK、SLEの施設が同じ一覧へ表示されることを確認する。
4. 滑走路、Launch Pad、Helipad、Harbourが区別されることを確認する。
5. `LAND-CAPABLE ONLY`相当の表示条件で非滑走路施設が除外されることを確認する。

## C. 未検証KK/SLE滑走路の安全拒否

1. SLEまたは任意KK基地の滑走路を選択する。
2. `RUNWAY GEOMETRY REQUIRED`または同等の未検証表示を確認する。
3. LAND ARMが拒否されることを確認する。
4. 拒否後も通常AP、AA、スロットル、ブレーキ、エアブレーキへ変化がないことを確認する。

## D. KSC / Island方向別進入

1. KSC Main Runwayを選び、RWY 09とRWY 27が別々に選べることを確認する。
2. Island AirfieldでもRWY 09とRWY 27が別々に選べることを確認する。
3. 地上でARMし、`AIRBORNE FIXED-WING FLIGHT REQUIRED`として拒否されることを確認する。
4. Vessel Typeが`Plane`ではない機体でARMが拒否されることを確認する。
5. 固定翼機で離陸し、KSCまたはIslandの進入方向を選択して`ARM LAND — OBSERVE`を押す。
6. 次の状態を確認する。

```text
LAND       ARMED
CONTROL    PILOT
LOC        WAIT
GS         WAIT
```

## E. ND観測表示

1. 選択滑走路、中心線、Localizer延長線、捕捉漏斗がND上面に表示されることを確認する。
2. Glide Pathと現在高度がND縦断表示に現れることを確認する。
3. 機体位置を変え、距離、Cross Track、Intercept Angle、GS誤差、捕捉不能理由が更新されることを確認する。
4. LAND ARM中も操縦権がパイロットに残ることを確認する。
5. 通常APを使用中の場合、LAND ARMがDirectorのARM状態や目標値を書き換えないことを確認する。

## F. リセット境界

1. 滑走路方向を変更し、LANDが解除されることを確認する。
2. Active Vesselを切り替え、LANDが解除されることを確認する。
3. FlightからSpace CenterまたはMain Menuへ遷移し、LAND所有権・ARM・Commandが残らないことを確認する。
4. 再度Flightへ入り、Registryが再構築され、KK/SLE施設が重複登録されないことを確認する。

## 返却資料

- `KSP.log`
- `GameData/AERISFlightControl/Logs/`内の当該ログ
- LAND欄とNDのスクリーンショット
- SLE基地一覧で、滑走路／非滑走路の分類が不自然だった施設名

## 第一関門PASS条件

```text
KSP起動・AERIS読込み                 PASS
既存AP / AA / Protect回帰            PASS
KK/SLE Provider列挙                  PASS
施設種別分類                          PASS
未検証滑走路ARM拒否                   PASS
KSC / Island RWY 09・27分離           PASS
空中Planeのみ観測ARM                  PASS
CONTROL=PILOT / LOC=WAIT / GS=WAIT    PASS
ND進入幾何表示                        PASS
Scene / Vessel変更時完全リセット       PASS
操縦Command非介入                     PASS
```
