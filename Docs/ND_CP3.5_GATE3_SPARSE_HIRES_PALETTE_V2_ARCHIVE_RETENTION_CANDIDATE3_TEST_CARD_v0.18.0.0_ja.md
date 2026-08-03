# Candidate 3 実機試験カード

1. KSC周辺でND ON / TERRAIN ON。20/40/80/160kmを順に確認し、青一色化・2FPS級thrash・BUILD/EVICTループがないこと。
2. 160km HIGHで海岸線をCandidate 2/AERIS59と比較。GeometryはFAR Foundationのままなので山岳面の完全Hi-Res化は要求しないが、海岸線の階段位置と輪郭の見栄えが改善すること。
3. STD -> RG -> BY -> HIGHを切替。旧色混在、黒潰れ、白飛び、海陸識別不能がないこと。
4. 同一飛行条件で ND ON/Terrain ON -> Terrain OFF -> ND OFF を各15～20秒保持しFPS/ログを記録。Candidate 2比でExact-only時の余分なtemporal shadow負荷が減っていること。
5. SYSTEM > OPTIONSのFDR/CVR Archive limitを1、10、30へ変更し再起動後も保持すること。
6. retention試験はテスト用ZIPで行い、verified markerを持つ正式ZIPだけが最古順に削除され、raw folder / .zip.tmp / unverified ZIPが残ること。
7. AUTOPILOTのLANDが残り、Terrain qualityはAUTO/LOW/MEDIUM/HIGHだけであること。

ログ提出時は AERISFlightControl.log/KSP.log、動画、FPS比較区間を添付する。
