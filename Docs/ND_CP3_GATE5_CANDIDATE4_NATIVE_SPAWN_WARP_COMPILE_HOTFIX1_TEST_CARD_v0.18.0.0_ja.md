# CP3 Gate 5 Candidate 4 Native Spawn Warp Utility — Compile Hotfix 1 テストカード

## 目的
KSP 1.12.5 / Mono xbuildでNative Spawn Warpをコンパイルし、MOD純正スポーン地点へ直接移動できることを確認する。

## コンパイル
`Orbit.Clone()` / `Vessel.SetOrbit()` を使用しない。`Vessel.SetPosition` / `SetRotation` / `SetWorldVelocity` を使用する。

## 実機
1. Sandbox Flightを開始する。
2. SYSTEM > AIRFIELDSを開く。
3. MOD providerが現在存在する登録済み物理滑走路で `WARP TO MOD NATIVE SPAWN` を押す。
4. MOD純正スポーン位置・向きへ移動することを確認する。
5. Airport/RWY選択、認証、A/B補正、LAND/AP状態がワープによって変更されないことを確認する。
6. Career / ScienceではワープUIが表示されないことを確認する。
