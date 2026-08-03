# AERIS14 引き継ぎ — v0.18.0.0 DEV CP2 Alignment Diagnostic Hotfix 1

## 再開位置

CP2 Field Render Consistency Hotfix 1の実機動画・ログ解析後、地形位置一致を最優先とするAlignment Diagnostic Hotfix 1を作成した。次の作業はユーザー環境でのnative Monoビルドと実機試験である。

## 現行原本

`AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip`

## この版で行ったこと

1. GPU地形RenderTextureのY方向を`DIRECT / FLIP`明示選択へ変更
2. 既定を`DIRECT`へ変更
3. `[ND/TERRAIN_ALIGN]`診断ログ追加
4. ND上へ`CLR SEL`追加
5. 明示的な空港／滑走路選択解除を永続化
6. LAND平面の画面外早期returnを線分クリッピングへ置換
7. GS中心線、上下漏斗境界、遠端capを安定表示
8. fallback込みcoverageと要求品質coverageを分離

## 解析で確定したこと

- 問題は単なる地図回転ではなく、地形レイヤーが自機・滑走路と異なる地理位置に見えること
- LAND平面漏斗消失は、滑走路端または漏斗端の画面外判定で描画全体をreturnしていたこと
- 空港選択解除は既存ND UIに存在しなかったこと
- fallback coverageがsampling中でも完成度100%を作り得たこと

## 実機で確定すべきこと

- `TERR Y DIRECT`で島・滑走路・自機位置が一致するか
- `TERR Y FLIP`との比較
- `[ND/TERRAIN_ALIGN] deltaPx`が正しい方向で0付近になるか
- LOC／GS漏斗がrange・orientation変更後も維持されるか
- `CLR SEL`がDISARM・解除・再起動後保持まで成立するか
- `requestedCoverage`がsampling中に偽の1.000を報告しないか

## 凍結・安全境界

- BANK、HDG、PITCH、V/S、Ground Stability ProtectionはHotfix1原本とバイト一致
- AP操縦ロジックへ変更なし
- LANDへ操縦権限を追加していない
- 旧NAV不在、新NAVはLAND完成までBLOCKED

## 未修正・次議論

次は別Hotfix候補として保持する。

1. steady時の約45秒周期`stale_cancelled`
2. touchdown後の誤liftoff判定
3. Ground ARM APの一時解放
4. Airfield Snapshot約104ms slice
5. Tile境界、LOD pop、最終描画品質

地形alignment試験前にこれらを同時修正しない。原因切り分け性を優先する。

## 静的受入

`Tools/run_v01800_cp2_acceptance.py`を使用する。作成環境ではnative C# compileとKSP実行は未実施。

## 参照

- `Docs/CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX_1_v0.18.0.0_ja.md`
- `Docs/ND_CP2_ALIGNMENT_DIAGNOSTIC_TEST_CARD_v0.18.0.0_ja.md`
- `Evidence/AERIS14_VIDEO_LOG_REVIEW_2026-07-25.txt`
- `Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX1.txt`
