# CP2.5 総合受入テストカード — Candidate 4

## 0. ビルド

- SHA-256一致。
- Mono/xbuild成功。
- 起動版名に`CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 4`と`PRELOAD THROUGHPUT ARCHITECTURE HOTFIX 1`。
- AERIS起因ERROR／FATAL／Exceptionなし。

## 1. 標準Preload

1. Main MenuまたはSPH/VABで、十分な未構築tileがある天体をBUILDする。
2. FULL BOOSTを押さずに60秒以上観測する。
3. 表示が`STANDARD ACTIVE — former BOOST envelope`となることを確認する。
4. PQS producer、parallel encode、SSD super-batchが進行することを確認する。
5. Candidate 3旧BOOST程度以上のtile進捗が得られることを確認する。

## 2. 手動FULL BOOST

1. 非Flightで`START PRELOAD BOOST — FULL`を押す。
2. `FULL BOOST ACTIVE`、workers／permits／queue／PQS budgetが増加することを確認する。
3. pipeline limitが256、pending block limitが12になることを確認する。
4. encode queue／active encode、SSD queue／active writer、last super-batchを動画に記録する。
5. 少なくともproducer、CPU compression、SSD writeのいずれかで標準より明確にthroughputが増えることを確認する。
6. `STOP PRELOAD BOOST — FULL`で標準へ戻ることを確認する。
7. 再度FULLを開始してFlightへ入り、次を確認する。

```text
[PRELOAD_BOOST] state=STOPPED; reason=FLIGHT_SAFETY
```

8. KSP再起動後にFULLが自動再開しないことを確認する。

## 3. 律速表示

未構築workがある状態で、次のいずれかが状況に応じて表示されることを確認する。

```text
BOTTLENECK: PQS PRODUCER
BOTTLENECK: CPU COMPRESSION
BOTTLENECK: SSD WRITE
BOTTLENECK: PIPELINE BALANCED
```

workがなくなれば`NO WORK`を許容する。

GPU対応環境では、今回のsource-only版は次を表示してよい。

```text
GPU COMPUTE CAPABLE — NO PRELOAD KERNEL ASSET / CPU AUTHORITATIVE
```

GPU使用率上昇はCandidate 4の合格条件ではない。空演算で使用率を作らないこと。

## 4. 安全smoke test

- UIからSTOP操作ができ、Main Menu／SPH／VAB操作が致命的に固まらない。
- Flight中にPreload producer／encode／writeが継続しない。
- ND／FDI AUTO・ALWAYS・OFFとSPEED-only FDIがCandidate 3同様に動く。
- Candidate 2 lifecycle切替時に破棄済み`SyncModuleControlSurface` callbackが出ない。
- Gate 4終了時`result=PASS`かつ`synchronousSSD=0`。

## 提出物

- `AERISFlightControl.log`
- 最新performance CSV
- `KSP.log`
- STANDARDとFULLの各60秒程度が比較できる動画
- Preload UIのpipeline／bottleneck表示が読める画像
