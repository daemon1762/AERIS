# AERIS11 Phase 1 旧NAV完全削除監査

対象：AERIS Flight Control v0.15.0.0  
基準原本：v0.14.5.0  
作成日：2026-07-22

## 1. 目的

旧NAVへ追加修正を行わず、独立LANDと将来の新NAVを安全に構築できる原本を作る。
本版はLANDや新NAVを実装せず、旧NAVの実行コード、所有権、副作用、設定、API、FDR、配布試験を除去する。

## 2. 完全削除した専用ソース

```text
Autopilot/AERISNavDirector.cs
Autopilot/AERISRouteSpeedPlanner.cs
Autopilot/AERISTrajectoryPrimitives.cs
```

旧`AERISNavDirector`に含まれていた以下の能力は現行ソースに存在しない。

- Flight Plan sequencing
- 直線Leg・Fly-by Arc誘導
- Lateral／Vertical／Speed Guidance
- Turn Efficiency・Radius Recovery
- 旧着陸Waypoint制御
- 旧LAND・Flare・Touchdown
- Go-Around再進入
- Path Integrity・Recovery
- NAVからAPへのCommand書込み

## 3. 既存ファイルから除去した接続

### AERISBootstrap

- NAVインスタンス、Update、Speed update、場面遷移通知を削除
- Auto Takeoff後のNAV復帰を削除
- NAV横・縦・速度所有権を削除
- NAV専用Recorder呼出しを削除
- `SetNavigationArm`は安全な拒否境界のみとした

### UI / ND / FDI

- 旧NAV状態・Leg・Arc・Landing表示を削除
- NAV ARMを無効化
- NDを旧経路を描かない中立シェルへ変更
- 通常AP計器、Protect表示、NavBall連動、描画クリップ・相対スケールは維持

### External Automation

- 旧NAV Landing request、plan descriptor、開始・列挙APIを削除
- NAV mission更新、状態保存、rollback、completion判定を削除
- Contract v2のCapability数値1・2は将来互換用の予約値として維持するが、実行入口もCapability広告も持たない

### Recorder

- NAV diagnostics CSV、Arc／Leg／Landing／Path Integrity診断を削除
- HDG・V/Sに残っていた旧NAV専用列を削除
- 通常AP、Ground、Auto Takeoff、Protect、CVR、一般FDR、自動ZIPを維持

### Settings

旧`ApNav*`／`apNav*`設定を削除し、以下の中立設定へ置換した。

```text
flightPlanSelectedBody
flightPlanSelectedId
flightPlanSectionExpanded
navigationDisplayEnabled
navigationDisplayTrackUp
navigationDisplayAutoRange
navigationDisplayManualRangeMeters
```

## 4. データ専用フライトプラン基盤

既存CFG形式と選択UIを維持するため、制御能力を持たない以下を新設した。

```text
AERISFlightPlanFix
AERISFlightPlanDefinition
AERISFlightPlanLibrary
```

このライブラリはCFGの選択・表示・将来解析のみを担当する。`FlightCtrlState`、AP Director、`SetArmed`、Guidance、sequencingを参照しない。
旧IAF／FAF／RW／STOPフラグは滑走路測量資料として読み取るだけで、着陸制御には使用しない。

## 5. 維持した共通基盤

- BANK／HDG／PITCH／V/S／ALT／ACC／VEL
- AA StandardFlyByWire
- Protect／Anti-Stall
- Auto Takeoff
- Ground Stability／Ground Assist
- Speed Airbrake
- FlightState
- APP propulsion integration
- External Setpoint Guidance等の汎用API
- FDI／ND配置・描画安全基盤
- FDR／CVR／FlightData managed ZIP
- 設定・Toolbar・scene/vessel reset

## 6. 安全なPhase 1動作

NAV UIは表示でき、既存CFGを選択できる。ただしNAV ARMは常に拒否する。

```text
NAV RESET
CONTROL UNAVAILABLE
INDEPENDENT LAND DEVELOPMENT IN PROGRESS
```

NAV操作によってBANK、HDG、PITCH、V/S、ALT、ACC、VELをARMせず、`FlightCtrlState`へ書き込まない。

## 7. 監査結果

以下の旧識別子・入口は現行ソースで0件。

```text
AERISNavDirector
AERISNavFlightPlan
AERISNavWaypoint
AERISRouteSpeedPlanner
AERISTrajectoryPrimitives
core.Nav
SetNavMaster
SampleNavDiagnostics
TryStartNavLanding
TryGetCompatibleNavPlans
AERISNavLandingRequest
AERISNavPlanDescriptor
SetManagedBankOverride
SetManagedYawRateOverride
NavPathCaptureAuthority
NAV_LANDING
fdr_nav_diagnostics
ApNav / apNav
```

## 8. 次工程

次はPhase 2の独立LAND設計・実装へ進む。
旧NAVコードを現行原本へ戻さず、旧滑走路座標は測量資料としてのみ利用する。
