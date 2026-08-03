# CP3 Gate 1 Compile Hotfix 1 実機テストカード

## 1. ビルド

`build_ubuntu.sh`を通常どおり実行する。

合格条件：
- `CS0103 ApplyStandardSchedulerState`が出ない。
- xbuildが0 errorで完了する。
- DLLがKSPの`GameData/AERISFlightControl/Plugins`へ配置される。

## 2. KSP smoke

KSPを1回起動し、Flightへ入る。

合格条件：
- AERIS DLLがロードされる。
- `[CP3_RESIDENT]`ログに`payloadRoute=DISCONNECTED`が出る。
- 非Flight Terrain PreloadのSTANDARD状態とFlight中のSUSPENDED状態が切り替わる。
- FULL BOOST操作・ログ・状態が存在しない。
- AA/AP/PROTECT/LANDの既存動作に回帰がない。
