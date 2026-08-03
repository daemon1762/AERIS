# CP2.5 総合受入テストカード — Candidate 1

## 0. 起動・ビルド

- SHA-256一致。
- Mono/xbuild成功。
- 起動ログの版名が`CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 1`。
- AERIS起因のERROR／FATAL／Exceptionなし。

## 1. AIRFIELDS 0件UI修正

1. `USER CALIBRATED — MANUAL (0)`を確認する。
2. 見出しをクリックしても展開せず、`None.`行や巨大な空白が出ない。
3. スクロールバー、下段カテゴリ、Close、Resizeが正常に操作できる。
4. KSP再起動後も0件カテゴリは閉じたまま。
5. ログに必要時のみ次が1回記録される。

```text
[AIRFIELDS/UI] USER CALIBRATED category forced collapsed because count=0.
```

## 2. Gate 1 高度

HHC-4等で39.5km未満から40.5km以上へ上昇し、再び39.5km未満へ降下する。

- 40,500m以上でND Terrain viewport OFF。
- 40,500～39,500mでOFF維持。
- 39,500m未満でON。
- FDI、AP、AA、PROTECT、Preloadは継続。
- 古い地点のTerrainが再表示されない。

## 3. Gate 2 品質

- OPTIONSはAUTO／LOW／MEDIUM／HIGHのみ。
- DIAGNOSTICSにDeveloper LAND設定がある。
- 設定変更と再起動保持が正常。

## 4. Gate 3 LAND分離

- Developer LAND ON、LAND未ARMではSTANDBY。
- LAND ARMでACTIVE／effective LAND。
- DISARMでSTANDBY／基礎品質へ即時復帰。
- SSD Preloadは継続。

## 5. Gate 4 Map DRAM

DIAGNOSTICS期待値：

```text
STATE READY / DRAM-ONLY LOOKUP
AIRFIELD NORMAL READ  DRAM SNAPSHOT + ID INDEX — ACTIVE
SSD GUARD OBSERVED > 0
ALLOWED STARTUP/MAINT > 0
SYNC SSD 0 — PASS
```

AIRFIELDS開閉、複数空港・方向選択、CLEAR、ND RANGE／TRK UP／PLAN、TOPO／REL、Preload進行を行う。

終了時：

```text
[CP2.5/MAP_DRAM_SUMMARY]
synchronousSSD=0
result=PASS
```

## 6. 長時間・場面遷移

- Flight→Main Menu→Flight。
- 機体切替、爆散後再開、UI開閉。
- 30分以上運用し、チャタリング、古いsnapshot、同期SSD違反、UI崩れがない。

## 提出物

- `AERISFlightControl.log`
- 最新session log／performance CSV
- `KSP.log`
- AIRFIELDS 0件操作、Gate 1境界、Gate 3 ARM/DISARM、Gate 4 DIAGNOSTICSが分かる動画
