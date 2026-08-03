# AERIS v0.18.0.0 CP3.5 Gate 3 Candidate 2 — Presentation Authority Hotfix 1 — Build Entrypoint Hotfix 2

## 発端

ユーザー環境で `build_ubuntu.sh` を実行すると、Candidate 2 / Presentation Authority Hotfix 1 / C# definite-assignment 回帰までは完走したが、`Source SHA-256 manifest verification` の

- `SOURCE_MANIFEST_SHA256.txt exists`
- `source manifest file set matches source payload`

の直後で停止し、`xbuild` に到達しなかった。

## 設計判断

通常のソースビルドと配布物完全性監査の責務を分離する。

### 通常ビルド

`build_ubuntu.sh` は以下だけを実行する。

1. KSP / xbuild / ToolbarController の前提確認
2. CP3 Frozen Visual Path Recovery 回帰
3. Presentation Authority Hotfix 1 回帰
4. Build Entrypoint Hotfix 2 回帰
5. C# definite-assignment compile regression
6. `xbuild`
7. DLL install

SOURCE/PACKAGE manifest の全ファイル SHA-256 再計算は通常ビルドから完全に除外する。

### 完全受入

配布物の完全性確認が必要な場合だけ、明示的に次を実行する。

```bash
python3 Tools/run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_acceptance.py
```

このrunnerのみが SOURCE/PACKAGE manifest の全ハッシュ検証を行う。

## 非変更範囲

このHotfixでは描画コードを変更しない。

- CP3 Frozen Visual Path
- Exact FRONT authority
- Temporal shadow mode
- overscan / temporal reprojection
- terrain / coastline / contour
- Resident Cache / Predictive Corridor / preload
- ND UI
- AUTOPILOT / LAND / PROTECT
- runway / airfield

はPresentation Authority Hotfix 1から維持する。

## 期待結果

通常ビルドではmanifest全ハッシュ工程を通らず、軽量static regression完了後に直ちに`xbuild`へ進む。
