# CP2.5 総合受入テストカード — Candidate 2

## 0. 起動・ビルド

- SHA-256一致。
- Mono/xbuild成功。
- 起動ログの版名が`CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 2`。
- AERIS起因のERROR／FATAL／Exceptionなし。

## 1. AA操舵面イベント寿命

操舵面を持つ機体で次を実施する。

1. SPHまたはVABへ入り、操舵面付き機体をロードして退出する。
2. Flightへ入り、BANK／HDG／PITCHまたはAA FBWを短時間作動させる。
3. Space Centerへ戻る。
4. 同じ機体または別の操舵面付き機体でFlightへ再進入する。
5. 機体切替、revert、爆散後の再開のうち可能なものを実施する。
6. Main Menuへ戻り、KSPを正常終了する。

期待ログ：

```text
[AA/CONTROL_SURFACE_LIFECYCLE] explicit stock callback cleanup active.
```

不合格ログ：

```text
[KSPCF:MemoryLeaks] ... destroyed AERISFlightControl:SyncModuleControlSurface ...
[AA/CONTROL_SURFACE_LIFECYCLE] cleanup incomplete ...
```

上記不合格ログは0件であること。

## 2. 操縦則非退行

- BANK保持、HDG旋回、PITCHまたはV/Sを各1回実行する。
- 操舵面が中立へ戻る。
- mirror側の向き、deploy、authorityLimiter、actuator速度に異常がない。
- 過大振動、片側逆転、入力残留がない。

## 3. Candidate 1 UI smoke test

- `USER CALIBRATED — MANUAL (0)`が展開不能。
- `None.`や巨大な空白が出ない。
- 下段カテゴリ、スクロール、Resizeが正常。

## 4. Gate 1～4 smoke test

- Gate 1：可能なら40.5km OFF／39.5km未満ONを1往復確認する。
- Gate 2：OPTIONSがAUTO／LOW／MEDIUM／HIGHのみ。
- Gate 3：LAND ARMでACTIVE、DISARMでSTANDBY。
- Gate 4：`SYNC SSD 0 — PASS`、終了時`result=PASS`。

## 5. 長時間・場面遷移

最低3回、Flightと非Flight場面を往復する。可能なら30分以上運用する。

- AERIS所有の破棄済みSyncModuleControlSurface callback：0件。
- callback総数が往復ごとに単調増加しない。
- AERISのmain-thread時間、worker、writer、Terrain、Map DRAMに退行なし。

## 提出物

- `AERISFlightControl.log`
- 最新session log／performance CSV
- `KSP.log`
- 操舵試験と場面遷移が分かる動画
