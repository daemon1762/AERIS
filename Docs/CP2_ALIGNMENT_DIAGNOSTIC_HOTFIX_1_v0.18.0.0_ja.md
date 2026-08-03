# AERIS v0.18.0.0 DEV CP2 Alignment Diagnostic Hotfix 1

## 目的

2026-07-25の実機動画と同一セッションログで確認された、ND地形と自機・滑走路表示の地理的不一致を切り分ける。同時に、原因がコード上確定したLAND表示と選択UIの不具合を修正する。

本チェックポイントはCP2の実機診断版であり、CP2完了版、RC、LAND完成版、新NAV実装版ではない。

## 入力証拠

- `Video_2026-07-25_09-40-16.mkv`：約16分35秒、1920×1200、30fps
- `AERISFlightControl(7).zip`：実機投入済みDLL、設定、Terrain Preload DB、最新セッションログ／性能CSV
- 原本：`AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip`

## 実機で確認した問題

### 1. 地形位置の不一致

自機・滑走路シンボルはIsland Airfieldへ接近する一方、島の地形描画が別位置に残り、実景で島上空へ到達してもND地形が自機直下へ収束しなかった。単なる生成遅延ではなく、GPU RenderTextureからGUIへ提示する際のY方向対応不一致が第一候補である。

### 2. 選択解除操作の欠落

NDから空港／滑走路を選択できるが、同じND上で選択状態を解除できなかった。LAND DISARMだけではRegistry選択が保持される。

### 3. LOC／GS形状の消失

LAND平面表示は滑走路両端の一方が画面外へ出ると描画関数全体を終了していた。そのため滑走路線だけでなく、LOC中心線、左右漏斗境界、終端線、XTK表示まで同時に消えた。

GS縦断表示は機体高度を縦スケールへ含めるため、捕捉範囲から大きく外れた場合に基準線が圧縮され、実質的に見えなくなる場合があった。

### 4. coverage 100%誤報

fallback地形で画面が埋まった状態を、要求中の最終品質完成度と同一視していた。samplingが残っていてもcoverage 1.000を報告し得た。

## 実装内容

### Terrain RenderTarget方向を明示化

- `AERISTerrainRenderTargetOrientation`を追加
  - `Direct`
  - `Flipped`
- 既定値を`Direct`とした
- `SystemInfo.graphicsUVStartsAtTop`による自動反転を描画決定から除外した
- NDの`MENU`へ次の切替を追加した
  - `TERR Y DIRECT`
  - `TERR Y FLIP`
- 選択値は設定へ永続化する

`graphicsUVStartsAtTop`自体は診断ログへ残すが、表示方向を自動決定する根拠にはしない。

### Alignment診断ログ

GPU地形描画中、2秒周期で`[ND/TERRAIN_ALIGN]`を出力する。

記録項目：

- presentation orientation
- graphics backend
- `graphicsUVStartsAtTop`
- 地図中心緯度・経度
- 自機緯度・経度
- ND range、heading、TRACK UP状態、anchor
- 自機中心点に対応するRenderTexture座標
- GUI提示後の予測座標
- 自機基準点からの`deltaPx`
- fallback込み`visualCoverage`
- 要求品質のみの`requestedCoverage`
- 自機中心を含むTileキー、南北東西境界、Tile内局所座標

正しいorientationでは、自機追従表示中の`deltaPx`が概ね`0,0`へ近づく。誤った縦反転ではY差が大きくなる。

### ND `CLR SEL`

選択中のみND右上へ`CLR SEL`を表示する。押下時：

1. LAND観測をDISARM
2. 選択Directionを解除
3. 選択Runway／Airfieldを解除
4. 選択解除状態を設定へ保存
5. previewとprepared frameを破棄

明示的に解除した状態はRegistry再読込や再起動後も勝手に既定空港へ復帰しない。新しい空港を選択すると解除ラッチを解く。

### LAND／ILS線分クリッピング

`DrawLandingPlan`の画面外早期returnを廃止し、Liang–Barsky法で各線分をND viewportへクリッピングする。

対象：

- 滑走路中心線
- LOC中心線
- LOC左右漏斗線
- 漏斗終端線

線分が画面と交差する限り、端点が画面外でも表示を維持する。

### GS縦断表示

- 縦スケールを機体高度から分離
- 認証GS角とcapture distanceを基準に表示範囲を固定
- 中心GS線に加えて上下漏斗境界と遠端capを描画
- 機体が範囲外の場合は機体記号だけを端へクランプし、GS形状自体は圧縮しない

### coverage分離

- `visualCoverage`：fallbackを含む、現在画面が埋まっている割合
- `requestedCoverage`：現在要求しているstyle／品質の完成割合

ND状態と性能telemetryへ公開する完成度は`requestedCoverage`を使う。fallbackだけでsampling中の要求を100%完成とはしない。

## 安全境界

変更対象はSettings、Airfield Registry、ND UI、Terrain GPU renderer、文書・検証のみ。

以下は原本とバイト一致を検証する。

- BANK
- HDG
- PITCH
- V/S
- Ground Stability Protection

LANDは観測・計画・表示のみで、操舵権限を追加しない。

## 意図的に未修正

次は別原因・別安全領域のため、本パッケージでは変更しない。

- steady状態でも約45秒周期で増える`stale_cancelled`
- 接地後の一時的な誤liftoff判定とGround ARM AP解放
- Airfield Snapshot中の単発約104msスパイク
- Tile境界／LODポップの最終品質調整

まず地形座標一致、LOC／GS持続、選択解除を実機で確定し、その結果を基に次Hotfixの範囲を議論する。

## 未実施項目

作成環境にはMono/xbuild、KSP 1.12.5参照DLL、Unity/KSP実行環境がないため、ネイティブC#コンパイル、KSP起動、GPU実描画は未実施。静的受入後、ユーザー環境で試験カードを実行する。
