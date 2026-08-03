# Gate 5 Candidate 3 実機テストカード

重点試験は通常飛行中のgeneration rollover。

1. TRACK UP / Terrain AUTO / 80 kmで250–350 m/sを5分以上飛行する。
2. 160 kmへ切替え、同条件で5分以上飛行する。
3. 360°以上の連続旋回を行う。
4. 画面に一瞬でも黒抜けが出ないことを確認する。
5. 滑走路・空港・ownship・trailがTerrainから浮遊しないことを確認する。
6. 5/10/20 kmでは滑走路端番号、40 km以上では端番号/tickが非表示であることを確認する。
7. Terrain OFF -> AUTOを1回実施。再構築中は青系standbyで、黒背景にならないことを確認する。
8. ログで `cpu_terrain_draw=0`、`ready_build_violation=0` を確認する。
9. `gen_bridge_frames`が増えること自体は正常。`front=LATCHED`も短時間なら正常。
10. `latch_age`は8秒以下。AERIS ERROR/Exceptionは0。

上記重点試験合格後、Gate 5フルマトリクスを継続する。
