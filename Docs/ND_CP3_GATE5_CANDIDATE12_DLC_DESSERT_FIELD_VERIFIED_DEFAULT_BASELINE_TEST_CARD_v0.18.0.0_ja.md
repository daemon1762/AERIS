# AERIS v0.18.0.0 CP3 Gate 5 Candidate 12 実機テストカード

Making History の Dessert Airfield を、2026-08-02 に実機で取得した A/B 絶対測地座標を唯一の滑走路Authorityとしてデフォルト化したことを確認する。

- Making History導入時、SYSTEM > AIRFIELDS に Dessert Airfield が通常滑走路として表示される。
- `RWY -- / GEOMETRY REQUIRED` ではなく `RWY 36/18` が表示される。
- RWY 36 = A→B、heading 約 0.966390893°。
- RWY 18 = B→A、heading 約 180.966390893°。
- 認証Basisは manual/UserCalibrated 系であり、DLC自動認証へ変化しない。
- ND位置が物理滑走路と一致し、追尾・浮動しない。
- LANDは明示選択までNONE。
- Making History未導入時はDessertをUI/ND/LANDから非表示。

A: `-6.5996178022817782, -144.04085510339206, 822.713678324013 m`
B: `-6.4482987102736748, -144.0382863593405, 822.6041791010648 m`
Coordinate frame: `BODY_FIXED_GEODETIC_ABSOLUTE`
