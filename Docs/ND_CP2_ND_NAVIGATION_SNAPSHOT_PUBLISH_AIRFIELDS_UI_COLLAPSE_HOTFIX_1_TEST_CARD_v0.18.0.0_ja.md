# 実機試験カード — ND Snapshot Publish / AIRFIELDS UI Collapse Hotfix 1

## 0. 前提

- `UserRunwayCalibrations.cfg`と`AirfieldCertificationCache.cfg`は削除しない。
- Kolaの既存手動校正を継承して起動する。
- Build identity末尾が次であること。

`ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1`

## 1. Native build

- Mono/xbuildがError 0で完了する。
- Warningは既知warningのみ。
- DLLがGameDataへ配置される。

## 2. AIRFIELDS初期表示

SYSTEM > AIRFIELDSを初めて開く。

期待値：

- CERTIFIED：閉
- PROVISIONAL：閉
- FAILED：閉
- PENDING：閉
- REVALIDATION：閉
- 上部build表示が`AERIS v0.18.0.0 DEV CP2 — FLIGHT CONTROL`程度の短い表示である。
- STATUS、RESULT、SURVEY、WITNESS、CALIBRATION文字が重ならない。

## 3. AIRFIELDS展開表示

CERTIFIEDを開き、Kolaを開く。

期待値：

- 空港名／滑走路名と方位・長さ・状態が2行に分かれる。
- Provider、Basis、Geometry、Runway、Threshold、Uncertainty、Calibrationの各行が折り返される。
- CHECK HEREボタン文字が切れない。
- 横方向へUIが伸びず、縦スクロールだけで閲覧できる。
- いったん閉じて再度開けば開閉状態を保存できる。

## 4. ND起動時

空港DBの初回commit後、ND右上を確認する。

期待値：

- `NAV DATA`ではなく`<数> RWY`を表示する。
- Kola周辺5k/10k/20kで校正滑走路が表示される。
- 80k/160kでも範囲内の認証済み空港が表示される。

## 5. Revision更新後の再表示

AIRFIELDSで`RELOAD / RESCAN`を実行する。またはKolaをCLEAR→MARK A→MARK Bで再登録する。

期待値：

- Database Revision更新中は一時的に旧frameがstaleとなってもよい。
- commit後、worker処理を経て滑走路表示が自動復帰する。
- NDが`NAV DATA`のまま固定されない。
- 選択中の滑走路と空港一覧が新Revisionへ同期する。

## 6. 校正回帰

Kolaについて次を確認する。

- `fullRoundTrip=True`
- `committedReadback=True`
- `reciprocalPairSchema=3`
- `RECIPROCAL PAIR GENERATED`
- A→B／B→Aの両方向が存在
- 再起動後も両方向が維持

## 7. 安全境界

ログと挙動で以下を確認する。

- 旧NAV復活なし。
- LANDは観測専用。
- AIRFIELDS UI操作で機体操縦、スロットル、ブレーキ、操舵、ギア、APP推進値が変化しない。
- CP2候補デバッグオーバーレイは復活しない。

## 合否

上記すべてPASSかつAuto Preload残存条件がPASSした場合のみ、CP2 CLOSE監査へ進む。
