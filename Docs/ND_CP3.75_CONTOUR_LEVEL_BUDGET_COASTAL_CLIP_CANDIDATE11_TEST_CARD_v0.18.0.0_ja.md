# CP3.75 Candidate11 実機試験カード

## 0. 版確認
AERIS上部に `DEV CP3.75 — CONTOUR LEVEL BUDGET / COASTAL CLIP CANDIDATE 11` が表示されること。
表示が違う場合、その試験結果はCandidate11判定に使用しない。

## 1. 等高線倍率試験
Candidate10で崩れが見えた急峻な島・海岸を可能な限り同じ航路／視点で追う。
RANGEを 160 → 80 → 40 → 20 → 10 km の順に変更する。

確認項目:
- 急斜面で低高度側だけに短い等高線が束状集中しない。
- 高高度側の等高線が途中で一斉に消えない。
- 海岸沿いに四角い／セル状の等高線欠落が出ない。
- 等高線が海上へ明確にはみ出さない。
- 島の輪郭と等高線の関係が倍率変更で急変しない。

## 2. Contours ON/OFF A/B
同一地点・同一RANGEで `Terrain contour lines` をOFF→ONする。
OFFで消える異常はcontour系として記録する。
OFFでも残る色・塗り異常はterrain fill/relief系として別件化する。

## 3. 海岸線／fill継承
Candidate9系の修正が維持されていること。
- 129x129 HD coastlineが鮮明。
- land fillが海岸線を大きく越えない。
- High Contrast + AUTO→RELで安全域がシアン化しない。
- preset切替後に古いFRONT色が残り続けない。

## 4. 性能
KSC周辺と海上飛行でFPSを確認する。
Candidate8-10相当を許容範囲とし、Candidate7級（約2 FPS）へ落ちた場合は即FAIL。

## 5. ちらつき監視
前回の横線状ちらつきは現時点で再現性なし。
再発した場合のみ、次を記録する。
- ND矩形内だけか、KSP画面全体か
- 静止中にも出るか
- スクリーン録画に写るか
ちらつき非再発だけを理由にCandidate11をFAIL/PASSにはしない。

## 合格条件
上記1〜4がPASSし、新しい重大回帰が無いこと。
この条件を満たした場合、CP3.75 ND基礎描画構造の完成判定候補とする。
