# CP2.5 Gate 4 — Map DRAM Cache Foundation Hotfix 1
## KSP実機試験カード

## 提出物

- ビルド端末出力
- `AERISFlightControl.log`
- `KSP.log`
- `SYSTEM > DIAGNOSTICS`のMap DRAM欄が読める動画またはスクリーンショット

## A. 起動・初期publish

1. KSPを完全終了した状態から起動する。
2. Main MenuでAERISが正常ロードされることを確認する。
3. Flightへ入り、Airfield Registryの初期処理が完了するまで待つ。
4. `SYSTEM > DIAGNOSTICS`を開く。

期待値：

- `CP2.5 MAP DRAM CACHE — METADATA ONLY`
- `STATE READY / DRAM-ONLY LOOKUP`
- Map revisionが0より大きい
- Terrain source revisionとTerrain index件数が表示される
- Airfield処理完了後、該当環境ではAIRFIELD／RWY／ILS-DIRが0より大きい
- `SYNC SSD 0 — PASS`

## B. 通常検索

1. NDを表示する。
2. AIRFIELDSを開閉する。
3. 滑走路を選択・CLEARする。
4. NDのTRK UP、PLAN、RANGEを操作する。
5. Terrain表示をAUTO／TOPO／RELで切り替える。

期待値：

- AERIS UIにフリーズや長い同期停止がない
- Map DRAM lookup hit/missが必要に応じて増える
- `SYNC SSD`は0のまま
- ND、FDI、AP、AA、PROTECTに退行がない

## C. Preload更新

1. `SYSTEM > PRELOAD MAPS`で対象天体の処理を継続させる。
2. 新しいtileがcommitされるまで待つ。
3. DIAGNOSTICSのTerrain revision／tile／chunk件数を観察する。

期待値：

- Preload Builderは停止しない
- commit後にTerrain source revisionまたはMap revisionが増える
- tile／chunk件数が新しいmanifest内容へ追従する
- ログに`[CP2.5/MAP_DRAM] domain=TERRAIN_INDEX`が記録される
- `payloadBytes=0; normalLookup=DRAM_ONLY`

## D. Airfield atomic commit

Airfield reload／provider安定化が発生するセッションでは、ログに次を確認する。

```text
[CP2.5/MAP_DRAM] domain=AIRFIELD
normalLookup=DRAM_ONLY
cause=AIRFIELD_ATOMIC_COMMIT
```

Airfield revisionはRegistryのcommitted database revisionと一致すること。

## E. 終了・再起動

1. FlightからMain Menuへ戻る。
2. KSPを終了する。
3. 再起動してAを再実施する。

期待値：

- manifestからTerrain indexが再構築される
- 起動後の件数が前回commit済みmanifestと一致する
- AERIS由来のERROR／FATAL／Exceptionがない

## 合格条件

- A〜Eが成立
- `SYNC SSD 0 — PASS`
- Map DRAMがmetadata onlyである
- Preload Builderとpayload workerが継続する
- 操縦系、LAND separation、高度Gate、品質体系に退行がない
