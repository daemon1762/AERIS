# CP2 Manual Calibrated Runway Separation / Preservation Hotfix 1

## 原因

`AERISFlightControl(22)`では、Kola IslandのA/B二点校正は一度正常に保存・再読込され、相反する2方向も生成された。

しかし、その後に手動校正済み方向へ`CHECK HERE`を実行すると、汎用位置ずれ判定が再度作動した。`RecordPlacementMismatch()`は既存のA/B端点を消去して隔離状態へ戻す設計だったため、校正済み滑走路がAIRFIELDSおよびNDから消えた。

## 修正

1. `CertificationBasis == UserCalibrated`の方向では、`CHECK HERE`による自動隔離を禁止する。
2. Witness保存層でも、完成済みA/Bペアの端点消去を拒否する二重防壁を設ける。
3. 手動校正済み滑走路を`CERTIFIED — AUTOMATIC / PROVIDER`から分離し、`USER CALIBRATED — MANUAL`へ表示する。
4. 手動校正一覧は閉じた状態をデフォルトとする。
5. 手動校正を変更する場合は、ユーザーが明示的に`CLEAR`した後で`MARK A/B`を行う。

## 安全境界

手動校正方向は従来どおり運用上選択可能である。ただし、これは自動・provider認証と同一表示にはせず、由来を明示する。今回の変更はAP、LAND操縦権限、FlightCtrlState、推力、ブレーキ、舵面へ書き込まない。
