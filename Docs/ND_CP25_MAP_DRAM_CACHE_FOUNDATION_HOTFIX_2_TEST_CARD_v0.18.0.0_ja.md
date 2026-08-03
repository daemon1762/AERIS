# CP2.5 Gate 4 — Map DRAM Cache Foundation Hotfix 2
## KSP実機試験カード

## 提出物

- Mono/xbuild端末出力
- `AERISFlightControl.log`
- `KSP.log`
- `SYSTEM > DIAGNOSTICS`のMap DRAM欄が読める動画またはスクリーンショット

## A. 起動・guard接続

1. KSPを完全終了した状態から起動する。
2. Flightへ入り、Airfield Registry初期処理とTerrain manifest loadを完了させる。
3. `SYSTEM > DIAGNOSTICS`を開く。

期待値：

- `CP2.5 MAP DRAM CACHE — METADATA ONLY`
- `STATE READY / DRAM-ONLY LOOKUP`
- `AIRFIELD NORMAL READ  DRAM SNAPSHOT + ID INDEX — ACTIVE`
- `SSD GUARD OBSERVED`が0より大きい
- `ALLOWED STARTUP/MAINT`が0より大きい
- `SYNC SSD 0 — PASS`
- AIRFIELD／RWY／ILS-DIRがRegistryデータ存在環境では0より大きい

`SSD GUARD OBSERVED > 0`かつ`SYNC SSD 0`であることが、監視が未接続の固定0ではない確認になる。

## B. Airfield通常読取り

1. AIRFIELDS一覧を開閉する。
2. 複数Airfieldを選択する。
3. RWY方向を切り替える。
4. CLEAR後に再選択する。
5. NDのTRK UP／PLAN／RANGEを操作する。
6. LANDをARM／DISARMする必要はないが、選択runwayがNDへ正しく反映されることを確認する。

期待値：

- Airfield lookup hitが明確に増える
- 選択Airfield／Runway／Direction表示が一致する
- ND Airport／Runway symbolが消失・重複しない
- UIフリーズや同期停止がない
- `SYNC SSD 0 — PASS`を維持

## C. Terrain通常lookupとPreload

1. Terrain表示をAUTO／TOPO／RELで切り替える。
2. ND RANGEを変更する。
3. Preload Builderを継続し、新しいtile commitを待つ。

期待値：

- Terrain lookup hit／missが増える
- Terrain revision、tile／chunk件数がcommitへ追従する
- `SSD GUARD OBSERVED`と`ALLOWED STARTUP/MAINT`はmaintenance I/Oに応じて増えてよい
- `SYNC SSD`は0のまま
- `payloadBytes=0; normalLookup=DRAM_ONLY`

## D. 終了summary

1. Main Menuへ戻る。
2. KSPを正常終了する。
3. `AERISFlightControl.log`末尾を確認する。

期待ログ：

```text
[CP2.5/MAP_DRAM_SUMMARY]
result=PASS
synchronousSSD=0
```

Airfield lookup hitは0より大きく、guardedSSD／allowedSSDも0より大きいこと。

## E. 再起動

KSPを再起動しAを再実施する。

期待値：

- committed manifestからTerrain indexを復元
- Airfield atomic commit後にDRAM Airfield件数が復元
- `SYNC SSD 0 — PASS`
- AERIS由来ERROR／FATAL／Exceptionなし

## 合格条件

- A〜E成立
- Airfield normal readがshared Map DRAM snapshot／ID indexを実際に使用
- guard観測総数が0より大きく、normal lookup違反は0
- shutdown summaryが一度だけPASSを記録
- Gate 1〜3、CP1／CP2、AP／AA／PROTECT、Preloadに退行なし
