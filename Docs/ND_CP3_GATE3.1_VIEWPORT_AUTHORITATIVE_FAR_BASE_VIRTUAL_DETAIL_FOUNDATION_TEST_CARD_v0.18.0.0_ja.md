# ND CP3 Gate 3.1 実機テストカード

## 1. build表示

タブ上部が次であること。

`AERIS v0.18.0.0 DEV CP3 GATE 3.1 — VIEWPORT-AUTHORITATIVE FAR BASE & VIRTUAL DETAIL FOUNDATION`

旧CP2、Gate 3、Hotfix 1だけの表記ならFAIL。

## 2. viewport全面供給

Kerbin飛行中にTRACK UPを使用し、5/10/20/40/80/160kmを順逆に変更する。各rangeで360°旋回する。

合格条件：

- 黒い扇形、斜めの未要求領域、固定矩形境界が出ない。
- `terrain_gpu_pending=0`になった後、FAR foundationのmissingが0になる。
- 40km以上でもGLOBAL/FAR要求数が固定3×3相当へ留まらない。
- headingによって最終coverageが約80～95%へ変動せず、100%へ収束する。

## 3. SYSTEM表示

次を確認する。

`CP3 Foundation: GLOBAL/FAR G/F | missing M/R | detail VIRTUAL (exact SSD bridge / LAND microtile)`

通常巡航でResidentのROUTE/LOCALが自動的に大量増加しないこと。

## 4. CP3ログ

10秒周期ログに次が含まれること。

`foundation_gf=G/F; foundation_missing=M/R`

FARが揃った後は`foundation_missing=0/R`になること。

## 5. Predictive Corridor

100～350m/sで直進・旋回し、Corridor要求が維持されること。巡航中はFAR先読みだけが増え、欠落ROUTE/LOCALのPQS生成を大量発生させないこと。

## 6. LAND分離

LAND DISARM中はLAND payloadを展開しない。滑走路を選択してLAND ARMした場合だけ、両endpointのLOCAL/LAND exact要求とpinが有効になること。DISARMで解放されること。

## 7. 回帰

- Runway Map Lock、滑走路、ILS漏斗の位置が変化しない。
- AA/AP/PROTECT/LANDの操縦挙動が変化しない。
- Map DRAMはmetadata-onlyのまま。
- SSD/CRC/hash/decompress/GPU worker failureが0。
- 同期SSD readとFlight safety lane使用が0。
