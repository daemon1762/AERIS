# AERIS v0.18.0.0 DEV CP2 — Preload Terrain実KSP試験カード

## 0. 試験対象

対象パッケージ：

`AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip`

これはCP2検査版であり、正式版、新NAV、LAND自動制御ではない。

最初の試験では既存の`TerrainPreloadDatabase`を削除しない。旧CP2の`TerrainCache`が残っていてもよいが、主供給は新DBである。

## 1. SHA・静的受入・ビルド

### デスクトップPC

```bash
cd ~/Downloads
sha256sum -c AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip.sha256
mkdir AERIS-v01800-cp2-frc1-desktop-verify
unzip -q AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip \
  -d AERIS-v01800-cp2-frc1-desktop-verify
cd AERIS-v01800-cp2-frc1-desktop-verify/AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_cp2_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

### ノートPC

```bash
cd ~/Downloads
sha256sum -c AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip.sha256
mkdir AERIS-v01800-cp2-frc1-laptop-verify
unzip -q AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source.zip \
  -d AERIS-v01800-cp2-frc1-laptop-verify
cd AERIS-v01800-cp2-frc1-laptop-verify/AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_cp2_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

ビルドエラーが出た場合はそこで停止し、端末出力をそのまま提出する。手動修正しない。

## 2. 起動確認

1. KSPを起動する。
2. ログで`DEV CP2 FIELD RENDER CONSISTENCY HOTFIX 1`を確認する。
3. Main Menu、Space Center、VAB、SPH、Tracking Stationを順に通る。
4. 例外spam、scene移動停止、黒画面がないことを確認する。
5. AERISアイコンを押し、非Flightでは読み取り専用の`PRELOAD TERRAIN STATUS`画面になることを確認する。

不合格：起動直後の同期DB全走査、長時間UI停止、同じ例外の連続出力。


## 2A. FMJ方式Toolbar／非Flight Status

次のsceneを順に通り、各sceneで確認する。

- Main Menu
- Space Center
- VAB
- SPH
- Tracking Station
- Flight

合格条件：

- 同じAERIS 38×38アイコンが1個だけ表示される
- scene遷移を繰り返してもアイコンが増殖しない
- Main Menu専用の別overlayボタンが重ならない
- 非Flightでクリックすると`AERIS — Preload Terrain Status`が開く
- 非Flight画面にはscene、mode、idle/user active、DB容量、complete/pending、builder body/LOD、PQS、worker、write、compression、天体別coverageが表示される
- 非Flight画面にBUILD／PAUSE／RESUME／CANCEL／VERIFY／REBUILD／DELETE、priority変更、quality変更、容量変更の操作が存在しない
- CloseまたはToolbar OFFで閉じる
- Flightで同じアイコンを押すと既存`AERIS — Flight Control`が開く
- Flightから非Flightへ戻った際、Flightウィンドウが読み取り専用statusとして誤再利用されない
- 非FlightからFlightへ入った際、status窓がFlight Controlとして勝手に開かない
- ログにToolbar owner初期化は原則1回だけ記録される
- Launcher ready／destroyed後の再同期ログがあっても、duplicate owner警告や例外spamがない

不合格：アイコン消失、二重化、scene遷移後に押せない、Toolbar表示と窓状態が逆転、非Flightから管理操作が可能、Main Menu上の独自GUIボタン重複。

## 3. Builderモード

各モードを確認する。

- OFF：tiles complete／pendingが自動増加しない
- MANUAL：BUILDを押した天体だけ開始
- IDLE ONLY：操作中は停止、無操作後に開始
- BACKGROUND：操作中も低負荷継続
- AGGRESSIVE IDLE：操作中は低負荷、放置すると段階加速

重点：VAB／SPHでマウスを連続操作し、入力遅延や部品操作の引っ掛かりがないこと。手を離した後、急なframe spikeではなく段階的にBuilderが加速すること。

## 4. 天体優先度・生成順

1. KerbinをPINNEDにする。
2. MunをNORMAL、MinmusをLOW、未使用天体をDISABLEDにする。
3. KerbinでBUILD。
4. `Global`が先に進み、1地域のLANDだけが先に大量生成されないことを確認する。
5. 登録済み滑走路周辺が全球Overview後の高優先対象になることを確認する。
6. KSP再起動後、priority、quality、cursor、pause、進捗が維持されることを確認する。

