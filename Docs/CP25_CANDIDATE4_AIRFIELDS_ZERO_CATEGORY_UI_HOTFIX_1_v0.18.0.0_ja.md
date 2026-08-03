# AERIS v0.18.0.0 CP2.5 Candidate 4 Airfields Zero-Category UI Hotfix 1

## 発見
2026-07-31の実機動画を事前確認した。動画ではAIRFIELDSページの長時間表示は限定的だったが、同一セッションのUI遷移とソース監査から、0件カテゴリの早期returnが `USER CALIBRATED — MANUAL` のみに限定されていることを確認した。他カテゴリは保存済みexpanded状態のまま空子レイアウトへ入り、巨大な空白を生成し得る。

## 修正
AIRFIELDSの全カテゴリに同一の0件処理を適用する。

- USER CALIBRATED — MANUAL
- CERTIFIED — AUTOMATIC / PROVIDER
- PROVISIONAL — NON-SELECTABLE
- FAILED
- PENDING
- REVALIDATION

件数0の場合は、展開状態を強制的にfalseへ正規化して保存し、見出しを無効化して描画する。toggle、`None.`、行・詳細レイアウトは一切生成しない。該当カテゴリの古いdetail keyも破棄する。`GUI.enabled`はfinallyで必ず復元する。

## 境界
変更は `UI/AERISWindow.cs` のAIRFIELDSカテゴリ描画と版名・検査資料だけ。Preload throughput、Backpressure、AA、AP、PROTECT、LAND、Map DRAM、滑走路データは変更しない。
