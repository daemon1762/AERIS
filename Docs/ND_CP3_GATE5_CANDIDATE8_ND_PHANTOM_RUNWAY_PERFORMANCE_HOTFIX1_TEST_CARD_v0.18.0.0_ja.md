# AERIS v0.18.0.0 CP3 Gate 5 Candidate 8
## ND Phantom Runway / Performance Hotfix 1 — Runtime Test Card

### 目的
Candidate 7で確認された「ND上に本来存在しない滑走路が突然現れ、追尾する」現象を再発させないことを確認する。
同時に、NDの表示品質を一切落とさず、NDおよびAERIS全体の主スレッド負荷を軽減できていることを確認する。

### 必須前提
- KSP 1.12.5を完全終了してからCandidate 8を導入する。
- ゲーム内タブ表示が `DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 8 — ND PHANTOM RUNWAY / PERFORMANCE HOTFIX 1` であること。
- Candidate 6のField-Verified Runway Default Baselineを維持する。
- 起動時の空港・滑走路選択はNONE。
- CPU terrain presentationは禁止のまま。
- GUI.matrixを用いた時間ワープ型地形再投影は禁止のまま。

### Test 1 — 基本表示品質
1. KSC周辺でNDを表示する。
2. 5 / 10 / 20 / 40 / 80 / 160 kmを順番に切り替える。
3. TRACK UP / NORTH UP / PLANを確認する。
4. TOPO / REL、海岸線、等高線、滑走路、選択滑走路表示を確認する。

合格:
- Candidate 7以前と比較して地形解像度、輪郭、海岸線、色、滑走路形状に視覚的劣化がない。
- 黒欠け、CPU代替地形、terrain warpが発生しない。
- FAR基盤未完成時は従来どおり完全なGPU FRONTをラッチして表示する。

### Test 2 — Phantom Runway Regression
1. 滑走路選択をCLEAR/NONEにする。
2. KSCから離陸し、TRACK UPで飛行する。
3. ND範囲を複数回変更し、旋回・直進を繰り返す。
4. 地形FRONT更新中にもND上の空白部、海上部、滑走路付近をクリックする。
5. PLANへ入り、ドラッグ・RECENTER・通常ND復帰も行う。
6. 10分以上継続する。

合格:
- 画面上に描画されていない滑走路をクリックしてもpreview/SELECT候補にならない。
- 明示的にSELECTしていない滑走路の画面端ポインタが突然現れない。
- CLEAR後に滑走路ポインタが追尾し続けない。
- 明示的にSELECTした滑走路については、画面外に出た際の正規edge pointerが従来どおり動作する。

### Test 3 — Projection Consistency
1. TRACK UPで連続旋回しながらFAR FRONT更新境界を通過する。
2. 滑走路の線・端番号・地形・自機位置を観察する。

合格:
- 滑走路と地形の相対位置がFRONTラッチ中も一致する。
- Runway click/preview対象と実際に見えている滑走路が一致する。
- 「地形は旧FRONT、click判定は新requested view」の分離が再発しない。

### Test 4 — Preload Load Shedding
1. NDを表示したまま、地形プリロードが未完了の区間を300 m/s級で飛行する。
2. PRELOAD / CP3 telemetryを記録する。
3. 高負荷時と低負荷時の `preload_builder_pqs_ms` を比較する。

合格:
- フレーム/ND負荷が高い時、background PQS workが自動縮退する。
- 負荷が下がれば自動的に通常budgetへ復帰する。
- 生成されるタイルのLOD・解像度・最終データ品質は変化しない。
- 視界内FAR基盤とLAND安全側処理をbackground preloadが阻害しない。

### Test 5 — Map DRAM / Logging
ログで `[CP2.5/MAP_DRAM] domain=TERRAIN_INDEX` を確認する。

合格:
- INDEX_COMMITごとの大量INFO出力がなく、routine commitは概ね64回単位で集約される。
- STARTUP / invalidation / reindex等の非routine原因は引き続き即時記録される。
- Map DRAM lookupはDRAM-onlyのまま。
- 同期ディスクlookup違反が0。

### Test 6 — Long Run
最低30分、可能なら従来と同等の長時間飛行を行う。

確認項目:
- Phantom runway再発なし。
- ND repaintの継続悪化なし。
- PRELOAD PQSによる周期的な大スパイクがCandidate 7より軽減。
- Terrain/Runway presentationの品質低下なし。
- PROTECT / AP / LANDの既存機能に回帰なし。

### 提出物
- `AERISFlightControl` ログZIP
- `KSP.log`（異常がある場合）
- performance runtime CSV
- Phantom regression中の動画またはスクリーンショット

### Gate判定
Candidate 8はSTATIC PASSだけではCP3 CLOSE不可。
Native build + KSP runtimeで上記を完走後にGate 5 closure判定へ進む。
