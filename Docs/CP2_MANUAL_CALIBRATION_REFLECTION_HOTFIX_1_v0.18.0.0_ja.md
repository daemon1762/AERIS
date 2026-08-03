# AERIS v0.18.0.0 CP2 Manual Calibration Reflection Hotfix 1

## 目的

A/B二点校正が保存・認証まで成功しているにもかかわらず、AIRFIELDS画面とNDで結果が反映されていないように見える問題を修正する。

## 実機証拠

`AERISFlightControl(23).zip`ではKola Islandの校正ファイルにA/B双方が保存され、相反方向ペアも成立していた。

- `hasStart = True`
- `hasEnd = True`
- `reciprocalDirectionPair = True`
- `directionAHeadingDeg = 195.8379...`
- `directionBHeadingDeg = 15.8379...`

再測量ログでも次が成立した。

- `RECIPROCAL PAIR GENERATED`
- `basis=UserCalibrated`
- `absolutePlacementValid=True`
- `CERTIFIED 13 RWY / 19 APP`

しかしUIには旧自動判定のKola方向が`FAILED / USER CALIBRATION REQUIRED`として残り、全体校正状態も過去の隔離メッセージを表示した。またNDは滑走路タイトルが12 RWYとなり、登録DBの13 RWYと一致しなかった。

## 根本原因

1. AIRFIELDSのカテゴリ分離はCertified方向だけを分離しており、手動校正に置き換えられた旧provider方向をFAILED等から除外していなかった。
2. ND Snapshotは各滑走路の「最初の有限geometry 2方向」を採用しており、Certified／UserCalibratedを優先していなかった。旧FAILED geometryが先に並ぶ場合、手動A/B端点を公開できなかった。
3. Witness reload時に`CalibrationStatus`を保存済み内容から再構築せず、以前の隔離メッセージが残る場合があった。
4. MARK A/B完了後も手動カテゴリが閉じたままで、FAILEDリストのスクロール位置も維持されたため、成功結果が視覚的に確認しにくかった。

## 修正

- 1空港内にCertifiedなUserCalibrated方向が2方向存在する場合、そのペアをauthoritativeとする。
- authoritativeな手動ペア成立後、同じ物理滑走路の非手動方向を`superseded`として通常カテゴリ、件数、状態判定、選択対象から除外する。
- LAND／NDの選択対象は手動ペアのみとする。
- ND SnapshotはCertified geometryだけから方向ペアを選び、手動ペアがある場合はUserCalibratedだけを採用する。
- 2方向目は180度差に最も近い方向を選ぶ。
- Witness reload時に保存済み校正から状態表示を再構築する。
- A/B完成時は手動カテゴリを自動で開き、FAILED等を閉じ、スクロールを先頭へ戻す。
- 通常起動時のカテゴリ初期値は従来どおり閉じた状態を維持する。

## 安全境界

- Kola固有分岐は追加しない。
- 手動ペアの保存形式、schema、端点座標は変更しない。
- LAND/AP/APPへの操縦権限は追加しない。
- CP2デバッグ表示は復活させない。
