# ND CP2 Alignment Diagnostic Hotfix 1 実機試験カード

対象パッケージ：

`AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip`

## 0. 合否の前提

- 初期値は`TERR Y DIRECT`
- `TERR Y FLIP`は比較診断用。比較後は`DIRECT`へ戻す
- 旧設定ファイルを引き継いだ場合も、MENU表示で現在値を必ず確認する
- 動画録画を開始してから試験する
- 試験後は`AERISFlightControl.log`、最新session log、performance CSVを提出する

## 1. デスクトップ：検証・ビルド・導入

```bash
cd ~/Downloads
sha256sum -c AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip.sha256
rm -rf AERIS-v01800-cp2-alignment-diagnostic-desktop
mkdir AERIS-v01800-cp2-alignment-diagnostic-desktop
unzip -q AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip -d AERIS-v01800-cp2-alignment-diagnostic-desktop
cd AERIS-v01800-cp2-alignment-diagnostic-desktop/AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_cp2_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

## 2. ノートPC：検証・ビルド・導入

```bash
cd ~/Downloads
sha256sum -c AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip.sha256
rm -rf AERIS-v01800-cp2-alignment-diagnostic-laptop
mkdir AERIS-v01800-cp2-alignment-diagnostic-laptop
unzip -q AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source.zip -d AERIS-v01800-cp2-alignment-diagnostic-laptop
cd AERIS-v01800-cp2-alignment-diagnostic-laptop/AERISFlightControl-v0.18.0.0_DEV_CP2_AlignmentDiagnosticHotfix1_Source
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_cp2_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

ビルドエラーが出た場合はソースを手動修正せず、端末出力をそのまま提出する。

## 3. 起動確認

1. KSPを起動
2. AERISウィンドウの表示が次であること
   - `AERIS Flight Control v0.18.0.0 DEV CP2 ALIGNMENT DIAGNOSTIC HOTFIX 1`
3. NDを開く
4. `MENU`を開く
5. `TERR Y DIRECT`であることを確認

## 4. 地形位置一致試験

### 推奨経路

KSCからIsland Airfieldへ、島を目視できる高度・距離で飛行する。自動操縦の使用有無は問わないが、地形評価中は急激なロールを避ける。

### 操作

1. `TRACK UP`
2. `TERR TOPO`または`TERR REL`
3. レンジを順に変更
   - `5 → 20 → 160 → 10 → 40 → 80 → 160 km`
4. 地形modeを順に変更
   - `TOPO → REL → OFF → AUTO → REL`
5. Island Airfieldへ直進し、実景の海岸線・島・滑走路との位置関係を見る
6. 島上空を通過する

### DIRECT合格条件

- 島が画面内を逃げ続けず、自機直下へ到達する
- 滑走路シンボルと地形上の島の相対位置が整合する
- 実景で島上空へ到達した時、NDでも自機記号が島上にある
- range／mode変更後も同じ地理位置へ復帰する
- `[ND/TERRAIN_ALIGN] ... deltaPx=`が自機追従状態で概ね`0,0`付近

### 比較診断

1. 水平飛行中に`MENU → TERR Y FLIP`
2. 10～20秒だけ観察
3. 地形が上下方向に明確にずれるか、`deltaPx`のYが増えるか確認
4. 必ず`TERR Y DIRECT`へ戻す

`FLIP`の方が明確に正しい場合は、DIRECTを正しいと決めつけず、両状態の動画と`[ND/TERRAIN_ALIGN]`ログを提出する。

## 5. 空港選択解除試験

1. ND上でIsland Airfieldの滑走路をpreview
2. `SELECT`
3. 選択中にND右上へ`CLR SEL`が表示されること
4. `ARM OBS`でLAND観測をARM
5. `CLR SEL`を押す

合格条件：

- LAND観測がDISARMされる
- 選択空港・滑走路が消える
- `CLR SEL`ボタン自体が消える
- preview／LAND表示が残留しない
- AP／AA／MASTERへ副作用がない
- ログに`[AIRFIELD_SELECTION] cleared from ND`と`[ND/LAND] CLR SEL pressed`が出る

### 永続化確認

1. Space Centerへ戻る、またはKSPを再起動
2. Flightへ戻る
3. 明示解除した空港が勝手に再選択されないこと
4. 新たに空港を選択できること

## 6. LOC平面漏斗持続試験

1. Island RWY 09またはKSC RWY 09を選択
2. `ARM OBS`
3. LOC中心線と左右漏斗線を確認
4. レンジを5、20、40、80、160kmで切り替える
5. TRACK UP／NORTH UPを切り替える
6. 滑走路端や漏斗遠端が画面外になる位置まで飛行する

合格条件：

- 線分がND表示域と交差する限り、LOC中心線と漏斗線が残る
- 滑走路端が画面外へ出ただけで表示全体が消えない
- XTK表示が不意に消えない
- 選択解除またはLAND DISARMまでは選択進入形状を維持する

## 7. GS縦断漏斗試験

1. LAND観測をARM
2. GS縦断欄を確認
3. GSより大幅に高い状態、低い状態、捕捉付近を通る

合格条件：

- 中心GS線が常時見える
- 上下漏斗境界と遠端capが見える
- 機体高度が高くてもGS形状が下端へ押し潰されない
- 機体記号だけが表示端へ寄り、GS基準形状は維持される

## 8. coverage診断

性能CSVと`[ND/TERRAIN_ALIGN]`を確認する。

合格条件：

- fallbackで画面が埋まった場合、`visualCoverage=1.000`は許容
- sampling／要求Tileが残る間、`requestedCoverage`が偽の1.000へ張り付かない
- Final完成時に`requestedCoverage`が1.000へ到達する

## 9. 提出物

- 試験動画
- `GameData/AERISFlightControl/Logs/AERISFlightControl.log`
- 最新の`Logs/Sessions/*_session.log`
- 最新の`Logs/Sessions/*_performance_runtime.csv`
- 使用した`Config/AERISSettings.cfg`
- DIRECTとFLIPのどちらが地形位置一致したか

## 10. 今回の合否対象外

記録はするが、このパッケージ単独のFAIL理由にはしない。

- 約45秒周期の`stale_cancelled`増加
- 接地後の誤liftoff／Ground ARM AP解放
- Airfield Snapshotの単発長時間slice
- Tile境界とLOD popの最終品質

これらは地形alignment結果を確認後、別Hotfixとして議論する。
