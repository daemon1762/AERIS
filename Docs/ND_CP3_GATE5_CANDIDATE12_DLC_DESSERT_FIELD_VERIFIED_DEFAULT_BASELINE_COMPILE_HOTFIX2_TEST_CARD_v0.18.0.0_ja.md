# Candidate 12 Compile Hotfix 2 実機テストカード

## 目的
Compile Hotfix 1後も、`build_ubuntu.sh` が `AERISBuildVersion.generated.cs` を再生成した直後だけ Candidate 11 継承selftestが `FAIL: Candidate 11 lineage identity retained in successor build` で停止する非決定性を修正する。

## 原因
配布時の generated.cs には Candidate 11 の装飾付き履歴コメント（`—` / `A/B`）があったが、build script の再生成後にはそのコメントが消えた。一方、Display lineage の正規化文字列 `CANDIDATE 11 DLC PLACEHOLDER MANUAL A B HOTFIX 1` は生成前後とも存在していた。

## ビルド確認
1. `./build_ubuntu.sh <KSP path>` を実行する。
2. Compile Hotfix 2専用selftestがPASSすること。
3. Candidate 12 baseline 58/58がPASSすること。
4. Candidate 11継承selftestが `Candidate 11 lineage identity retained in successor build` をPASSすること。
5. Gate 5全selftest完走後に `xbuild` が実行され、`AERISFlightControl.dll` が生成・GameDataへinstallされること。
6. ゲーム内タブ表記が `CANDIDATE 12 — DLC DESSERT FIELD-VERIFIED DEFAULT BASELINE — COMPILE HOTFIX 2` であること。

## Runtime smoke
- Making History導入時、Dessert Airfield RWY 36/18が通常滑走路として表示される。
- Making History未導入時はDessertを表示しない。
- 未導入の外部空港provider由来空港はAIRFIELDS/LAND/NDへ出ない。
- ND/LAND/UI/AP/FBW/PROTECT/Terrainの挙動がCandidate 12から変化していない。
