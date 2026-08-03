# CP2.5 Candidate 4 Full Boost Downstream Commit Hotfix 1

## 発見事項
AERIS35実機ログではFULL動作中のTerrain Blockは`requiredDropped=0`で前進したが、手動STOP後に`encode=176/114`、`ssd=6/1`が固定した。CPU EncodeとSSD Super Batchが通常の`SubmitLatest`経路に残っており、共有Schedulerのbest-effort dropで完了callbackを失う余地があった。

## 修正
CPU EncodeとSSD書込みを`SubmitRequired`へ昇格した。queue満杯時は既存jobを捨てず、呼出側が所有権を保持して再試行する。worker失敗またはstale時も`Commit(null)`で所有権を終了し、Encodeは再queue、SSD batchは元のchunk batchへ戻す。

## 下流commit予算
FULLはBlock 192、CPU Encode最大56、SSD Write最大2で合計250。Schedulerのnominal result capacity 256未満に固定した。STANDARDはBlock 96、Encode最大32、Write最大1で合計129。

Encode上限はworker数へ追従するが、STANDARD 16〜32、FULL 32〜56の範囲を越えない。queue admission拒否は実行失敗回数へ数えない。

## STOP契約
STOPは新規仕事の上限を即時にSTANDARDへ戻す。FULL中に既にaccept済みのrequired jobは破棄せず、そのままcallbackまでdrainする。KSP再起動なしでSTANDARDのtile進捗へ戻ることを実機合格条件とする。

## UI・ログ
Preload MapsにEncode/SSDのcommit上限とadmission rejectを表示する。`[PRELOAD_THROUGHPUT]`には`encodeRequired`、`encodeReject`、`ssdRequired`、`writeReject`を出力する。

## 非変更範囲
AA、AP、PROTECT、LAND、ND/FDI表示方針、Map DRAM、滑走路座標、Track B、CP3 Current-Body Resident Cacheは変更しない。