同一滑走路一覧が周期更新されても、生成中Tileが2秒ごとに取消されないこと。ログにstale取消しが一定周期で増え続ける場合はFAIL。

## 5. 非Flight生成からFlight読込み

1. Space CenterまたはVABでKerbinを数分生成する。
2. `TerrainPreloadDatabase/manifest.atm`と`Chunks/*.atb`が増えることを確認する。
3. Flightへ入る。
4. Builderの全球生成が停止することを確認する。
5. ND ownship、TRACK UP、runway、操作が即時更新されることを確認する。
6. 生成済み範囲はPQS Final生成より先にDB readで表示されることを確認する。
7. DB missだけがBlock fallbackへ入ることを確認する。
8. Flight終了後、取得したFinal TileがDBへ追加されることを確認する。

合格目安：`terrain_db_read_requests`とcache hitが増え、生成済み範囲で`terrain_tile_sample_batch`が継続的に大量発生しない。

## 6. Range・PLAN世代試験

`5 → 20 → 160 → 10 → 40 → 80 → 160km`を短時間で変更する。その後、遠方PLANへ移動しRECENTERする。

合格条件：

- 旧Range read／decode／PQSが表示へ復活しない
- 最新viewportのCRITICAL readが先行する
- `terrain_stale_results_discarded`は変更時に増えてよい
- queue depthが無制限増加しない
- steady viewでpendingが収束する
- ND ownship／runway／TRACK UPはTile待ちで低頻度化しない
- 黒い三角形・矩形・全消失がない
- style切替待ちの間も直前の地形が下敷きとして残る
- `terrain_gpu_coverage`が一時低下しても、表示済み範囲が全面消失しない
- steady viewで`terrain_tile_obsolete_cancelled`が一定周期で増え続けない

ログに`[ND/TERRAIN] range=...m`が操作順どおり記録されること。

## 7. Hot／Warm／Cold Cache

1. 同じ地域で5kmと20kmを往復する。
2. 同一Flight内でHot RAM hitを確認する。
3. RAM pressure後の再表示でWarm RAMから復元されることを確認する。
4. KSP再起動後、Cold Diskから非同期復元されることを確認する。
5. `terrain_db_cache_hit_ratio`、`terrain_db_read_latency_ms`、`terrain_decompress_time_ms`を記録する。

Main Thread上の同期File I/Oを示す長いframe spikeがないこと。

## 8. Progressive Block表示

未生成地域へ移動する。

- Preview／低LOD／Global／CPU fallbackが先に残る
- 完成Blockから段階的にHDへ置換される
- 1枚の33×33 Final完了まで全域を待たない
- PreviewはDB容量を増やさず、Finalだけ保存される
- 部分coverageで黒背景へ抜けない
- 25／50／75% Finalの上書き中も、完成Previewが穴を埋める
- 完成前Tileを存在だけで100%と判定しない

`terrain_viewport_coverage_ratio`が0から段階的に1へ進むこと。`sampling_remaining > 0`
かつ部分Tileしかない状態で常時`1.0000`に固定される場合はFAIL。

## 9. 容量管理

1. PRELOAD MAP STORAGEを512MBへ設定する。
2. 複数天体を生成し、上限付近まで進める。
3. 古い遠方高LODから削除されることを確認する。
4. Kerbin、PINNED、Global、現在天体、滑走路周辺が先に消えないことを確認する。
5. 天体別capも確認する。
6. UNLIMITEDへ戻せることを確認する。

## 10. Builder／Flight I/O競合

1. 非FlightでBuilder writeを発生させる。
2. 直後にFlightへ入り、複数Rangeを切り替える。
3. Flight critical readへI/O枠が譲られることを確認する。
4. Builder新規PQSがFlight中に継続しないことを確認する。
5. `terrain_db_read_queue_depth`がwrite待ちで長時間固定されないことを確認する。

## 11. 破損復旧試験

事前にKSPを正常終了し、DBフォルダ全体をバックアップする。

```bash
cd "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program/GameData/AERISFlightControl/PluginData"
cp -a TerrainPreloadDatabase TerrainPreloadDatabase.test-backup
```

