# AERIS v0.18.0.0 CP2.5 Gate 4
## Map DRAM Cache Foundation Hotfix 2

Hotfix 1の実機・ソース監査で確認された次の不足を閉じる。

1. Airfield／Runway／ILS-Direction snapshotはpublishされていたが、通常UI・選択・ND・Terrain runway要求が従来Registry listを読んでいた。
2. `SYNC SSD 0`カウンターへ実際の同期I/O監視経路が接続されていなかった。
3. セッション終了時のMap DRAM総括ログがなかった。

## Airfield通常読取りのDRAM実配線

`AERISAirfieldRegistry.Airfields`は、shared `AERISMapDramCache`が存在するruntimeではimmutable snapshotのread-only listだけを返す。次の既存consumerはこのpropertyを通じて同じsnapshotを読む。

- AIRFIELDS UI
- ND Airport／Runway symbol source
- runway selection／CLEAR／selection restore
- LAND Foundationの選択runway解決
- Terrain LAND request key生成

選択中のAirport／Runway／ILS-Directionはstable ID dictionaryでも再解決する。

- `TryGetMapAirfield`
- `TryGetMapRunway`
- `TryGetMapDirection`

snapshot順序はpublish元のcommitted registry順序を維持するため、既存のselection index契約を変更しない。Airfield atomic commitでは、committed revisionを確定してsnapshotをpublishした後にselectionを復元する。

## 同期SSD runtime guard

Map DRAM対象の同期filesystem operationは`AERISMapDramDiskGuard.BeforeSynchronousDisk`を通す。

通常lookup中はthread-local scopeが有効になり、そのscopeから同期I/Oへ到達した場合だけ`SynchronousDiskLookups`を加算し、次をERROR記録する。

```text
[CP2.5/MAP_DRAM_VIOLATION]
```

起動・manifest load・Airfield discovery・Preload commit・repairなど、明示されたstartup／maintenance I/Oは許可される。guardは次を別々に集計する。

- `GuardedSynchronousDiskOperations`：guardが観測した同期I/O総数
- `AllowedSynchronousDiskOperations`：startup／maintenanceとして許可された数
- `SynchronousDiskLookups`：normal lookupから到達した違反数

これにより`SYNC SSD 0`が固定値ではなく、実際に起動I/Oを観測したguardの違反結果になる。

## 終了時総括

AERIS shutdown前に次を一度出力する。

```text
[CP2.5/MAP_DRAM_SUMMARY]
```

内容：

- Map／Airfield／Terrain revision
- Airfield／Runway／Direction／Terrain tile／chunk件数
- guarded／allowed／violation SSD件数
- Airfield／Terrain lookup hit／miss
- 最終PASS／VIOLATION

## CP3との境界

Hotfix 2でもMap DRAM Cacheはmetadata onlyである。次を保持しない。

- 圧縮Terrain payload
- decode済み高度配列
- Current-body resident tile
- render-ready mesh
- GPU Mesh／Material／RenderTexture

地形本体RAM常駐と速度予測型Forward Corridor PreloadはCP3で実装する。

## Track B境界

滑走路A/B絶対測地デフォルト、座標、認証内容、Provider補正値は変更しない。
