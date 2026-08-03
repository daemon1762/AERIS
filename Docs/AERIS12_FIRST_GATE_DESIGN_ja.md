# AERIS12 独立LAND 第一関門 設計記録

## 完成状態

```text
Airfield Registry       IMPLEMENTED
Stock/DLC Provider      IMPLEMENTED
KSP-RO KK Provider      IMPLEMENTED / OPTIONAL
SLE識別                 IMPLEMENTED
Runway Select           IMPLEMENTED
LAND ARM                OBSERVATION ONLY
ND Plan/Profile         IMPLEMENTED
LOC/GS Control          NOT IMPLEMENTED
```

## Provider統合

```text
AERIS CFG definitions
        +
KSP-RO KK LaunchSiteManager
        +
PSystemSetup Stock/DLC facilities
        ↓
AERISAirfieldRegistry
        ↓
Facility classification / validation
        ↓
LAND UI and ND
```

KK ProviderをPSystemSetupより先に処理する。KKは自身のLaunchSiteをPSystemSetupへ登録するため、逆順ではSLEがStockへ誤分類される。

## Validation

- `DiscoveryOnly`: 施設を発見したがThreshold両端を未確定。ARM禁止。
- `FoundationValidated`: 第一関門の観測に使用可能。制御にはまだ使用しない。
- `PrecisionValidated`: 将来の精密LAND制御に使用可能。
- `Rejected`: LAND対象外。

v0.16.0.1以降でFoundationValidatedなのはKSCとIslandのみ。DLC、KK、SLEは検出・分類まで対応し、実測完了までDiscoveryOnlyとする。

## 所有権

`AERISLandingFoundation`は観測クラスである。出力は表示状態と幾何計算だけで、操縦Commandを持たない。

```text
LAND ARMED
CONTROL PILOT
LOC WAIT
GS WAIT
```

ARMは空中かつKSP機体種別`Plane`の固定翼機だけに許可する。Scene変更、Vessel変更、Runway変更、Provider refresh、設定リセットでARMを解除する。

## 次工程

1. KSP/SLE実機ログから全施設列挙結果を確認
2. Dessert AirfieldのThreshold測量
3. SLE滑走路モデル別の端点生成方法を確立
4. KSC/Island ND表示の実機確認
5. LOC ALIVE判定
6. LOC Capture Guidance設計

第一関門の実機PASSまではLOC制御を追加しない。
