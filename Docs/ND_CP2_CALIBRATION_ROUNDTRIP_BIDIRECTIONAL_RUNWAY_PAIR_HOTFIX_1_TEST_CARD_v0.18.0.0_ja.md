# AERIS v0.18.0.0 CP2 手動校正保存・相反2方向 最終実機試験カード

## 1. ビルド

- SHA-256一致
- 全静的受入PASS
- native Mono/xbuild成功
- Build identity末尾が次であること

```text
CALIBRATION ROUND-TRIP HOTFIX 1 BIDIRECTIONAL RUNWAY PAIR HOTFIX 1
```

## 2. 初回条件

既存の`UserRunwayCalibrations.cfg`は削除しない。旧schemaからの移行と、保存失敗時のrollbackを含めて確認する。

## 3. Kola手動校正

1. Kolaの実滑走路端Aへ機体を停止する。
2. AIRFIELDSで対象物理滑走路を選び`MARK A`。
3. 反対端Bへ移動して停止する。
4. `MARK B`。
5. 次のエラーが出ないこと。

```text
CALIBRATION SAVE FAILED
```

6. UIに`PHYSICAL RUNWAY RWY xx/yy READY`が表示されること。
7. Reload/Rescan後、同じ物理滑走路配下へ2方向が現れること。
8. A→B方向のThresholdがA、Opposite ThresholdがBであること。
9. B→A方向のThresholdがB、Opposite ThresholdがAであること。
10. 両方位が180度差であること。
11. 両方向のStable IDが異なること。

## 4. ND/進入方向

- NDで同じ物理滑走路の双方の方向を個別に選択できること。
- 双方のLocalizer線が同じ中心線上で逆向きに延びること。
- 方向別の地形検証結果が独立していること。
- 片側が`APPROACH TERRAIN BLOCKED`でも、反対側が安全なら反対側はCERT可能であること。

## 5. 再起動継承

1. KSPを完全終了する。
2. `UserRunwayCalibrations.cfg`が存在すること。
3. KSPを再起動する。
4. `[RUNWAY_WITNESS] ... USER 1以上`を確認する。
5. Kolaの相反2方向が再生成されること。
6. 保存ファイルがschema 3であり、以下を持つこと。

```text
reciprocalDirectionPair = True
 directionAHeadingDeg = ...
 directionBHeadingDeg = ...
```

## 6. 汎用性

Kola以外のずれた空港を1か所手動校正し、同じ1物理滑走路→相反2方向生成が成立することを確認する。空港固有例外は合格扱いにしない。

## 7. ログ合格条件

```text
fullRoundTrip=True
committedReadback=True
reciprocalPairSchema=3
RECIPROCAL PAIR GENERATED
localizerPair=True
approachValidation=INDEPENDENT
```

以下が1件でもあればFAIL。

```text
CALIBRATION SAVE FAILED
RECIPROCAL PAIR REJECTED
InvalidDataException
NullReferenceException
```

## 8. CP2 CLOSE前の残条件

- Auto Preload Progressionの他天体進行
- `COMPLETE`
- `PROMOTE`
- 再起動継承
- CP2専用デバッグ表示・設定・ログの不在

全条件成立後のみCP2をCLOSEDとする。
