# External Automation — Phase 1状態

v0.15.0.0では旧NAV Landing固有APIを削除した。

## 利用可能な方向性

実行環境と安全条件を満たす場合、以下の汎用Capabilityを維持する。

- SetpointGuidance
- GroundPropulsionTest
- AutoTakeoff
- LearningCorridor
- EnvelopeSurvey
- AntiStallEvent
- ControlAuthorityTelemetry
- GroundAssistStop
- ExternalTrimFeedForward
- ExternalTaskDisplay
- ResourceOverrideCoordination

## 予約Capability

Contract v2の数値互換のため、Capability ID 1と2は`Navigation`、`AutoLanding`の予約値として列挙体に残す。
ただし本版ではCapability一覧へ広告せず、開始API、request DTO、plan descriptor、runtime missionを持たない。

独立LAND完成後は、旧NAV Landing契約を復元せず、新しいLAND固有の汎用入口を定義する。
