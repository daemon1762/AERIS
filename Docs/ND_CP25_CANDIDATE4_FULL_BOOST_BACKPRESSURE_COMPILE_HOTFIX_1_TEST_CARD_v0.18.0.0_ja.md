# 実機テストカード — Compile Hotfix 1

1. ZIPのSHA-256を確認。
2. `build_ubuntu.sh`を実行。
3. `verify_v01800_cp2_static.py`が168/168 PASSすること。
4. xbuildが0 errorで完了しDLLがKSPへ導入されること。
5. KSP起動後、既存のFULL BOOST Backpressure試験を実施する。
