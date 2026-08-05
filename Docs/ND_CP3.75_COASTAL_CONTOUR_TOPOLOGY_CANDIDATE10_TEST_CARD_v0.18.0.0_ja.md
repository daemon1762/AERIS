# CP3.75 Candidate10 — Coastal Contour Topology Test Card

## 目的
Candidate8実機動画で確認された海岸付近の等高線崩壊を除去し、Candidate9の海岸境界・REL palette authorityを継承した上で基礎描画構造を再評価する。

## 修正対象
- 四角セルの4交点曖昧性を廃止し、地形meshと同一の2三角形単位で等高線を生成する。
- 129x129 HD land/water境界が通るcoarse parent cellでは粗い等高線を抑止する。
- Sparse coastal fill、HD coastline、33x33 FAR baseの構造は維持する。

## 実機試験
1. 起動画面の識別が `COASTAL CONTOUR TOPOLOGY CANDIDATE 10` であることを確認する。
2. KSC周辺から島嶼部を含む20/40/80/160 kmを確認する。
3. 海岸付近に短い平行線束、矩形状の等高線、海面へ侵入する等高線がないこと。
4. 内陸の等高線が途切れず、急斜面では自然に密になること。
5. 海岸線/land-water fillの一致がCandidate9要件を維持すること。
6. FPSがCandidate8水準から有意に悪化しないこと。

## 横線ノイズの分離試験
録画フレームへ残らない横線状ノイズはscan-out tearing候補として別管理する。同じ場面で以下を記録する。
- KSP FPS
- モニターrefresh rate
- VSync ON/OFF
- ND表示時のみか画面全体か
- OBS/録画フレームに残るか

録画へ残らず物理画面だけに出る場合、Candidate10ではterrain RenderTextureの幾何を変更せず表示同期系として切り分ける。
