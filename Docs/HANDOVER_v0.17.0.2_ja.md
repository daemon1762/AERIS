---
artifact: AERISFlightControl-v0.17.0.2_RunwayRegistryIdentityHotfix_Source
date: 2026-07-23
baseline: AERISFlightControl-v0.17.0.1_PerformanceRuntime_CompileHotfix_Source
status: source RC; native KSP compile and runtime runway reload pending
---

# AI向け引継ぎ — v0.17.0.2 Runway Registry Identity Hotfix

## 固定された製品状態

- APは完成済み。BANK制御を含むAP制御則は回帰保護対象。
- 旧NAVは完全削除済み。新NAVは全面新規開発中で現行版には未搭載。
- 独立LANDは観測・認証基盤であり、FlightCtrlState/AP出力を所有しない。
- LANDは認証済み進入方向だけを選択し、ARM中は滑走路、方向、データベース世代、幾何を固定する。
- v0.17.0.1のPerformance Runtime、非同期記録、アーカイブを維持する。

## 添付物から確定した障害

動画:

- 長さ152.641秒、1924 × 1111、VP8
- 起動後`STATE FAILED / DATABASE REV 0 / LAST NEVER`
- 全airfield/runway/certified approach件数が0
- 手動再読込で`DISCOVERING`へ進むが、完了時にFAILED・0件へ戻る

AERISログ／KSPログ:

- generation 1: STARTUP
- generation 2: MANUAL
- generation 3: MANUAL
- 3回とも`STAGED DATABASE INVALID: DUPLICATE AIRFIELD Kerbin / DISC_STOCK_KSP`
- 失敗前に多数の`AIRFIELD_SNAPSHOT`があり、検出・捕捉経路は動作

## 根本原因

`AERISKspFacilityProvider.Collect`がすべての独立KSP施設へ同じ`ProviderGroup = "KSP"`を設定し、`AERISAirfieldRegistry.MergeDiscovered`がStock/DLCにもグループを発見IDとして使用した。この組合せで複数施設が同じ`DISC_STOCK_KSP`となり、正しい安全検査が初回ステージ全体を拒否した。

## 修正契約

1. Stock/DLCは`ProviderSiteId`、なければUUID、最後に表示名を発見IDへ使う。
2. `ProviderGroup`による飛行場集約はKerbal KonstructsとStock Launchsites Expansionだけに限定する。
3. KSP施設Providerはリテラル`KSP`をグループ値へ使用しない。
4. 重複airfield／approach、無効なcertified geometryの拒否は維持する。
5. 診断にStable IDを出す場合は一行化する。
6. AIRFIELDS画面は`registry.Status`の全文を表示する。

## 変更ファイル

製品コード:

- `Landing/AERISAirfieldProviders.cs`
- `Landing/AERISAirfieldRegistry.cs`
- `UI/AERISWindow.cs`
- バージョン定義2ファイル

受入・配布:

- v0.17.0.2静的／build／manifest検証器
- 滑走路レジストリID回帰モデル
- v0.17.0.2受入ランナー
- README、リリースノート、本引継ぎ、実KSP試験カード

## 実KSPの次ゲート

1. `python3 Tools/run_v01702_acceptance.py`
2. `./build_ubuntu.sh "<KSP root>"`
3. KSPを完全終了して再起動
4. `SYSTEM > AIRFIELDS`で起動時再読込を待つ
5. `STATE COMPLETE`、`DATABASE REV >= 1`、`LAST`が`NEVER`以外であること
6. KSC Main RunwayとIsland Airfieldの両方向が表示されること
7. 手動再読込を2回行い、各回がatomic commitし、件数と選択が安定すること
8. ログに`DISC_STOCK_KSP`重複がないこと

## 禁止事項

- この障害を理由にAP/BANK制御則を変更しない。
- 重複検査を削除・警告化して不正データベースをコミットしない。
- KK/SLEの複数滑走路を施設ごとに分断しない。
- NAVを搭載済み、または利用可能と記載しない。
- ソース試験の合格をKSPネイティブビルド／実行合格と表現しない。
