# AERIS 2026-07-24 実行証拠解析

## 1. 証拠と完全性

| 証拠 | SHA-256 | 判定 |
|---|---|---|
| 入力ソースZIP | `50e469b0add6ce8a21e6218ddecca106676d5bb9eaaa908ff1e828515c03dfe8` | 提供SHAと一致、ZIP検査合格 |
| 入力ログZIP | `34bcab7ba413574c6c85f7266d609fed2ab9231b1f5e97e4738df04a9ddc20dc` | ZIP検査合格 |
| session log | `4773da7c061d3abb9bbce2e341f36754dab4c524fb8a1d3e9832944a172872ba` | 読取可能 |
| Performance CSV | `815c4b46dd0b29cb5900938ab193c730b6af90ed36543af4f7f7bdcc21e711ec` | 902行、138列 |
| FlightData ZIP | `43f9732c6c05b0c6649c6bf22efdcd5f86cd4236982f4cf1eaed4e16e9ddf5c4` | 16ファイル、CRC合格 |

今回の添付物に動画はない。提示画像は完成済みBANKの回帰基準であり、地形描画の実画面証拠ではない。

## 2. 滑走路Registry

11:57:19にstartup reloadを開始し、11:57:26にatomic commitへ到達した。

```text
databaseRevision=1
REGISTERED 43 RWY / 86 APP
CERTIFIED 13 RWY / 24 APP
FAILED 32 RWY / 62 APP
PENDING 0 RWY / 0 APP
```

結論：

- AERIS13で未証明だった、修正後の起動時DB commitは今回一回確認できた。
- 旧障害の`DISC_STOCK_KSP`重複は今回のログに現れていない。
- `AIRFIELD_SNAPSHOT`警告はcomponent slice 1.50ms超過であり、認証失敗コードそのものではない。
- 最大sliceはGlacier Lake Runwayの16.917ms。
- 手動RELOAD二回の再現ログはないため、AERIS13の完全なRegistry field gateは未完了。
- 13 certifiedは現行の粗い認証器による結果であり、方向別3D障害回廊、missed approach、機体適合性を含む最終安全認証ではない。

## 3. 地形Runtime

### 確認できた正常項目

- GPU failure 0。
- DB CRC failure 0。
- DB hash mismatch 0。
- first tile visibleは最終18.6768ms。
- 最終GPU coverageは0.9917まで回復。
- Flight中のDB write増加は確認されていない。

### 不整合を示す項目

- Range／表示切替の間にGPU coverageが0へ落ちてもviewport coverageは1.0000を維持する区間がある。
- 最終時点でもsampling remaining 2、pending 2の一方、viewport coverageは1.0000。
- stale cancellation 1353、obsolete cancellation 1341に達した。
- partial／preview／finalの状態と表示coverageの意味が一致していない。

これは「GPUが壊れた」または「DBが破損した」証拠ではなく、世代更新、途中結果の状態表現、fallback合成、coverage算出の整合性障害である。

## 4. 証明できない事項

- Field Render Consistency Hotfix 1適用後の実KSP画面。
- native Mono/xbuild compile。
- Unity GPU shader／mesh実行。
- 実PQS負荷下のsteady cancellation収束。
- 60分以上のRAM／VRAM／queue boundedness。
- Range／mode切替中に黒欠損が一切出ないこと。
- AUTO品質低下／回復ログの実測。

これらは修正版試験カードに従って再取得する。

## 5. 判定

入力版`DEV CP2 PRELOAD STATUS TOOLBAR COMPILE HOTFIX 1`のCP2 Field Render gateはFAIL。

本Hotfixのソース監査・静的回帰はPASSとしてよいが、CP2の実機合格は保留する。CP3開始条件は満たしていない。
