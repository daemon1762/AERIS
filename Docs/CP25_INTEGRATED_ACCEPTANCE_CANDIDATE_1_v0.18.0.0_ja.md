# CP2.5 Integrated Acceptance Candidate 1

## 目的

CP2.5 Gate 1～4を単一パッケージで総合受入する候補版である。Gate 4 Hotfix 2の実機合格原本を基礎とし、滑走路Track BやCP3の地形本体RAM常駐には着手しない。

## 同梱修正：手動補正済み0件UI

AIRFIELDSの`USER CALIBRATED — MANUAL`が0件であるにもかかわらず、保存済みの展開状態が残っている場合にUIレイアウトが不安定になる現象を修正した。

- 0件なら展開状態を強制的に閉じて保存する。
- `USER CALIBRATED — MANUAL (0)`の見出し自体は表示する。
- 見出しは無効化され、クリックしても展開しない。
- 子要素や`None.`行を生成しない。
- 描画後は`GUI.enabled`を必ず元へ戻す。
- 他の認証カテゴリの動作は変更しない。

## Gate 4 Hotfix 2 実機結果

2026-07-29のAERIS31試験で以下を確認した。

- Airfield 93、Physical Runway 43、Direction 86をatomic publish。
- Airfield lookupは実際にDRAM snapshotとstable-ID indexを使用。
- guarded SSD 338、allowed SSD 338。
- normal lookup同期SSD違反 0。
- shutdown summaryは`result=PASS`。

## 総合受入範囲

1. Gate 1：40.5km OFF／39.5km未満ONの高度ヒステリシス。
2. Gate 2：AUTO／LOW／MEDIUM／HIGHとDeveloper LAND隔離。
3. Gate 3：LAND capability＋LAND ARMによるruntime要求分離。
4. Gate 4：Map DRAM metadata snapshot、DRAM-only lookup、SSD guard。
5. AIRFIELDS 0件カテゴリ、スクロール、ウィンドウ移動・リサイズ。
6. CP1／CP2凍結回帰、Preload、ND、LAND foundation、AP／AA／PROTECT非退行。

## 境界

- 手動補正済み滑走路データそのものは追加しない。
- A/B絶対測地デフォルトは後日のTrack Bで扱う。
- Terrain payloadのRAM常駐と速度予測型前方回廊はCP3で扱う。
