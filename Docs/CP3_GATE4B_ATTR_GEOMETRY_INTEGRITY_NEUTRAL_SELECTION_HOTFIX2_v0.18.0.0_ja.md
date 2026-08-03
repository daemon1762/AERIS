# CP3 Gate 4B ATTR Geometry Integrity & Neutral Selection Hotfix 2

## 目的
- 実機で崩壊した GUI.matrix temporal reprojection を表示authorityから隔離する。
- ND Terrainは現在projectionでGPU描画された完全FAR FRONTだけを表示する。
- CPU terrain draw/fallbackは復活させない。
- 新規フライト/registry初回commitは空港NONE・滑走路NONEで開始する。
- 保存値が空/不正な場合に最初の空港・最初の滑走路を自動選択しない。

## 既知のトレードオフ
ATTR temporal表示は安全のため一時停止。急旋回中はGPU BACK再描画頻度がGate4B rejected版より増える可能性がある。正確性を性能より優先する。
