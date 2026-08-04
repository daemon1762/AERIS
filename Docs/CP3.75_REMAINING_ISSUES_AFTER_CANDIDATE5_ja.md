# CP3.75 Candidate5 後の残存課題

## 1. High-Density Coastline Preload
Candidate3/4で線幅artifactとサブセル交点は修正済みだが、33x33 terrain sampling由来の海岸線形状の段々は残る。地形本体を高密度化せず、海岸線だけをadaptive extractionした高密度geodetic vectorとしてpreloadする案が次の本命。

## 2. GPU coverage瞬間低下
Candidate4実機ログでは通常coverage=1.0だが、ごく短時間0.4〜0.99へ落ちる瞬間が残る。viewport論理coverageは1.0のため、GPU tile availability / commit timingを優先調査する。

## 3. 高速域の残存FPS低下
Candidate2でforced recoveryの速度比例暴走は解消したが、速度上昇に伴うframe time増加は残る。ND ON/Terrain ON、ND ON/Terrain OFF、ND OFFの3条件でAERIS由来とKSP由来を分離する。

## 4. Factory Terrain Seed
全天体100%生成済みpreload DBの標準同梱は後回し。将来はlive writable DBを上書きしないread-only Factory Seed層として検討する。

## 5. Candidate5 runtime sign-off
10/20/40/80/160 kmだけが選択されること、旧5 km設定/profileが10 kmへ移行すること、RGで海だけが濃い青へ切り替わりSTD/BY/HIGHが不変であることを実機確認する。
