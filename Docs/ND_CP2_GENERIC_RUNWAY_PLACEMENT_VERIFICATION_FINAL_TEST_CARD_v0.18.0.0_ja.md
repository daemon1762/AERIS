# ND CP2 Generic Runway Placement Verification Final Test Card

対象ZIP：

`AERISFlightControl-v0.18.0.0_DEV_CP2_GenericRunwayPlacementVerification_FinalCandidate3_CompileHotfix1_Source.zip`

## A. ビルド前

1. ZIPのSHA-256一致。
2. クリーン展開。
3. `Tools/run_v01800_cp2_acceptance.py`が全PASS。
4. native Mono/xbuild成功。
5. Build identityが次と一致。

`AERIS Flight Control v0.18.0.0 DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1 MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1 AXIS REFERENCE HOTFIX 2 RUNWAY WITNESS ANCHOR SCAN CALIBRATION HOTFIX 3 GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3 COMPILE HOTFIX 1`

## B. 旧一時表示の撤去

1. 設定画面に滑走路候補表示トグルがない。
2. NDに候補専用の黄色線・候補文字列が出ない。
3. ログに候補専用の定期列挙が出ない。
4. 新規cacheに候補専用詳細フィールドが保存されない。
5. Provisional空港はAIRFIELDSで非選択状態を確認できても、ND・LAND選択へ出ない。

## C. Kola Island手動校正

1. 初回は既存`AirfieldCertificationCache.cfg`を手動削除せず起動する。
2. Kola Islandが`UserCalibrationRequired`となり、未校正のままCERTされないこと。
3. 飛行中または5m/s超では`MARK A/B`が拒否されること。
4. 実滑走路の一端へ機体を停止し`MARK A`。
5. 反対端へ移動して停止し`MARK B`。
6. 二点間が80m以上で、再測量後に`UserCalibrated`となること。
7. NDの滑走路線が実滑走路へ一致すること。
8. 再起動後も校正が維持されること。

## D. 他空港の汎用位置ずれ判定

Kola以外の代表空港を最低5か所確認する。推奨：Dundard's Edge、Mahi、Uberdam、Kojave Sands、Sandy Island、Area 52、Cove。

各空港で：

1. 目視できる実滑走路中心線上へ機体を完全停止。
2. AIRFIELDSで対象滑走路・方向を開く。
3. `CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY`を押す。
4. 正しく一致する場合は`PLACEMENT CHECK PASS`。
5. 長手範囲外、高度差過大、移動中は`INCONCLUSIVE`となり、隔離されない。
6. 実滑走路上なのに横回廊外なら`PLACEMENT MISMATCH DETECTED — RUNWAY QUARANTINED`。
7. mismatch後は即時再測量され、二点校正完了までCERT・ND選択・LAND ARM不可。
8. `UserRunwayCalibrations.cfg`へ観測値が保存され、再起動後も隔離が残る。
9. `CLEAR`後は観測隔離・手動端点が削除され、通常再測量へ戻る。

誤操作防止：飛行中や滑走路外でCHECK HEREを押しても確定判定にしない。

## E. Kramax / Anchor / Provisional安全境界

1. Kramaxプラン一致はPlan Witness証拠として使用可能。
2. 大きなPlan conflictはCERTされない。
3. プランなし空港はAnchor Surface Scanで救済可能。
4. 証拠不足はProvisionalのまま、ND選択・LAND ARM不可。
5. Kramaxを削除してもUser Calibrationは維持される。

## F. Auto Preload Progression

1. 非Flight画面でAGGRESSIVE IDLE。
2. Kerbin完了後にMun、Minmus、その他固体天体へ自動遷移。
3. 各対象で`[PRELOAD_AUTO] COMPLETE`。
4. 全固体天体Far完了後、登録地点の高精細化で`[PRELOAD_AUTO] PROMOTE`。
5. KSP再起動後に完成状態を継承。
6. `tiles_pending`が永久停滞しない。

## G. CLOSE判定

以下がすべて成立した場合のみCP2 CLOSE候補。

- static acceptance全PASS
- native Mono/xbuild PASS
- Kola手動二点校正PASS
- 他空港の汎用CHECK HEREがPASSまたは安全隔離
- 誤認滑走路をCERTしない
- 一時表示経路が実機でも消失
- Auto Preload COMPLETE / PROMOTE / restart継承PASS
- 例外、ERROR、操縦権限退行なし
