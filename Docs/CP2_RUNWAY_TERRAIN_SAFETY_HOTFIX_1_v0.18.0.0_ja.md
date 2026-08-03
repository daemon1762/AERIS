# AERIS v0.18.0.0 CP2 Runway / Terrain Safety Hotfix 1

## 目的

AERIS14実機動画・ログで確認された、MOD空港のLAND/ILS方向逆転とGPU地形色描画不良を安全側で修正する。

この版はCP2開発版であり、LANDは引き続き観測・計画・表示専用である。自動着陸、操舵、スロットル、ブレーキ、Ground Assistへの書込み権限は追加しない。

## 滑走路方向の安全修正

- 滑走路登録Headingと、運用閾値から反対端への実方位を比較する。
- 誤差10度以内を整合とする。
- 約180度の相反方向が一致する場合は、閾値・反対端、Physical Start/End、Usable Start/Endを自動で交換する。
- 自動交換後にTouchdown Aim、Glide Slope Anchor、Rollout Endを再生成する。
- どちらにも一致しない方向は`ReciprocalMismatch`として認証を拒否する。
- 誤方向はLAND ARM、NDのLOC/GS誘導、将来用Runway Track Tokenをすべて拒否する。
- 認証アルゴリズムを1680へ更新し、旧キャッシュを再認証対象にする。

## 正しい進入側の判定

- 閾値より滑走路内側・反対側にいる場合は`NOT ON APPROACH SIDE`とする。
- その場合はLOC漏斗を誘導形状として表示しない。
- GS目標値・誤差は`N/A`とし、数値誘導を出さない。
- 滑走路線そのものは位置確認用として維持する。

## GPU TOPO / REL再構築

旧方式は組み込みShaderの`mainTextureScale`／`mainTextureOffset`で色パレットを読み替えていた。実機ではRELが高度に追従せず赤のまま、TOPOにも色描画不良が発生したため、この方式を撤去した。

新方式：

- Workerは地形標高をメートル単位で保持する。
- Main ThreadでTOPO／RELの最終頂点色を明示的に計算する。
- RELは`機体ASL高度 - 地形ASL標高`から色帯を決定する。
- 5m高度バケットごとに頂点色だけを更新し、地形メッシュを再生成しない。
- TOPOは標高-500m～12000mの固定勾配を使用する。
- GPUは計算済み頂点色を描画するだけとし、ShaderのUVパレット変換へ依存しない。

標準REL色帯：

- clearance 30m以下：赤
- 30～300m：黄
- 300～600m：緑
- 600m超：暗緑

## 陸海境界

- 水面はTOPO／REL共通の固定青色`RGB 8,52,118`とする。
- 陸と水を別メッシュへ分離する。
- 陸水混在三角形を境界で分割し、陸色が水面側へ補間されないようにする。
- 水セルを含む地形セルでは等高線を生成しない。
- 海岸線を独立した帯状三角形メッシュとして描画する。
- 海岸線は通常等高線より明確に太くする。

## GPUモード

- `AUTO`：RenderTexture、ARGB32、最低Shader capabilityを満たす場合だけGPUを使用し、満たさない場合はCPUへ安全fallbackする。
- `ON`：GPUを明示的に試す診断・強制モード。実GPU生成失敗時はCPU fallbackへ戻る。
- `OFF`：GPU地形を使用しない。

## 維持した既存修正

- `TERR Y DIRECT / FLIP`診断切替
- `[ND/TERRAIN_ALIGN]`位置整合ログ
- ND上の`CLR SEL`
- 画面外へ伸びるLOC/GS形状のクリッピング
- visual coverageとrequested-quality coverageの分離
- Preview下敷き維持

## 意図的に未変更

- AP、BANK、HDG、PITCH、V/S、ALT、ACC、VEL
- 旧NAV不在、新NAV BLOCKED
- LANDへの操縦権限付与
- 45秒周期stale cancellation
- 接地後の誤liftoff判定
- Airfield Snapshotの残存時間超過

