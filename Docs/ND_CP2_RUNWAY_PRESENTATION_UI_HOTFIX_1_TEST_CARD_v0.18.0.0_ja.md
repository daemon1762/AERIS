# CP2 Runway Presentation UI Hotfix 1 実機試験カード

対象ZIP：

```text
AERISFlightControl-v0.18.0.0_DEV_CP2_RunwayPresentationUIHotfix1_PreloadFastPath1_Source.zip
```

## 1. Native build

静的受入が全PASSし、Mono C# buildが成功すること。

## 2. 範囲外滑走路ポインター

KolaIslandなど現在Rangeより遠い滑走路を選択する。

```text
滑走路距離 30km超
ND Range 20km
TRACK UP
左右へ90度以上旋回
```

合格条件：

- 範囲外滑走路を地図内部の小さな滑走路線として描かない。
- 自機位置を起点とした外周矢印だけを表示する。
- 矢印は地図外周へ固定され、海上の任意位置に静止しない。
- ラベルへ滑走路名と現在距離を表示する。
- Rangeを滑走路距離以上へ広げると、外周矢印から実滑走路線へ切り替わる。

## 3. LAND下段UI

滑走路をクリックしてプレビューパネルを開く。

合格条件：

```text
ARM | CENTER | APP 09 | SELECT | CLEAR
```

- 進入方向ボタンがSELECTの直左にある。
- CLEARがSELECTの直右にある。
- APPボタンで同一滑走路の両方向だけを切り替える。
- SELECTで表示中の進入方向を確定する。
- CLEARでLAND DISARMと空港・滑走路選択解除を同時実行する。
- 小さいNDでは短縮表示になり、ボタンや文字がパネル外へはみ出さない。

## 4. Map Lock回帰

```text
20 → 40 → 80 → 160 → 80 → 40km
TRACK UPで90度以上旋回
Preview → Final置換
```

- 地図範囲内の滑走路は地形上の同じ位置へ固定され続ける。
- `runwayMapLockErrorPx <= 1.000`。
- 地形Commit拒否が定常増加しない。

## 5. Preload回帰

- BALANCED／FAST／MAXIMUMが選択可能。
- 放置時にFAST／MAXIMUMが加速する。
- 入力再開時に即時後退する。
- Queue、RAM、Disk writerが無制限増加しない。
