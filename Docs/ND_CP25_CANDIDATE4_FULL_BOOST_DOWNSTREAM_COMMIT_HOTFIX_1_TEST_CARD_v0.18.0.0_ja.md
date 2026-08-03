# 省再起動実機テストカード — Downstream Commit Hotfix 1

KSP起動は1回、最後の非永続確認で再起動1回だけ行う。

1. Main MenuまたはSpace CenterでSTANDARDを30秒観察する。
2. FULL BOOSTを60秒以上動作させる。
3. `required-drop=0`、TileまたはMap DRAM revisionの継続増加を確認する。
4. STOPを押し、再起動せず60秒観察する。
5. `encode active <= cap`へ減少し、SSD jobsが0〜1へ戻り、Tile進捗が再開・継続することを確認する。
6. FULLを再度30秒動作し、Flightへ移動して`FLIGHT_SAFETY`停止を確認する。
7. Space Centerへ戻り、STANDARDが再開することを確認する。
8. 正常終了後、KSPを1回だけ再起動し、FULLが自動再開しないことを確認する。

合格条件:
- `required-drop = 0`
- AERIS ERROR/FATAL/Exceptionなし
- STOP後、`encode`または`ssd`が同一値で12秒以上固定しない
- STOP後にKSP再起動なしでTileまたはMap DRAM revisionが増える
- `encode active <= 56`（FULL）、`<=32`（STANDARD）
- SSD jobs `<=2`（FULL）、`<=1`（STANDARD）
- FULLは再起動後OFF
