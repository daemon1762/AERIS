# AERIS v0.18.0.0 DEV CP2 — Preload Status Toolbar

## 1. 目的

Preload Terrain Map BuilderはMain Menu、Space Center、VAB、SPH、Tracking Stationなどの非Flight sceneで動作する。生成中の進捗を確認するためにFlightへ入る必要がある構成は不合理である。

本改良では、FMJと同じ考え方で単一の常駐Toolbar ownerを使用し、主要sceneのすべてからPreload状態を確認できるようにする。

## 2. Toolbar lifecycle

- `AERISBootstrap`は`KSPAddon.Startup.MainMenu, true`のpersistent owner
- `ToolbarBridge`はBootstrap GameObjectへ1個だけ追加
- static owner guardによりduplicate instanceを拒否
- `ToolbarControl.RegisterMod`は1回
- `AddToAllToolbars`は1回
- 対象sceneは`ApplicationLauncher.AppScenes.ALWAYS | TRACKSTATION`
- Stock 38×38とBlizzy 24×24の既存アイコンを再利用
- アイコンTextureにはstable nameを付与し、FMJ／FMJ監視で帰属可能にする
- Main Menu専用のIMGUI overlayボタンは作らない

## 3. Launcher再生成

KSPのscene遷移ではApplicationLauncherが破棄・再生成される場合がある。

`ToolbarBridge`は次を購読する。

- `GameEvents.onGUIApplicationLauncherReady`
- `GameEvents.onGUIApplicationLauncherDestroyed`

各イベントと、Main Menu↔KSC↔VAB↔SPH↔Tracking Stationを含む全AERIS scene boundaryで、最後に適用したToolbar表示状態のcacheを無効化する。次の通常Updateで現在sceneの実ウィンドウ状態を再同期する。

Launcherの破棄時に別のToolbarControlを作らない。ToolbarControl wrapper自身の再バインド機構を使い、AERISはownerとvisible-state contractだけを保持する。

## 4. Scene別動作

### Flight

Toolbar ON：既存`AERIS — Flight Control`を開く。

Toolbar OFF：既存Flight Control窓を閉じる。

### 非Flight

Toolbar ON：`AERIS — Preload Terrain Status`を開く。

Toolbar OFF：Preload Status窓を閉じる。

対象：

- Main Menu
- Space Center
- VAB
- SPH
- Tracking Station
- その他ToolbarControlのALWAYS対象scene

Flight窓とPreload Status窓は別のvisible stateを持つ。すべてのscene遷移時に両方を閉じ、旧sceneの窓を次sceneへ自動継承しない。

## 5. 読み取り専用Status

非Flight画面で表示するもの：

- 現在scene
- Builder mode
- idle／user active
- Builder status
- DB使用量／上限
- complete／pending Tile
- active body／LOD
- Builder queue depth
- PQS処理時間
- worker utilization
- write throughput
- compression ratio
- 天体別coverage、quality、size、priority、state

非Flight画面に置かないもの：

- BUILD
- PAUSE／RESUME／CANCEL
- VERIFY／REBUILD／DELETE
- body priority変更
- quality変更
- storage cap変更
- Builder mode変更

これらの管理操作はFlightの`SYSTEM > PRELOAD MAPS`に残す。今回の窓は状態確認専用であり、誤操作で非Flight生成計画やDBを変更しない。

## 6. 安全境界

- Toolbar／Statusは飛行制御へ書き込まない
- AP／BANKは変更しない
- LANDへ制御権限を追加しない
- 旧NAVを復活させない
- 新NAVはBLOCKEDを維持
- Toolbar専用ThreadやThreadPoolを作らない
- Status表示のために同期Disk I/OやDB全走査を行わない
- Statusは既存のimmutable Preload snapshotだけを読む

## 7. ログ

期待する主ログ：

```text
[AERIS] Toolbar owner initialized once; scenes=ALWAYS|TRACKSTATION ...
[AERIS] Toolbar launcher ready; rebind generation=...
[AERIS] Toolbar launcher destroyed; references invalidated generation=...
```

異常：

```text
[AERIS] Duplicate ToolbarBridge initialization rejected...
```

上記duplicate警告が通常scene遷移だけで出る場合はFAILとする。

## 8. 実機合格条件

- Main MenuからFlightまで同一アイコンが使用可能
- 各sceneでアイコンは1個のみ
- scene往復で増殖しない
- launcher再生成後も押せる
- ToolbarのON／OFF表示と窓の状態が一致
- 非Flightは読み取り専用Status
- Flightは既存Flight Control
- scene遷移で旧窓が勝手に開かない
- KSP.logにToolbarControl／AERIS UI例外がない
- 38×38 AERIS iconがセッション中に増殖しない

ネイティブコンパイルとKSP runtimeを通過するまで、この項目をPASSとは扱わない。
