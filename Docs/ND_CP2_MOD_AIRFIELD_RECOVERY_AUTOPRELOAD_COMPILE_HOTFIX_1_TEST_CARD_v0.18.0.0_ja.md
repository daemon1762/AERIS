# ND CP2 Mod Airfield Recovery / Auto Preload Compile Hotfix 1 実機試験カード

1. `run_v01800_cp2_acceptance.py`が全PASSすること。
2. `build_ubuntu.sh`がCS0103なしで完走すること。
3. KSP起動後、空港再測量が開始・完了すること。
4. 非Flight画面を放置し、Preloadが停止せず進むこと。
5. 天体または品質段階の完了時、ログへ`[PRELOAD_AUTO] event=COMPLETE`が出ること。
6. 次天体または登録地点高精細化へ移行時、`[PRELOAD_AUTO] event=PROMOTE`が出ること。
7. MOD空港のCERT数、Preload進捗、UI応答に前版からの新規退行がないこと。

ビルドエラー時はソースを手動修正せず、端末出力を提出する。
