# CP3 Gate 5 Candidate 2 — Presentation Latch / Runway Lock Hotfix 1

Gate 5 Candidate 1は実機で短いTerrain表示欠落と滑走路I字マーカーの浮遊追従を確認したためREJECTED。

Candidate 2はGPU FRONTを変形せず継続表示するpresentation latchを導入する。新しい要求projectionのFAR BACKが完成するまでは、同一天体・同generationの最後の完成FRONTを最大8秒だけ保持する。NDはrendererからそのFRONTの中心緯度経度、range、heading、TRACK/NORTH、anchorを受け取り、滑走路、交通、trail、track vector、自機シンボルを同じprojectionで描く。

Temporal GUI.matrix/affine warpは復活させない。CPU terrain presentationも引き続き0。

40 km以上では滑走路端threshold tickと端番号を描かず、5/10/20 kmでは従来どおり番号を表示する。Terrain OFFではGPU resource release契約を維持し、OFF→AUTO再構築時は青系standbyを表示する。

map-lock診断も要求projectionではなく実際のpresented FRONT projectionへ変更し、表示上の滑走路/地形整合をテレメトリで直接検証できるようにする。
