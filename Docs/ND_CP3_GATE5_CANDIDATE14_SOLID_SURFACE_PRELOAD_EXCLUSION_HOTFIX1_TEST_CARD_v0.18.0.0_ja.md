# Candidate 14 実機テストカード — Solid-Surface Preload Exclusion Hotfix 1

1. KSPを起動しPRELOAD画面を開く。
2. SunおよびJoolが自動プリロード対象/天体一覧に出ないことを確認する。
3. Kerbin、Mun、Minmus等のPQS地表天体は従来どおり表示・進行することを確認する。
4. Laythe等の固体地表天体への遷移後、ND terrainが従来どおり動作することを確認する。
5. MOD天体がある場合、PQS地表なし天体は自動除外、PQS地表あり天体は対象になることを確認する。
6. Candidate 13のPRELOAD ON/OFFのみ、%のみ、4操作ボタンのみのUI契約が維持されることを確認する。

合格条件: surface-less bodyへのterrain generation/preloadが発生せず、solid-surface bodyの既存機能に回帰がないこと。
