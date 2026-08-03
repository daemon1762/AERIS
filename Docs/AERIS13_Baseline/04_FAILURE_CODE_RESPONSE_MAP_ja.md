# 滑走路認証失敗コード別・対応表

対象: v0.17.0.2の失敗コードと次世代認証器  
原則: 原因を角度変更で隠さない。読めない、決められない、矛盾する場合はfail-closed。

## 1. 今回実際に観測された全体障害

| 事象 | 現在の証拠 | 対応 |
|---|---|---|
| `DUPLICATE AIRFIELD Kerbin / DISC_STOCK_KSP` | 旧版CVRの手動generation 2、3で発生。revision 0を保持 | v0.17.0.2でStock/DLCを施設ID、KK/SLEをグループIDに分離済み。重複検査は維持。起動+手動2回の実KSP commitで最終確認 |
| `AIRFIELD_SNAPSHOT ... 1.50 ms` | 33施設、98警告 | 認証失敗ではなく性能警告。指紋キャッシュ、分割取得、オフライン/手動幾何で改善。安全条件を緩和しない |
| 外側FlightData ZIP破損 | 中央ディレクトリと末尾欠落 | CRC確認できた内容だけ採用。アーカイブは展開・バイト照合後に原本削除 |

## 2. 方向・滑走路失敗コード

| コード | 意味 | 自動処置 | 人手での復旧条件 |
|---|---|---|---|
| `None` | 失敗なし | 他の全ゲートを継続 | `None`だけで認証済みと表示しない |
| `NotFixedWingRunway` | 固定翼滑走路ではない | LAND候補から恒久除外 | Provider分類の誤りを証明した場合だけ再分類 |
| `FacilityCategoryConflict` | Runway/LaunchPad等の分類が矛盾 | 隔離し自動認証禁止 | Provider情報、モデル、現物を照合して一意に分類 |
| `ModelUnavailable` | 対象モデルを取得不能 | `PENDING`、低頻度再試行 | MOD/モデル版、ロード状態、パスを修正 |
| `MeshUnreadable` | Mesh読取り失敗 | 自動認証禁止 | Collider、別LOD、手動閾値等の独立証拠を用意 |
| `ColliderUnavailable` | 有効Colliderなし | 自動認証禁止 | Mesh/Rendererまたは手動幾何で補完 |
| `NoGeometryEvidence` | 滑走路形状の証拠なし | `FAILED`または手動待ち | 両閾値、中心線、幅、表面を明示 |
| `InsufficientEvidence` | 独立証拠が不足 | 自動認証禁止 | 複数の証拠系列を追加し一致を確認 |
| `MultipleGeometrySolutions` | 複数の軸が成立 | 自動選択禁止 | X字等は物理軸ごとに手動定義 |
| `WholeSiteBoundsOnly` | 空港全体boundsしか得られない | 自動認証禁止 | 滑走路子要素または手動閾値を指定 |
| `CenterlineConflict` | 中心線候補が矛盾 | 角度探索へ進まない | 軸、heading、両端を再測量 |
| `ThresholdUnresolved` | 運用閾値が決まらない | 進入方向を不合格 | 閾値座標・標高を明示し現物確認 |
| `DisplacedThresholdUnresolved` | 変位閾値が不明 | 進入方向を不合格 | マーキング/手動距離で使用可能開始点を確定 |
| `RunwayWidthUnresolved` | 幅が不明 | 回廊幅を作れないため不合格 | 複数断面で幅を測定 |
| `SurfaceDiscontinuity` | 表面が連続しない | 滑走路/方向を不合格 | 断絶がLOD誤差か現物かを確認し再測量 |
| `SurfaceSlopeExceeded` | 面勾配が制限超過 | 滑走路/方向を不合格 | 制限値を緩めず、現物と機体能力を手動審査 |
| `RunwayTooShort` | 使用可能長不足 | 対象機体のLAND候補から除外 | 機体別必要長を満たす場合だけ限定適格 |
| `RunwayTooNarrow` | 幅不足 | 対象機体のLAND候補から除外 | 翼幅・横誤差を含む機体別適格を証明 |
| `ApproachTerrainBlocked` | 地形余裕不足 | 3.0～6.0°を0.1°刻みで再評価 | 全回廊が成立しなければその方向を閉鎖 |
| `ApproachObstacleBlocked` | 人工障害物/粗回廊が遮られる | 上昇で解消可能か全回廊再評価 | 横侵入や復行不能ならその方向を閉鎖 |
| `ReciprocalMismatch` | 反対方向との幾何が整合しない | 両方向を一時保留 | 同一物理滑走路の両端・headingを再測量 |
| `MeasurementDisagreement` | 測定手法間で値が不一致 | 自動認証禁止 | 外れ値を特定し、独立再測定で収束 |
| `ProviderDataError` | Provider値が不正 | 当該レコードを隔離 | UUID/site/group/category/versionを修正 |
| `ModelChanged` | モデル参照変更 | `REVALIDATION` | 新モデルを全再測量 |
| `PositionChanged` | 設置位置変更 | `REVALIDATION` | 地形・進入・復行を含む全再認証 |
| `RotationOrScaleChanged` | 回転/縮尺変更 | `REVALIDATION` | heading、長さ、幅、両方向を全再認証 |
| `MeshFingerprintChanged` | Mesh指紋変更 | `REVALIDATION` | キャッシュを使わず全再測量 |
| `ProviderVersionChanged` | MOD版変更 | `REVALIDATION` | 変更影響を再測量し新指紋を保存 |
| `SurveyTimeout` | 規定時間内に完了しない | 安全側に失敗、低負荷時再試行 | 大型モデルは指紋キャッシュ/オフライン抽出/手動幾何へ移行 |
| `UnsupportedLayout` | X字、複合、特殊配置等 | 自動認証禁止 | 物理滑走路ごとの明示定義を追加 |
| `WorkerFailure` | ワーカー内部失敗 | 世代を破棄、DBをcommitしない | 例外全文を保存し、再起動後に限定再試行 |

## 3. 状態遷移の規則

```text
PENDING
  ├─ 十分な幾何証拠 → CORRIDOR VALIDATION
  ├─ 手動定義待ち   → PENDING / AUTO INHIBIT
  └─ 回復不能       → FAILED

CORRIDOR VALIDATION
  ├─ 方向別合格     → CORRIDOR VALIDATED
  └─ 方向別不合格   → FAILED（反対方向は独立）

CORRIDOR VALIDATED
  ├─ 機体適合       → AIRCRAFT ELIGIBLE
  └─ 機体不適合     → GEOMETRY VALID / AIRCRAFT INELIGIBLE
```

現行の一語`CERTIFIED`へ複数段階を押し込めない。