ノートPCはKSPパスを`$HOME/.local/share/Steam/...`へ置換する。

次の試験は1種類ずつ行い、毎回backupへ戻す。

1. 1個の`.atb`末尾を少量破損
2. `manifest.atm`だけ退避
3. `Journal`へ中断markerを残す想定

合格条件：

- DB全体を捨てない
- valid chunkは利用継続
- 破損Tile／chunkだけ無効化
- 非Flight maintenanceでindex復旧
- 必要範囲はPQS fallback
- ND ownship／runway／symbolは継続
- CRC failureがtelemetryへ記録される

試験後は必ずKSPを終了してbackupを戻す。

## 12. 地形MOD変更

実施可能な環境だけで行う。

1. MOD変更前のbody environment hashを記録。
2. 1天体のPQSへ影響するMOD構成を変更。
3. 該当天体の旧Tileだけ無効化されることを確認。
4. 無関係天体のDB hitが維持されることを確認。
5. `terrain_db_hash_mismatches`と再生成範囲を記録。

全DB削除が発生した場合はFAIL。

## 13. GPU・ND回帰

- AUTO／TOPO／REL／OFF
- TRACK UP／NORTH UP
- 5／10／20／40／80／160km
- PLAN／RECENTER
- runway常時表示と選択
- TRAIL／VECTOR／TRAFFIC／WIND
- GPU failure時にterrain layerだけ停止
- FDI、runway、LAND観測表示が継続

重点確認：

1. 表示済みの同一地点で`TOPO → REL → OFF → AUTO → REL`を切り替える。
2. TOPO／REL切替直後にTile再生成待ちや全面消失が発生しない。
3. RELで航空機高度を変え、同じ地形の危険色が高度に追従する。
4. `SYSTEM > OPTIONS`のTerrain qualityをAUTOにし、負荷時に
   `[ND/TERRAIN] AUTO rate tier=`、必要時に`AUTO quality=ECO`が記録される。
5. 後続の正常worker通知が、一度観測したbacklog判定を同一評価窓内で消さない。

AP／BANK tuningは行わない。LANDを自動着陸として評価しない。

## 14. 長時間試験

最低60分、可能なら3時間以上。

確認：

- RAM／VRAM／Diskが設定上限へ収束
- pending、read queue、decode queue、write queueが無制限増加しない
- result ageが継続増加しない
- scene／vessel／body変更後に旧Tileが戻らない
- Builder state保存が`.tmp`だけ残る状態にならない
- FlightData ZIPとarchive処理が完走する

## 15. 提出物

- 画面録画
- `AERISFlightControl`ログ一式
- KSP.log
- 最新FlightData ZIP
- `NavigationDisplayProfiles.cfg`
- `AERISSettings.cfg`
- `TerrainPreloadDatabase/manifest.atm`
- Performance CSV
- DB全体は容量が大きい場合、manifest、state、問題chunkだけ

提出用ZIP例：

```bash
cd "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
zip -r "$HOME/Downloads/AERIS_CP2_PreloadTerrain_Test.zip" \
  GameData/AERISFlightControl/Logs \
  GameData/AERISFlightControl/FlightData \
  GameData/AERISFlightControl/Config/AERISSettings.cfg \
  GameData/AERISFlightControl/Config/NavigationDisplayProfiles.cfg \
  GameData/AERISFlightControl/PluginData/TerrainPreloadDatabase/manifest.atm \
  GameData/AERISFlightControl/PluginData/TerrainPreloadDatabase/preload_state.aps \
  KSP.log
```

## 16. CP2完成判定

- 非Flightで事前生成できる
- 進捗が再起動を越える
- 生成済みTileを非同期並列読込みできる
- Main ThreadがDisk read／展開を待たない
- 現在viewport readが最優先
- Flight中PQSはDB missだけ
- Block単位で段階表示できる
- FinalをDBへ追加できる
- 容量・priority・qualityを管理できる
- 変更／破損を局所復旧できる
- Tile待ちでもND記号更新が滑らか
- AP／BANK凍結、LAND無制御、旧NAV不在、新NAV BLOCKEDを維持

全項目合格後のみCP3へ進む。
