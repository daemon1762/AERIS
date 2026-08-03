# AERIS v0.18.0.0 CP2.5 Candidate 4 Full Boost Backpressure Compile Hotfix 1

## 修正
`build_ubuntu.sh` が生成する `Cp2FrozenBaselineDisplay` にCP2.5 Candidate 4の版名が混入していたため、旧CP2静的検査が167/168で停止した。

- `BASELINE_DISPLAY`を凍結CP2原本の完全一致文字列へ復元。
- `verify_v01800_cp2_static.py`は現在の`Display`ではなく専用`Cp2FrozenBaselineDisplay`を検査。
- Backpressure、Preload、AA/AP/PROTECT/LAND等の実行コードは変更しない。

## 期待結果
`verify_v01800_cp2_static.py` が168/168 PASSし、その後xbuildへ進む。
