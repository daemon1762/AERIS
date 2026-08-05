# Candidate9後の残存課題

Candidate9はND基礎描画構造の完成候補。以下は実機試験で最終判定する。

1. KSC 20 kmでHigh Contrast + AUTO→RELの安全色が赤/黄/緑/暗緑になること。
2. 20/40/80/160 kmでHD coastlineとSparse fillが目視一致し、帯状・三角形・矩形の越境が無いこと。
3. 256-parent railへ戻したことによるFPS影響がCandidate8許容範囲内であること。
4. `coast_hd_entries` に対する `coast_sparse_entries` の改善を確認すること。複雑海岸tileで256を超える場合は意図したfallbackとして扱う。
5. 稀なGPU coverage drop、高速域の残存FPS低下、Factory Terrain Seed同梱は基礎構造完成後の別課題とする。

上記1〜3がPASSし、新規のブラックアウト・追従・投影回帰が無ければCP3.75 ND基礎構造を完成扱いとする。
