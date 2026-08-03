# CP3 Gate 4A — Render-Ready Height Field & GPU-Only FAR Presentation

## 目的

Gate 3.1で確立したViewport-authoritative FAR baseを、CPU描画に逃げずGPUだけで連続表示する。CPUは地形データのdecode・検証・render-ready化だけを担当し、NDへ地形を描画しない。

## Render-Ready Height Field

Workerが生成する不変payloadは次を保持する。

- tile identity／generation／style key
- 緯度経度bounds
- height metres
- land／water分類
- validity／shade
- triangle topology
- coastline／contour segments

このpayloadは`UnityEngine.Object`を含まず、MeshやRenderTextureを解放した後も独立budget内で再利用できる。Resident Cache状態は`RAM RESIDENT → RENDER READY → GPU READY`へ昇格し、GPU解放時は安全にdemoteする。

## GPU-only表示契約

```text
CPU decode / render-ready build
              ↓
GPU BACK bufferを非表示で構築
              ↓
FAR foundation 100% + 全FAR GPU-ready
              ↓
GPU FRONTとatomic swap
              ↓
FRONTだけをNDへ表示
```

BACKが未完成の間は表示しない。既に互換性のある完成FRONTがあれば、そのGPU画像を維持する。初回など完成FRONTがなければ`TERRAIN GPU BUILDING`を表示し、CPU地形やUNKNOWN_TERRAINで埋めない。

## 禁止経路

- `AERISTerrainRasterWorker`によるCPU terrain texture生成
- CPU safety fallback
- CPU／GPU混在合成
- partial BACK bufferの直接表示
- UNKNOWN_TERRAIN塗り潰し
- ROUTE／LOCAL不足を黒・透明穴として表示

ROUTE／LOCALが未完成でも、完成したGPU FAR baseを保持する。

## Resource lifecycle

以下でMesh／Material／FRONT／BACK RenderTextureを解放する。

- Terrain／ND OFF
- 高度ゲートによるflight viewport停止
- scene transition
- GPU acceleration無効化
- GPU capability不成立

Render-ready CPU payloadは通常のGPU解放では維持し、再昇格に使う。Dispose時にはrender-ready payloadも解放する。

## Gate 4A外

Temporal Reprojection、jittered sampling、confidence/reactive mask、Virtual ROUTE／LOCAL再構成はGate 4Bで実装する。
