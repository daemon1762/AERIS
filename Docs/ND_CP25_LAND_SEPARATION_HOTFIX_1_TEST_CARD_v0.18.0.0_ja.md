# CP2.5 Gate 3 — LAND Separation Hotfix 1 実機試験カード

## 目的

通常巡航中にはLAND解像度が展開されず、Developer許可とLAND ARMが同時に成立した場合だけLAND profile／滑走路LAND request laneが有効になることを確認する。

## 前提

- `SYSTEM > DIAGNOSTICS`で`Enable LAND detail when landing demand is active`を確認できる。
- 認証済み滑走路を選択できる固定翼機を使用する。
- 高度39,500m未満でNDを表示する。

## 試験A：通常巡航の分離

1. Developer LAND許可をOFFにする。
2. 滑走路だけを選択し、LAND ARMはしない。
3. DIAGNOSTICSのRuntime表示が`DEVELOPER CAPABILITY DISABLED`であることを確認する。
4. Developer LAND許可をONにする。
5. LAND ARMしないまま飛行し、Runtime表示が`STANDBY — WAITING FOR LAND ARM / APPROACH`となることを確認する。
6. `ND effective`がAUTO／LOW／MEDIUM／HIGHの基礎品質であり、LANDにならないことを確認する。

## 試験B：LAND ARMによる起動

1. 認証済み滑走路を選択し、Developer LAND許可をONにする。
2. 空中でLAND ARMする。
3. Runtime表示が`LAND DETAIL ACTIVE — LAND_ARM`へ変化することを確認する。
4. `ND effective: LAND`となることを確認する。
5. ログに次が一度だけ出ることを確認する。

```text
[CP2.5/LAND_DETAIL] LAND DETAIL ACTIVE — LAND_ARM
```

## 試験C：解除

1. LANDをDISARMする。
2. Runtime表示がSTANDBYへ戻ることを確認する。
3. `ND effective`が元の基礎品質へ戻ることを確認する。
4. LANDの解除でND、AP、AA、PROTECT、FDIが停止・解除されないことを確認する。
5. ログでACTIVEとSTANDBYがチャタリングしないことを確認する。

## 試験D：高度Gate併用

1. LAND ARMかつDeveloper LAND許可ONの状態で40,500m以上へ上昇する。
2. Terrain viewport OFFに伴いLAND detailもOFFになることを確認する。
3. 39,500m未満へ降下した時、LAND ARMが維持されていればLAND detailが再びACTIVEになることを確認する。

## 保存確認

KSP再起動後、設定ファイルに次が保存されることを確認する。

```text
terrainQualityModelRevision = 2
terrainLandRuntimeQualityEnabled = True
```

Developer LAND許可をOFFにして保存した場合は`False`となる。

## 合格条件

- 滑走路選択だけではLAND profile／runtime LAND requestが起動しない。
- Developer許可だけでも起動しない。
- Developer許可＋LAND ARMでのみLANDが起動する。
- DISARMで即座に基礎品質へ戻る。
- SSD Preload Builderの進行はLAND runtimeのON/OFFに影響されない。
- AP／AA／PROTECT／FDIへの操縦権限変更がない。
