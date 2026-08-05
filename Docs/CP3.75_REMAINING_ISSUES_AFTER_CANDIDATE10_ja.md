# CP3.75 Candidate10 後の残存課題

## 実機確認必須
- Candidate10識別のDLLが実際にロードされること。
- 島嶼・急峻海岸で短い平行線束/矩形状等高線が消えること。
- HD coastlineとSparse fillの一致がCandidate9水準を維持すること。
- 20/40/80/160 kmでCP3後期品質下限を維持すること。

## 横線状ノイズ
録画データへ残らないため現段階では最終scan-out tearing/表示同期を第一候補とする。AERIS内部RenderTextureのfaultは添付Candidate8ログでは確認されていない。Candidate10実機で再現条件を分離し、ND領域限定か画面全体か、VSync/refresh rateとの相関を確認する。

## 注意
2026-08-05 12:30頃の添付試験データは、ログ・DLL・画面表示の全てがCandidate8でありCandidate9実機試験ではない。
