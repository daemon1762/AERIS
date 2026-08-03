# ND CP2 Generic Runway Placement Final Candidate 3 Compile Hotfix 1 Test Card

対象ZIP：

`AERISFlightControl-v0.18.0.0_DEV_CP2_GenericRunwayPlacementVerification_FinalCandidate3_CompileHotfix1_Source.zip`

## A. ビルド

1. SHA-256一致。
2. クリーン展開。
3. `Tools/run_v01800_cp2_acceptance.py`全PASS。
4. native Mono/xbuildでCS0165が再発しない。
5. Build identity末尾が`FINAL CANDIDATE 3 COMPILE HOTFIX 1`。

## B. 位置ずれ判定回帰

1. Kolaは未校正時`UserCalibrationRequired`。
2. `CHECK HERE`のPASS／INCONCLUSIVE／MISMATCHを確認。
3. MISMATCH時は永続隔離され、再起動後も非CERT。
4. `MARK A/B`後にUserCalibratedとなり、実滑走路へ一致。
5. Kola以外の最低5空港で同じ汎用判定を確認。

## C. Auto Preload

1. 複数天体の`[PRELOAD_AUTO] COMPLETE`。
2. 全固体天体Far完了後の`[PRELOAD_AUTO] PROMOTE`。
3. 再起動継承。

## D. CP2 CLOSE条件

native build、実機滑走路、Auto Preload、再起動継承、例外なしがすべてPASSした場合のみCP2 CLOSE候補とする。
