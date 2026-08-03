# CP3 Gate 1 実機テストカード

## 目的

Gate 1はpayload経路未接続であるため、表示高速化ではなく、起動・scene遷移・
body scope契約が既存機能を壊していないことを確認する。

## ビルド前

1. 添付Source ZIPを新規フォルダへ展開する。
2. `build_ubuntu.sh`を実行する。
3. static acceptanceが全PASSすることを確認する。
4. build表示に`CP3 GATE 1 CURRENT BODY RESIDENT CACHE CONTRACTS`が含まれることを確認する。

## KSP実機

KSP起動は1回でよい。

1. Main Menuまで正常起動する。
2. KSCからKerbin機体をロードする。
3. `KSP.log`に次の形式が1回現れることを確認する。

```text
[CP3_RESIDENT] ... body=Kerbin ... payloadRoute=DISCONNECTED
```

4. ND/Terrain表示がCP2.5最終版と同じく動作する。
5. Terrain Display OFF、ND AUTO、40km altitude gateで例外が出ない。
6. Space Centerへ戻り、別機体でFlightへ再進入して例外が出ない。
7. Mun等へbody transitionできる場合、旧Kerbin scopeのcommitが残らず、
   新bodyの`[CP3_RESIDENT]`ログが出ることを確認する。
8. AIRFIELDS、滑走路選択、LAND ARM/DISARM、AA、通常AP、PROTECTに回帰がない。

## 合格条件

- NullReferenceException、InvalidOperationException、collection modified例外なし
- main thread同期Terrain payload readの新規ログなし
- FULL BOOST UI・状態・ログが復活していない
- CP2.5表示品質と操作に回帰なし
- Gate 1だけではRAM RESIDENT tile数が0のままでよい
