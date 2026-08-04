# CP3.75 Candidate5 実機テストカード

## 1. Build identity
起動UIに `DEV CP3.75 — ND RANGE CONSOLIDATION / RG SEA CONTRAST CANDIDATE 5` が表示されること。

## 2. ND range
RANGE操作を一巡し、表示可能な値が `10 / 20 / 40 / 80 / 160 km` の5段階だけであること。
5 kmがボタン、ホイール、profile復元のいずれからも出現しないこと。

旧5 km設定/profileを持つ環境では初回読込後10 kmへ移行すること。

## 3. RG sea colour
SYSTEM > OPTIONS > Terrain coloursでRGを選択する。

- 海のみCandidate4より明確に濃い青になること。
- 陸地色は従来RGのまま。
- coastline / contour / runway / symbolsに色変更がないこと。
- RG→STD→RG切替が即時反映され、preload/rebuildを要求しないこと。

## 4. Candidate4 regression
20 kmおよび160 kmで海岸線が均一線幅を維持すること。青一色、blank、短冊状coastline artifactを再発しないこと。
高速時に`forced_recovery`が速度比例で増殖しないこと。

## 5. 判定
Candidate5の合否はrange整理とRG海色の正しい限定変更で判定する。海岸線33x33由来の段々、GPU coverage瞬間低下、速度依存FPSは別課題であり本CandidateのFAIL条件にはしない。
