# v0.17.0.2 ソース監査・位置表

基準ルート:

```text
AERISFlightControl-v0.17.0.2_RunwayRegistryIdentityHotfix_Source
```

| 監査事項 | ファイル | 確認内容 |
|---|---|---|
| Stock/DLC基礎滑走路 | `GameData/AERISFlightControl/Airfields/Defaults/01_Stock_DLC_Foundation.cfg` | KSC/Islandの両方向が3.0°、FoundationValidated。DessertはDiscoveryOnly、WoomerangはLaunchPad |
| MODカタログ41件 | `GameData/AERISFlightControl/Airfields/Defaults/02_Current_Mod_Runway_Survey_Catalog.cfg` | StaticBounds 34、ManualRequired 5、PairedThresholds 2 |
| 失敗コード | `Source/AERISFlightControl/Landing/AERISAirfieldModels.cs` | 30種の方向/測量失敗コード |
| カタログ読込み | `Source/AERISFlightControl/Landing/AERISRunwaySurveyCatalog.cs` | MethodとPairKeyを読み込み |
| 方式の未強制 | `Source/AERISFlightControl/Landing/*.cs` | Methodの実行分岐がなく、PairKeyは読込み検証以外で使用されない |
| 進入角固定 | `Source/AERISFlightControl/Landing/AERISOperationalRunwayResolver.cs` | `BuildDirection`で3.0°、TCH 15 mを固定 |
| 現行地形検査 | 同上 `ValidateApproach` | 中心線16点、250～8000 m、最低10 m |
| 粗障害フラグ | `Source/AERISFlightControl/Landing/AERISRunwaySnapshotBuilder.cs` | `ApproachAAvailable/BAvailable`をtrueで生成 |
| LAND観測境界 | `Source/AERISFlightControl/Landing/AERISLandingFoundation.cs` | 滑走路/LOC/GS観測値を算出。完成済みAPの操舵則とは分離 |
| AIRFIELDS表示 | `Source/AERISFlightControl/UI/AERISWindow.cs` | state、failure code、detail、reload UI |
| IDホットフィックス回帰 | `Tools/selftest_v01702_runway_registry_identity.py` | Stock/DLCの一意ID、KK/SLEグループ化、旧ID禁止 |
| 全受入入口 | `Tools/run_v01702_acceptance.py` | 12スクリプトの受入を実行 |

## 監査時の検索結果要約

- `definition.Method`を方式別の測量分岐に使う製品コードは確認できない。
- `PairKey`はモデル、カタログ読込み、入力検証に存在するが、実行時の閾値結合処理に使用されていない。
- Runtime surveyから作られる両方向は、同じ3.0°と15 m TCHで初期化される。
- `ValidateApproach`の`firstDirection`引数は、監査対象範囲では方向固有処理に使われていない。
- 人工障害物の実三次元走査結果を`ApproachA/BAvailable`へ接続する処理は確認できない。

## 再監査時の最低条件

修正後は次のテストを追加する。

1. `ManualRequired`がワーカーへ入らず`AUTO INHIBIT`になる。
2. `PairedThresholds`の同一PairKeyが一つの物理滑走路になる。
3. 片方向だけ地形遮蔽したモデルで、反対方向だけ合格する。
4. 3.0°不合格、3.7°合格の人工地形で3.7°を選ぶ。
5. 6.0°まで不合格なら方向を閉鎖する。
6. 中心線外の障害物が回廊内なら失敗する。
7. サンプル点間だけにある障害物を見逃さない。
8. 復行回廊不合格を進入角変更で隠さない。
9. 機体の安定降下率を超える場合は機体不適格になる。
10. AP/BANKディレクトリが基準版とバイト一致する。

