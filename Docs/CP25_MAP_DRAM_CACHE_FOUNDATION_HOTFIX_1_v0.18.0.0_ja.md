# AERIS v0.18.0.0 CP2.5 Gate 4
## Map DRAM Cache Foundation Hotfix 1

CP2.5のMap DRAM Cacheは、地形本体ではなく「地図を検索するための軽量メタデータ」をDRAMへ常駐させる層である。

## 常駐対象

- Airport Registry
- Physical Runway Registry
- Direction Registry（Virtual Localizer／Glide Path情報を含む）
- Terrain Tile Index
- Terrain LOD Index
- TileからSSD chunk位置を引くための索引

Airfield Registryが新しいデータベースをatomic commitした時点で、Airfield／Runway／Directionをdeep cloneし、単一revision snapshotとして公開する。Terrain Preload Databaseは起動時にmanifestだけを読み込み、Tile／LOD／chunk-locationメタデータを同じMap DRAM Cacheへ公開する。

通常のTerrain存在確認とchunk位置検索は、このimmutable snapshotだけを読む。Flight中の通常検索経路では`File.Exists`、`FileInfo`、manifest再読込、chunk内容読込を行わない。地形payloadの読込が必要になった後は、従来どおりshared worker／disk laneへ要求を渡す。

## CP3との境界

Gate 4のMap DRAM Cacheには次を保持しない。

- 圧縮済みTerrain payload
- decode済み高度配列
- Current-body resident tile
- Render-ready mesh
- GPU Mesh／Material／RenderTexture

これらはCP3のCurrent-Body Resident Cacheと、速度予測型Forward Corridor Preloadで実装する。

## 更新契約

- Airfield：staged databaseのatomic commit後だけpublish
- Terrain：startup manifest load、manifest commit、chunk repair/reindex、tile/chunk invalidation後にpublish
- reader：volatile snapshot参照のみ
- writer：単一publish lockでrevisionを直列化
- Airfield query：snapshot内部を外部から変更できないようcloneを返す

## 起動I/Oの削減

manifestには同じchunkへ属する複数tileが並ぶ。Gate 4ではblob存在確認と`FileInfo`取得をtileごとではなくchunkごとに一度だけ実行する。存在しないchunkは同じstartup load内で再検査しない。

## DIAGNOSTICS

`SYSTEM > DIAGNOSTICS`へ以下を追加した。

- Map／Airfield／Terrain source revision
- Airfield／Runway／ILS-Direction件数
- Terrain Tile／Chunk件数
- GLOBAL／FAR／ROUTE／LOCAL／LAND件数
- 推定DRAM使用量
- publish時間
- lookup hit／miss
- synchronous SSD lookup violation count

`SYNC SSD 0 — PASS`が通常状態である。
