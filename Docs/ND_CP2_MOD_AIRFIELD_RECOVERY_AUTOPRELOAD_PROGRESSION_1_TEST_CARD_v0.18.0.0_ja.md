# 実機試験カード — MOD Airfield Recovery + Auto Preload Progression 1

## 1. ビルド

- native Mono/xbuildが成功すること。
- 画面／ログのBuild表示に`MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1`が含まれること。

## 2. MOD空港再認証

1. KSP起動後、Airfield reload完了まで待つ。
2. `[AIRFIELD_RELOAD] atomic commit`のCERTIFIED数を記録する。
3. KolaIsland、Dundard's Edge、Mahi、Uberdam、Kojave Sands、Sandy Islandを確認する。
4. `[RUNWAY_AXIS]`で`axisRealigned=True/False`、`registeredHeadingAfterDeg`、`headingCorrectionDeg`を確認する。
5. `[RUNWAY_PLACEMENT] absolutePlacementValid=True`を確認する。
6. 実滑走路とND線の角度・位置が一致することを録画する。

合格条件：

- 前版の`CERTIFIED 3 RWY`から有意に回復する。
- 旧認証済みだった正常なMOD滑走路が過剰拒否されない。
- 12度超の不明確な軸は安全拒否を維持する。
- バニラ滑走路に回帰がない。

## 3. Preload自動天体移行

1. `AGGRESSIVE IDLE`、任意の速度設定で非Flight画面を放置する。
2. KerbinのFar完成後も監視を続ける。
3. ログの`[PRELOAD_AUTO] event=COMPLETE`を確認する。
4. Builder bodyがMun、Minmus、その他固体天体へ自動遷移することを確認する。
5. KSP再起動後も完成済み天体を延々再走査せず、次の未完成天体へ進むことを確認する。

## 4. 自動高精細化

全固体天体のFar被覆完了後、登録地点を持つHigh／Pinned天体について、

`[PRELOAD_AUTO] event=PROMOTE; from=FAR_GLOBAL; to=LAND_SITES; routeGlobal=False`

が出ることを確認する。

- 滑走路周辺Local／Land Tileが増える。
- 全球Route生成が暗黙開始しない。
- 手動品質指定した天体は自動昇格しない。
- UI操作再開時に負荷が即座に後退する。

## 5. 提出物

- `AERISFlightControl`フォルダZIP
- Airfield reload完了からのログ
- PreloadがKerbinから他天体へ移るまでのログ
- KolaIsland等の実滑走路とNDを同時に確認できる動画またはスクリーンショット
