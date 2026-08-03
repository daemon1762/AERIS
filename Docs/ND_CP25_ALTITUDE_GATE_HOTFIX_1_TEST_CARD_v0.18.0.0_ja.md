# ND CP2.5 Altitude Gate Hotfix 1 実機テストカード

## 必須ログ

`AERISFlightControl.log`と`KSP.log`を保存する。ログ中の`[CP2.5/TERRAIN_ACTIVATION]`遷移を確認する。

## 試験

1. 39,500m未満でNDを表示し、Terrain・海岸線・滑走路・空港が通常表示されること。
2. 上昇し40,500mへ到達した瞬間にND全体が消え、古いTerrain textureやrunway表示が残らないこと。
3. 40,500m以上でFDI、AP、AA、PROTECTが通常どおり動作すること。
4. 40,500m以上でPreload statusの件数・進捗・PROMOTE処理が停止しないこと。
5. 40,500mから降下し、39,500m以上の間はNDがOFFのまま保持されること。
6. 39,500m未満でNDが再表示され、現在位置のTerrainを再構築し、上昇前位置の古いframeを表示しないこと。
7. 境界付近を往復し、39.5–40.5kmのヒステリシスによりON/OFFが連打されないこと。
8. Scene遷移、機体切替、天体変更後にNDが誤って残留しないこと。

## 合格条件

- OFF境界：ASL 40,500m以上
- ON境界：ASL 39,500m未満
- 中間帯：直前状態保持
- OFF中のND/Terrain/Runway/Airport/LAND/GPU更新：停止
- OFF中のPreload Builder：継続
- FDI/AP/AA/PROTECT：機能退行なし
- 例外、stale commit、表示残留、境界チャタリングなし
