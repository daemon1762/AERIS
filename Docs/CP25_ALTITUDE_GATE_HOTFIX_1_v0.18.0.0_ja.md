# AERIS v0.18.0.0 CP2.5 Altitude Gate Hotfix 1

## 目的

CP2.5 Track Aの最初の独立変更として、航空用Navigation DisplayのTerrain viewportだけを海抜高度で停止する。滑走路A/B絶対測地デフォルト化（Track B）は保留し、本Hotfixへ混在させない。

## 中央Activation Policy

- 海抜40,500m以上：OFF
- 海抜39,500m未満：ON
- 39,500m以上40,500m未満：直前状態を保持
- 初回評価がヒステリシス帯内の場合：40,500m未満としてONから開始
- 非Flight、Active Vesselなし、天体なし、ASL非有限値：OFF

判定は`AERISTerrainViewportActivationPolicy`へ集約し、`AERISTerrainAwareness.Tick()`で一度だけ評価する。

## OFF対象

- NDパネル全体
- Terrain、Runway、Airport、LANDのND描画
- Flight viewport tile planning / disk-read要求 / FlightFallback PQS要求
- CPU terrain raster
- GPU tile mesh準備、Mesh、Material、RenderTexture
- Navigation/Traffic prepared snapshot、trail、wind、preview state
- 旧PQS grid samplingとterrain alert評価

OFF遷移時は公開済みPQS gridも破棄し、再開時に移動前の地形が一瞬再表示される経路を閉じる。

## OFF対象外

- Preload BuilderとPreload Database進行
- FDI本体
- FlightState
- AP / AA / PROTECT
- Landing foundationの観測処理（ND上のLAND描画のみ停止）

## 再開

39,500m未満へ降下するとviewport generationを更新し、表示キャッシュを空の状態から再要求する。40,500mから39,500mまでの降下中はOFFを保持する。

## 互換性

CP2の凍結表示識別子は`Cp2FrozenBaselineDisplay`として残し、既存CP1/CP2回帰受入を維持する。現行`Display`はCP2.5 Altitude Gate Hotfix 1を示す。
