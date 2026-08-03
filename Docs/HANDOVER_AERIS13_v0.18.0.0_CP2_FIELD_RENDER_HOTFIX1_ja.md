# AERIS13準拠 引継ぎ書 — v0.18.0.0 CP2 Field Render Hotfix 1

## 1. 新しいチャットが最初に理解すること

本パッケージは`AERIS Flight Control v0.18.0.0 DEV CP2 FIELD RENDER CONSISTENCY HOTFIX 1`である。

- AP：完成済み、凍結。
- BANK：提示画像の20°捕捉をgolden基準として凍結。
- LAND：独立開発中だが、現状は観測・Registry・認証・表示基盤のみ。操縦しない。
- NAV：旧NAV削除済み。新NAVは未搭載・BLOCKED。
- 現在の作業：改良型ND CP2 Terrain／GPU／Cacheの実機合格。
- 次の大工程：CP2合格後のCP3 Adaptive Approach／3D Corridor。

`Docs/AERIS13_Baseline/`の全9文書を基準として読むこと。特に滑走路41件の個別対応、失敗コード、可変角度方針を省略してはならない。

## 2. 今回の不具合

修正前ログでは、Range／mode切替中に次の矛盾があった。

- GPU coverageが0へ低下してもviewport coverageは1.0000。
- sampling未完了でもcoverageが完成扱い。
- stale／obsolete cancellationが1353／1341まで増加。
- partial resultが完成Previewを消し、旧style entryをfallbackに使わない。

原因はGPU failureやDB CRCではなく、Tile lifecycleと描画合成の整合性だった。

## 3. 修正の要点

- Tileへ`SamplingComplete`を追加。
- Preview／Finalと、途中／完了を別概念にした。
- active Block workへ最新generationを統合。
- commit時はPipelineが確定したrequestを使う。
- mode変更でheight Tile generationを無効化しない。
- range/style切替中は完成済み旧entryを下敷きにする。
- partial current entryを上へ重ね、完成前にfallbackを削除しない。
- coverageはvalid triangleとTile qualityで計算。
- AUTO backlogを評価窓内でlatch。
- range／mode／AUTO遷移をログ化。

詳細は次を読む。

- `Docs/CP2_FIELD_RENDER_CONSISTENCY_HOTFIX_1_v0.18.0.0_ja.md`
- `Docs/RUNTIME_EVIDENCE_ANALYSIS_2026-07-24_ja.md`
- `Docs/CODE_AUDIT_v0.18.0.0_CP2_FIELD_RENDER_HOTFIX1_ja.md`

## 4. 滑走路Registryの更新

今回のログではstartup commitが一回成功した。

```text
databaseRevision=1
REGISTERED 43 RWY / 86 APP
CERTIFIED 13 RWY / 24 APP
FAILED 32 RWY / 62 APP
```

これはAERIS13時点より前進した証拠である。ただし次を断定しない。

- 手動RELOAD二回の安定性。
- 13滑走路の最終安全認証。
- 32失敗滑走路の方向別解決。
- 3D corridor、人工障害物、missed approach、機体適合性。

AERIS13方針どおり、3.0°を開始値とし、方向ごとに3.0～6.0°を0.1°刻みで探索して最小の安全角を選ぶ。ただし実装はCP3であり、今回のHotfixに入っていない。

## 5. 絶対条件

- `AERISBankDirector.cs`を変更しない。
- AP／BANK tuningをCP2 Terrain修正へ混ぜない。
- LANDへFlightCtrlState、throttle、steeringを書かせない。
- legacy NAVを復活させない。
- CP2合格前にnew NAVやCP3を開始しない。
- Terrain専用ThreadPoolを追加しない。
- workerからPQS／Unity objectへ触れない。
- Main ThreadでDisk read／decode／worker待機をしない。
- partial Tileを100% coverageと報告しない。
- style／range切替だけで有効なheight Tileを捨てない。
- 修正前ログを修正後PASSの証拠に使わない。

## 6. 再開手順

1. ソースZIPとSHA-256を確認する。
2. `python3 Tools/run_v01800_cp2_acceptance.py`を実行する。
3. Ubuntuで`build_ubuntu.sh`を実行する。
4. KSPログのversion labelを確認する。
5. `Docs/ND_CP2_PRELOAD_TERRAIN_TEST_CARD_v0.18.0.0_ja.md`を順番どおり実施する。
6. 特にRange、mode、Preview→Final、AUTO、steady cancellationを動画とPerformance CSVで確認する。
7. PASSの場合だけロードマップGate 3–5を閉じる。
8. FAILの場合は、画面時刻と`[ND/TERRAIN]`ログを同期し、CP2内で修正する。

## 7. 現時点での証明範囲

証明済み：

- 添付ZIP整合性。
- 修正前runtime不具合のログ再構成。
- 原因とソース経路の対応。
- 専用静的回帰。
- AP／BANK／LAND／NAV境界の差分監査。

未証明：

- 修正後KSP build。
- 修正後Unity GPU実画面。
- 実PQS下の収束。
- 長時間boundedness。
- CP2合格。

## 8. 現行ロードマップ

`Docs/ROADMAP_CURRENT_AERIS13_v0.18.0.0_ja.md`を正とする。

順序は次で固定する。

```text
CP2 Field Retest
→ CP3 Approach Registry / Adaptive Glide / 3D Corridor
→ CP4 LAND Display / Integrated Acceptance
→ Independent LAND Completion
→ New NAV Rebuild
```

