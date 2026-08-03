# AERIS Flight Control v0.16.3.0

## MOD Runway Survey / ILS Runway-Only

### 目的

独立LANDのShadow Guidanceより先に、現在導入されているMOD飛行場の固定翼滑走路データを安全に生成する。滑走路以外の施設はLAND/ILSの選択・表示対象にしない。

### 現在構成のカタログ

提供された`ModuleManager.ConfigCache`の`Category = Runway`を基準に41レコードを収録した。

| Method | Records | 動作 |
|---|---:|---|
| StaticBounds | 34 | KKの実行時Transformとboundsから両端・長さ・幅を測量 |
| PairedThresholds | 2 | TSC RWY09/27を対向Thresholdとして1本へ統合 |
| ManualRequired | 5 | X字形または汎用SpawnPoint。自動推測を禁止 |

カタログに存在するだけでは不十分で、UUID/LaunchSite名/Group/構成パス/モデルが一致し、Provider CategoryもRunwayである必要がある。Registry走査では副作用のある`StaticInstance.mesh` getterを呼ばず、既に読込済みのMeshまたはモデルPrefabのMesh boundsを使用する。測量結果が長さ・幅・縦横比制限を外れた場合はLAND ARMを拒否する。

### ILS表示

LAND/ILSの飛行場リストは固定翼滑走路だけを表示する。ヘリパッド、RocketPad、LaunchPad、Harbour、WaterLaunch、Unknownは内部安全分類には残すが、ユーザーのILS選択肢には出さない。

- OVERLAY: Terrain＋選択滑走路＋LOC/GS
- FOCUS: clean background＋選択滑走路＋LOC/GS
- LAND/ILS中は周辺飛行場・ヘリパッド・発射台・港の記号を描かない
- TERRAIN単独時の施設記号は従来どおり利用可能

### 安全境界

Runtime Surveyは第一関門の表示・観測用Geometryであり、LANDに操舵、スロットル、ブレーキ、AP ARM権限を与えない。X字滑走路を一本の滑走路と推測しない。カタログ外または測量失敗時は`RUNWAY GEOMETRY REQUIRED`のままとする。旧NAVは含まれない。

### 検証

```bash
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01630_acceptance.py
```
