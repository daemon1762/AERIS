# CP3 Gate 5 Candidate 4 Native Spawn Warp Utility — Safety Hotfix 2 テストカード

## 目的
Native Spawn Warp直後の落下衝撃による爆散を防止し、MOD純正LaunchPadTransformを使った登録巡回補助を安全に成立させる。

## 変更点
- 旧: `Vessel.SetRotation / SetPosition / SetWorldVelocity`によるunpacked vessel直接移動。
- 新: KSP純正`FlightGlobals.SetVesselPosition(..., easeToSurface=true, gravityMultiplier=0.05)`へ委譲。
- live LaunchPadTransform由来の緯度・経度・ASL・forwardは維持。
- Physics Easing中の再ワープを12秒禁止。

## 実機試験
1. Sandboxで任意のKK空港を開く。
2. `WARP TO MOD NATIVE SPAWN`を1回押す。
3. 機体が穏やかに接地し、爆散・部品分離・猛烈な跳ね返りがないことを確認。
4. 12秒以内に再度押し、`STOCK PHYSICS EASING ACTIVE`で拒否されることを確認。
5. 12秒経過後、別空港へワープして再確認。
6. AIRFIELDSのCHECK HERE / A-B補正が通常どおり使用できることを確認。

## 合格条件
- RUD 0。
- ワープ後のactive vessel消失 0。
- MOD Native Spawnの水平位置・方位が妥当。
- Career / Scienceではボタン非表示。
- Terrain/NDに新規回帰なし。
