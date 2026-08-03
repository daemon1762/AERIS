# CP3 Gate 4B ATTR Presentation Recovery Hotfix 1 実機テストカード

## 必須

- タブ表記が`DEV CP3 GATE 4B — ATTR PRESENTATION RECOVERY HOTFIX 1`。
- `cpu_terrain_draw=0`。
- `[CP3_GATE4B_READY_BUILDING_VIOLATION]`が0件。
- `ready_gf == required_gf`かつ`back_foundation=1.000`になった状態で`TERRAIN GPU BUILDING`が1秒以上継続しない。

## 飛行

1. 250～350m/sで直進を60秒以上。
2. TRACK UPで左右各360°の連続旋回。
3. 5/10/20/40/80/160kmを順方向・逆方向に変更。
4. 80kmまたは160kmで小刻みなheading変化を継続。
5. NDをOFF→ON、高度ゲートOFF→ON相当の遷移を確認。

## 合格

- 安定飛行中の黒画面／`TERRAIN GPU BUILDING`反復が0。
- FARが真に未準備な初回build以外、完成foundationを待ちながら表示不能にならない。
- `forced_recovery`は必要時のみ増加し、その直後にFRONT presentationが復旧する。
- `ready_build_violation=0`。
- GPU failure=0、同期SSD read=0、CPU terrain draw=0。
- TRACK UP旋回中に恒久的な黒扇形欠落がない。
