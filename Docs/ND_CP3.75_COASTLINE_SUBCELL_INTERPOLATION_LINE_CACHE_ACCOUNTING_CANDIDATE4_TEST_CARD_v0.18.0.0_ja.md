# AERIS v0.18.0.0 CP3.75 Candidate 4 Runtime Test Card

対象: `DEV CP3.75 — COASTLINE SUB-CELL INTERPOLATION / LINE CACHE ACCOUNTING CANDIDATE 4`

## 主目的

Candidate 3 で均一線化した海岸線について、land/water sample 間の境界を固定38%ではなく water 判定閾値 1 m の実高度交点で補間し、160 km の階段状 coastline geometry を軽減する。

## 推奨条件

- HHC4
- Kerbin
- 高度 約30,000 m
- 速度 約2,100 m/s
- RANGE 160 km
- Terrain Quality: LOW (LOCKED)
- ND presentation: internal fixed 10 Hz

## 観察項目

1. 海岸線の線幅は Candidate 3 と同じ均一な細線である。
2. 同じ沿岸を比較し、直角的・固定位置的な階段感が Candidate 3 より軽減する。
3. 海岸線が land fill と water fill の境界から離れない。
4. 島・入り江・細い沿岸部で land/water bleed や線の飛びがない。
5. 20 km / 160 km、TRK UP / NORTH UP で地理整合を維持する。
6. 高速域で `forced_recovery` が増殖しない。
7. blank / blue-only terrain、例外、クラッシュがない。

## 判定

海岸線の均一線を維持しつつ中心線形状が改善し、Candidate 2/3 の安定化を退行させなければ PASS。
