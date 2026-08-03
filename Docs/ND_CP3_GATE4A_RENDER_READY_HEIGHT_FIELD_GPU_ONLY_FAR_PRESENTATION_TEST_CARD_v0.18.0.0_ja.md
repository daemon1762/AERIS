# ND CP3 Gate 4A 実機テストカード

## 事前確認

- タブ表記：`DEV CP3 GATE 4A — RENDER-READY HEIGHT FIELD & GPU-ONLY FAR PRESENTATION`
- SYSTEM：Residentの`RR/GPU`が表示される
- ログ：`[CP3_GATE4A_GPU_ONLY]`
- `cpu_terrain_draw=0`
- `CPU SAFETY FALLBACK`、`CPU_FALLBACK`、`UNKNOWN_TERRAIN`が出ない

## 基本表示

1. Kerbinで飛行開始する。
2. 5／10／20／40／80／160kmを順に確認する。
3. NORTH UPとTRACK UPを切り替える。
4. 250～350m/sで360°旋回する。

合格条件：

- 初回は`TERRAIN GPU BUILDING`を許容する
- 完成前のBACK bufferが表示されない
- 完成後のFRONTはFAR coverage 100%
- 黒い扇形・透明穴・CPU地形への切替がない
- ROUTE／LOCAL不足時もGPU FAR全面表示を維持する
- `CPU terrain draw count = 0`

## lifecycle

Terrain OFF、40km高度ゲートOFF、flight scene離脱、GPU acceleration OFFを順に試す。各場面でGPU resourceが解放され、再開時はrender-ready payloadから再昇格できることを確認する。

## telemetry判定

```text
front=PRESENTED|BUILDING
back_foundation=0.000..1.000
swap=0|1
blocked=<count>
render_ready=<count>/<bytes>
cpu_terrain_draw=0
```

`swap=1`は`back_foundation>=0.999`かつready FARがrequired FAR以上のときだけ許可する。
