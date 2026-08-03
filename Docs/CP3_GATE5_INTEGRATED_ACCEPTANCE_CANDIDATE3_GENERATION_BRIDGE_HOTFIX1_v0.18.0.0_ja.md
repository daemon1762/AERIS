# CP3 Gate 5 Candidate 3 — Generation Bridge Hotfix 1

Candidate 2で残った通常飛行中の短時間ブラックスクリーンを修正する。

原因はGPU FRONT不足ではなく、`TerrainGeneration`切替時に完成済みFRONTが残っていても
`CanPresentLatchedFront()`が世代不一致だけで拒否していたことだった。

Candidate 3では、同一天体・同じ天体半径・FRONT age 8秒以内に限り、旧世代の完成GPU FRONTを
**表示継続専用bridge**として許可する。表示中はそのFRONTが持つcenter/range/heading/TRACK UP/anchorを
ND全world layerへ公開するため、滑走路・traffic・trail・ownshipとTerrainは同一projectionを維持する。

この旧FRONTはLANDや安全判定のauthorityには使用しない。CPU terrain draw=0、GUI.matrix temporal warp禁止、
FAR由来Virtual Detail、Route/Local通常常設なし、startup airport/runway NONEは維持する。
