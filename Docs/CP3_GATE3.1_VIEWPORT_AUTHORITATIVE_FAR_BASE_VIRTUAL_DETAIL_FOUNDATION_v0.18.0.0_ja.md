# CP3 Gate 3.1 — Viewport-Authoritative Far Base & Virtual Detail Foundation

## 目的

今回の実機ログではGPU処理待ちが0件でもTerrain coverageが約80%に留まった。原因はGPU性能ではなく、LOW品質時の粗地形要求が中心3×3へ制限され、TRACK UPで回転した実際のND viewport全域を要求していなかったことにある。

Gate 3.1では、固定半径式を廃止し、NDの実投影から必要なGLOBAL/FARタイルを逆算する。また、常設Terrain payloadをGLOBAL/FARへ限定し、ROUTE/LOCALを将来の再構成品質へ移行する基礎を作る。

## Terrain階層の再定義

```text
GLOBAL
  起動・欠落時の最粗bootstrap/fallback

FAR
  通常NDで唯一の常設・権威的Terrain base payload

VIRTUAL ROUTE
  Gate 4BでFAR、履歴、疎な補正sampleから再構成

VIRTUAL LOCAL
  Gate 4B以降、現在viewportの重要領域だけ高密度再構成

EXACT LOCAL / LAND
  滑走路、LAND回廊、障害物、安全検証対象だけ生成・保存
```

ROUTE/LOCALという品質名は残すが、現在天体の全面常駐・全面事前生成対象から外す。

## Viewport-authoritative planning

`AERISTerrainViewportFoundationPlanner`を追加する。

入力：

- 現在天体と半径
- environment hash
- ND中心緯度経度
- range
- TRACK UP/NORTH UP
- map heading
- 自機anchor（通常TRACK UPは0.75）
- RenderTexture orientation

投影は`AERISNdMapProjection`と共通化し、GUI座標から緯度経度へ戻すinverse transformを追加した。

viewportを各LODタイル幅の0.42倍以下の間隔でsampleし、取得した全タイルへ1タイルのguard ringを付与する。これにより、1.30倍の横幅、下寄りanchor、TRACK UP回転、タイル境界付近の移動を含めた粗地形供給を行う。

## 要求優先順位

```text
1. GLOBAL bootstrap foundation
2. FAR authoritative viewport foundation
3. viewport内に既に存在するexact ROUTE/LOCAL bridge
4. LAND ARM中のexact LOCAL/LAND endpoint
5. FAR Predictive Forward Corridor
6. その他background
```

GLOBAL/FAR foundationは品質profileの`MaximumTerrainTileRequests`より先に全件受理する。profile上限はfoundation以外のdetail/corridorだけへ適用する。

## Resident Cache

現在天体のbackground population対象を次へ限定する。

```text
GLOBAL
FAR
```

ROUTE/LOCAL/LANDは次の場合だけResidentへ入る。

- 現在viewportで同一exact payloadがRAMまたはSSDに既に存在する
- LAND ARM中の選択滑走路endpoint
- 将来のexact microtile policyが明示的に要求する

## Predictive Forward Corridor

Gate 3の速度・進行方向・旋回率予測は維持する。ただしGate 4B完成まではFARだけを先読み・pinする。巡航中に欠落したROUTE/LOCALをPQSで大量生成しない。

## 完成判定

GLOBALはbootstrap/fallbackであり、FAR完成を遅らせる権威にはしない。SYSTEM表示とcoverage telemetryのfoundation requested/missingはFARを基準にする。

## Gate 4への接続

Gate 3.1では仮想ROUTE/LOCALの画像再構成自体は実装しない。Gate 4AでRender-ready height fieldを追加し、Gate 4BでTemporal Reconstructionを接続する。

## 禁止事項

- fixed 3×3 coarse viewportへの回帰
- ROUTE/LOCALの現在天体全面background population
- 巡航中のLAND payload展開
- Map DRAMへのdecode済みpayload混入
- main-thread同期SSD read
- Flight safety laneの占有
- Track B、LAND制御、AA/AP/PROTECTの変更
- FULL BOOST復活
