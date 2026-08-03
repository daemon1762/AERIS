# AERIS v0.18.0.0 DEV CP1 実KSP試験カード

## 目的

CP1はND表示基盤だけを検査します。Terrain Tile、GPU Terrain、適応型進入方式、LAND制御、新NAVは試験対象外です。

## 禁止事項

- 旧NAVを導入しない
- LAND自動操縦として評価しない
- AP/BANK調整を行わない
- 既存cacheを手動削除しない
- FlightDataを手動ZIPしない

## 1. 起動確認

KSP.logで以下を確認します。

```text
AERISFlightControl v0.18.0.0
AERIS Flight Control v0.18.0.0 DEV CP1
```

AssemblyLoader例外、AERIS compile/load例外がないこと。

## 2. 固定レンジ

NDのrange button、+/−、mouse wheelを使い、次の6段階以外が出ないこと。

```text
5 / 10 / 20 / 40 / 80 / 160 km
```

KSP再起動後も最後のrangeが最寄り固定値で復元されること。

## 3. Orientation

### TRACK UP

- `TRK UP`を選択
- 旋回時にmapがtrackへ追従
- ownshipが下寄り約75%位置

### NORTH UP

- `N UP`を選択
- 北が画面上で固定
- ownshipが中央

## 4. PLAN

1. TRACK UP状態でmapをドラッグ
2. headerが`PLAN N`になる
3. ownship追従が停止する
4. dragに応じてrunway/facilityが移動する
5. `RECENTER`を押す
6. ownshipへ戻り、TRACK UPへ復帰する

NORTH UPからPLANへ入った場合は、RECENTER後もNORTH UPであること。

CP1ではPLAN中に`PLAN — TERRAIN PAN IN CP2`が表示され、terrainが偽の位置へ追従しないことが正常です。

## 5. 滑走路常時表示

- LAND OFF、AP OFFでも同一天体のrunway形状が表示される
- 5/10/20kmでは方向名と施設名が読める
- 40/80/160kmでは情報密度が段階的に減る
- registered runwayがfacility symbol上限で欠落しない
- uncertified runwayはcertifiedと区別される
- LAND ARM中も選択runway以外のrunway形状が消えない
- 選択runwayが画面外ならedge pointerが出る

## 6. Preview／SELECT／CENTER

1. runwayをクリック
2. Preview panelだけが出る
3. AIRFIELDS pageの選択がクリックだけでは変わらない
4. `SELECT`を押す
5. AIRFIELDS pageのairfield/directionが変わる
6. `CENTER`でそのrunwayを中心にPLANへ入る
7. `RECENTER`でownshipへ戻る

uncertified runwayではSELECTとARM OBSが有効にならないこと。

## 7. ARM OBS安全確認

明示SELECT後に`ARM OBS`を押します。

- `LAND OBS ARMED`または既存LAND観測状態になる
- 操縦入力、throttle、AP modeが勝手に変化しない
- NAVは表示・ARM・制御とも存在しない

## 8. Resize／移動

- ND title barで移動
- 右下gripで最小～大型へresize
- runway線、label、buttonsがpanel外へ漏れない
- Preview panelが操作可能
- scene再生成、KSP再起動後に位置・sizeを復元

## 9. 遷移

同一KSPプロセスで以下を行います。

```text
Flight → Space Center → Flight
Flight → VAB/SPH → Flight
Active Vessel変更
```

各回でrunway layerが再表示され、旧位置・旧bodyのsymbolが残らないこと。

## 10. 性能証拠

最新`*_performance_runtime.csv`で次の列が存在し、数値が更新されること。

```text
navigation_display_worker_p95_ms
navigation_display_result_age_ms
nd_layout_ema_ms
nd_repaint_ema_ms
nd_capture_ema_ms
nd_texture_upload_ema_ms
nd_gc_positive_delta_ema_bytes
nd_facilities
nd_runways
nd_plan
nd_range_m
```

確認する異常：

- worker failure増加
- stale/failureの連続増加
- result ageが更新後も無限に増える
- ND Layout/Repaintの持続的な大スパイク
- scene遷移後の旧symbol混入

## 提出物

`GameData/AERISFlightControl`全体とSHA-256をZIPで提出してください。可能ならKSP.log、ND操作動画、最新performance CSVも添付してください。
