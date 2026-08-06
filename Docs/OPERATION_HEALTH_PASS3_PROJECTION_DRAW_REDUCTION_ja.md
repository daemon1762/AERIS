# Operation Health Pass 3 — Projection & Draw Submission Reduction

## 目的
Candidate11/Operation Health Pass 1-2 の画質 authority を一切変更せず、Terrain BACK render の CPU/GPU submission overhead を削減する。

## 変更範囲
- projected vertex 更新ごとの `Mesh.RecalculateBounds()` を廃止し、ND presentation 専用の保守的固定 Bounds を使用する。
- base water → base land → sparse coastal water → sparse coastal land の painter order を維持したまま、同一 terrain material の `SetPass(0)` を1回へ統合する。
- triangle-list / line mesh の identity index 配列を vertex count ごとに再利用する。
- uniform water colour upload 用 `Color32[]` scratch を vertex count ごとに再利用する。
- 上記効果を `oh_bounds_skip`, `oh_setpass_saved`, `oh_identity_index_hit/miss`, `oh_uniform_colour_reuse` で観測する。

## 変更禁止 authority
- 33x33 base terrain
- 129x129 coastal authority / sparse coastal correction
- Candidate11 contour 96-level budget / HD coastal clip
- REL/TOPO palette semantics
- ARGB32 / Bilinear render target
- 0.10 s BACK cadence
- Terrain FRONT swap synchronized ownship/prediction/range fan
- Candidate8 painter order

## 判定
静的/prebuild合格後も runtime 未確認として扱う。Candidate11/Pass2同等の見た目、ゲッタン非再発、GPU faultなしを実機で確認するまでGitHub Releaseへ登録しない。
