# CP3 Gate 3 Predictive Forward Corridor 実機試験カード

## build確認

タブメニュー上部が次であること。

```text
AERIS v0.18.0.0 DEV CP3 GATE 3 — PREDICTIVE FORWARD CORRIDOR
```

`DEV CP2`または`CP3 GATE 2`が現在build表記として残っていればFAIL。

## 1. 直線飛行

1. Kerbin上でND TerrainをAUTOまたはTOPOにする。
2. 対地速度100m/s以上で30秒以上直線飛行する。
3. SYSTEMの`CP3 Corridor`が`STRAIGHT FORWARD CORRIDOR`になること。
4. ahead距離、秒数、request／pinが0より大きいこと。
5. 操縦入力、AP目標、PROTECT状態が回廊生成によって変化しないこと。

## 2. 旋回追従

1. 30～60度bankで連続旋回する。
2. `CP3 Corridor`が`CURVED FORWARD CORRIDOR`へ変化すること。
3. turn rateが符号付きで更新されること。
4. 旋回を戻すと直線状態へ復帰すること。
5. pending requestが無制限に増加しないこと。

## 3. LAND demand

1. 通常巡航でLANDをDISARMにする。
2. `G/F/R/L/LD`のLDが巡航要求だけで増加しないこと。
3. 滑走路を選択しLANDをARMする。
4. LAND状態が`DEMAND`となり、選択滑走路付近のLD residentまたはdecode投入が増えること。
5. DISARM後、LAND／Runway pinが解放され、pin数が減少すること。

## 4. 優先順位

- viewport表示が回廊より先に出ること。
- LAND ARM中は選択滑走路が回廊より先に供給されること。
- 高速飛行中もFlight安全laneや操縦品質に影響しないこと。

## 5. 解放境界

以下でcorridor pinが解放されること。

- ND Terrain OFF
- 海抜40.5km以上のAltitude Gate OFF
- scene transition
- body transition
- vessel切替

## 6. 長時間

30分以上飛行し、RAM、pin、decode、requestが有界であること。別天体payloadが現在天体Residentへ混入しないこと。
