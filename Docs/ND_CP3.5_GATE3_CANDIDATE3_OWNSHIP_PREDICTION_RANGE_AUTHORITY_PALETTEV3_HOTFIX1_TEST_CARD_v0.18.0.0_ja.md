# CP3.5 Gate 3 Candidate 3 Authority / Palette V3 Hotfix 1 テストカード

## 目的
Candidate 3 runtimeで確認された「自機アイコン／予測線が描画距離に応じて不正移動する」回帰を除去し、Palette V3の識別性を確認する。
本HotfixはGate 4のFPS／真の地形解像度改善を合格対象にしない。

## 最重要試験
1. KSC周辺でTRACK UP、Terrain ON、Track Vector ON。
2. 160 → 80 → 40 → 20 → 10 → 5 km と順に切替。
3. 各rangeで直進・旋回を行う。
4. 自機シンボルが常に通常ND anchorに固定されること。
5. 予測線の始点が常に自機で、tickも同じ線上を維持すること。
6. range変更直後に旧range FRONTが新rangeの縮尺として表示されないこと。
   新FRONT未完成時に一時的に地形がBUILDINGになるのは、誤縮尺FRONT表示より安全側として許容する。

## PLAN確認
PLANへ切替えた場合のみ、自機は地理座標に従って表示されること。

## Palette V3
TOPOとRELの双方で Standard / RedGreenAssist / BlueYellowAssist / HighContrast を順に切替。
- 4profileが一目で区別可能であること。
- HighContrastは最も明暗差が大きいこと。
- 海面は安定したdeep blueを維持すること。
- 海岸付近の低い陸地が海底高度の影響で同系色へ潰れないこと。

## 非合格対象（次Gate）
- Terrain ON/OFFのFPS差の大幅縮小。
- 60fps相当presentation。
- 160kmでの真のRoute/Local級地形情報量。
これらはGate 4 Unified ND World Surfaceで扱う。
