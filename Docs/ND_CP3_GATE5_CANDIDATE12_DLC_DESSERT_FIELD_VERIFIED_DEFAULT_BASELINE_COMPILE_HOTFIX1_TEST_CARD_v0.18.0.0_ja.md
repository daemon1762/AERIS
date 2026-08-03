# Candidate 12 Compile Hotfix 1 実機テストカード

## 目的
Candidate 11/12の継承selftestが旧CandidateのUiCheckpointを「現行identity」として固定していたため、Candidate 12ビルドがnative compile前に停止する問題を修正する。

## ビルド確認
1. `./build_ubuntu.sh <KSP path>` を実行。
2. Candidate 11回帰で `FAIL: Candidate 11 tab/build identity exact` が出ないこと。
3. Gate 5全selftest完走後、xbuildが実行されDLLがGameDataへinstallされること。
4. ゲーム内タブ表記が `CANDIDATE 12 — DLC DESSERT FIELD-VERIFIED DEFAULT BASELINE — COMPILE HOTFIX 1` であること。

## Runtime smoke
- Making History導入時、Dessert Airfield RWY 36/18が表示される。
- KK等未導入providerの空港が復活していない。
- ND/LAND/UI挙動がCandidate 12から変化していない。
