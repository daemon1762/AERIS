# CP2.5 Gate 2 実機テストカード
## Quality Migration Hotfix 1

## A. 既存設定移行

1. Gate 1以前の`AERISSettings.cfg`をバックアップする。
2. `terrainQualityMode`をAUTO、ECO、BALANCED、HIGH、ULTRAの各値で起動確認する。
3. 期待値を確認する。
   - AUTO -> AUTO
   - ECO -> LOW
   - BALANCED -> MEDIUM
   - HIGH -> HIGH
   - ULTRA -> HIGH
4. 保存後に`terrainQualityModelRevision = 2`となることを確認する。
5. KSPを再起動し、同じ設定が維持されることを確認する。

## B. 通常UI

- SYSTEM > OPTIONSのTerrain qualityが
  `AUTO / LOW / MEDIUM / HIGH`の4択だけである。
- ECO、BAL、ULTRA、LANDが表示されない。
- PRELOAD MAPSのbody品質にLANDが表示されない。

## C. Developer LAND

- SYSTEM > DIAGNOSTICSにLAND runtime品質の明示設定がある。
- body別LAND preload設定がDIAGNOSTICSにある。
- LANDを有効化しても、OPTIONSの通常4択のいずれかとして誤表示されない。
- AUTO運用中に有効品質がLANDへ自動昇格しない。

## D. Gate 1回帰

HHC-4等で高度境界を再確認する。

- 40,500m以上でTerrain ND OFF
- 39,500m未満でON
- 中間帯でチャタリングなし
- FDI/AP/AA/PROTECTは継続

## 収集物

- `AERISFlightControl.log`
- `KSP.log`
- SYSTEM > OPTIONSおよびSYSTEM > DIAGNOSTICSのスクリーンショット
- 旧設定移行前後の`AERISSettings.cfg`
