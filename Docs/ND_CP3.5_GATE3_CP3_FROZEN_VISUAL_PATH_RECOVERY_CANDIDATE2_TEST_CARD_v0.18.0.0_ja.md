# Gate 3 Candidate 2 実機テストカード

1. まずビルドが通ること。
2. Kerbin、ND ON、TERRAIN ON、160 kmで30秒。開始不能/2 FPS化/BUILD-EVICT再生成ループがないこと。
3. 可能なら約2000–2100 m/sで30–60秒。CP3凍結版相当の地形・海岸線・等高線品質を確認。
4. ND ON→Terrain GPU OFF→ND OFFを各15–20秒。FPS差を記録。
5. STD→RG→BY→HIGHを切替。黒潰れ、白飛び、旧色混在がないこと。
6. 5/10/20/40/80/160 kmを切替。Route/Local exactが存在しない領域でもFAR/virtual detailで連続表示し、missing exactの大量生成を起こさないこと。
7. ログで pending、terrain_gpu_bytes、nd_repaint_ema_ms、temporal confidence、front BUILDING継続時間を確認。
8. UIは最小/中/最大サイズで文字切れ・自動改行・左右非対称余白がないこと。

FAIL: GPU/CPU resource thrash、FRONT BUILDING長時間停滞、ND repaint連続10ms超、地形欠落、線状ちらつき、アクセシビリティ異常。
