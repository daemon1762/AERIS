# ND CP2 Absolute Geodetic Endpoint Authority Build Entrypoint Hotfix 1 実機試験カード

## 1. SHA・静的受入

- 配布SHA-256一致。
- `Tools/run_v01800_cp2_acceptance.py`が全PASS。
- build script開始後の再受入も全PASS。

## 2. native Mono/xbuild

次を確認する。

```text
[AERIS] Building ... MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1
Build succeeded.
```

`167/168 PASS`で停止しないこと。`CoreCompile`へ到達し、DLLがKSPへ配置されること。

## 3. KSP起動

起動ログのBuild identity末尾が次であること。

```text
MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1
```

## 4. 滑走路回帰

既存`UserRunwayCalibrations.cfg`を削除せず起動する。

- Kolaの手動物理滑走路が1項目で表示される。
- RWY 20 / RWY 02を維持する。
- ND中心線がA端・中央・B端で実滑走路と一致する。
- 再測量、場面遷移、再起動後も維持する。

## 5. CP2判定

native build成功だけではCP2を閉じない。KSP実機、空港UI、ND、Auto Preload Progressionの残りの最終条件を確認する。
