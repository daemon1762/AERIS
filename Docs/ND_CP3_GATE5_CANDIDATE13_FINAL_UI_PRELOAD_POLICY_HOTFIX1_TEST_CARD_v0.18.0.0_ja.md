# AERIS v0.18.0.0 CP3 Gate 5 Candidate 13 — Final UI / Preload Policy Hotfix 1 実機テストカード

## 目的
最終実機試験前に、PRELOAD / PROTECT / SYSTEM / AIRFIELDS のユーザー向けUIと運用方針を固定する。

## PRELOAD
- PRELOAD MAPSには `AUTOMATIC PRELOAD` のON/OFFだけが設定項目として存在すること。
- Mode / Speed / Storage / Idle / Priority / Quality / BodyCap の変更UIが存在しないこと。
- 各天体には天体名、進捗%、`BUILD`、`PAUSE/RESUME`、`DELETE`、`REBUILD` だけが表示されること。
- PRELOADの状態、DB、queue、throughput、worker、backpressure等のデバッグ数値が表示されないこと。
- ON時は従来採用済みデフォルト動作で自動プリロードし、OFF時のみ自動プリロードを停止すること。
- 大容量生成時に旧2 GiB等の容量上限で停止・削除されないこと。

## PROTECT
- 旧設定ファイルから初回起動した場合も Parking Hold と Reverse Thrust Auto がONへ移行すること。
- その後ユーザーが明示的にOFFにした場合は次回起動でOFFが維持されること。
- PROTECTページにAoA、速度、減速度、レーダー高度等のライブデバッグ数値が表示されないこと。

## SYSTEM
- DIAGNOSTICSページが存在しないこと。
- Resident / Predictive Corridor / Map DRAM / ND EMA / worker等の内部テレメトリ表示が存在しないこと。
- 通常のユーザー設定、AIRFIELDS、PRELOAD MAPSは操作できること。

## AIRFIELDS
- Making History導入時、Dessert Airfield RWY 36/18が `VANILLA RUNWAYS` に表示されること。
- Making History未導入時はDLC滑走路が一覧・LAND選択・NDから非表示であること。
- MOD空港はprovider未導入時に引き続き非表示であること。
- Dessertの位置・方位はfield-verifiedデフォルトから変化していないこと。

## 最終CP3実機試験（ユーザー定義）
1. KerbinでND 160 km、極超音速で1周し、NAV/ND reloadが3回以内。
2. Laytheへ遷移し、高度10,000 m付近でNDがLaythe地形へ正常切替・復帰すること。

CP3 CLOSEは上記を含むruntime結果確認後に判断する。
