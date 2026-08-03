# ND CP3.5 Gate 3 Candidate 2 — Presentation Authority Hotfix 1 — Build Entrypoint Hotfix 2 実機試験カード

## Gate A — Build Entrypoint

通常ビルドを実行する。

### Ubuntuデスクトップ

```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

### Ubuntuノート

```bash
cd ~/Downloads
rm -rf AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source
unzip -o AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source.zip
cd AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate3_CP3FrozenVisualPathRecovery_BoundedExactRefinement_Candidate2_PresentationAuthorityHotfix1_BuildEntrypointHotfix2_Source
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

### PASS条件

- `[... PREBUILD] 4/4 lightweight suites PASS` が出る。
- `SOURCE_MANIFEST_SHA256.txt` / `MANIFEST_SHA256.txt` の全ハッシュ検証へ入らない。
- 直後に`xbuild`へ進む。
- `Build succeeded.`
- `[AERIS] Installed ...AERISFlightControl.dll`

## Gate B — KSP起動

- KSP 1.12.5が起動する。
- AERISがロードされる。
- Build Entrypoint Hotfix 2のidentityを確認する。

## Gate C — Presentation Authority Hotfix 1再開

KSC付近で以下を確認する。

1. ND ON
2. TERRAIN ON
3. 20 km
4. 40 km
5. 160 km

一度表示された地形が青一色へ戻らないこと。

## 完全manifest受入（通常ビルドとは別）

必要時のみ実行する。

```bash
python3 Tools/run_v01800_cp35_gate3_candidate2_presentation_authority_build_entrypoint_hotfix2_acceptance.py
```
