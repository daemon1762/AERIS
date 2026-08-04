# CP3.75 Candidate6 後の残存課題

1. High-density coastline line と 33x33 land/water fill の局所的な境界差が実機で目立つ場合、fill authority統合を検討する。
2. 33x33 candidate scanが完全に見落とす微小島・細い地峡の発見は未対応。必要ならadaptive global coastline discoveryを追加する。
3. ごく稀なGPU coverage dropはCandidate6の直接修正対象外。
4. 高速域で残る全体FPS低下はND ON/Terrain ON、ND ON/Terrain OFF、ND OFFの3条件で分離測定する。
5. Factory Terrain Seed標準同梱は後回し。
