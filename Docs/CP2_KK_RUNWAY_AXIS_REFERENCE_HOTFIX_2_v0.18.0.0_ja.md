# AERIS v0.18.0.0 CP2 — KK Runway Axis Reference Hotfix 2

## 状態

- CP2はOPENのまま継続する。
- 本Hotfixは`Mod Airfield Recovery Hotfix 1 + Auto Preload Progression 1 Compile Hotfix 1`を直接継承する。
- CP3、新NAV、LAND操縦権限には進まない。

## 実機FAIL

`AERISFlightControl(16).zip`の最新セッションでは、起動時キャッシュが`certified=12; failures=40`を読み込んだ後、再測量結果が次へ悪化した。

- `REGISTERED 43 RWY / 86 APP`
- `CERTIFIED 3 RWY / 6 APP`
- `FAILED 40 RWY / 80 APP`
- `MEASURED 1 / CACHE 0 / FAILED 40 / REVALIDATE 12`

Dull Spot Runwayだけが`axisRegistrationValid=True`となり、Dundard's Edge、Kola Island、Mahi、Goldpool、Uberdam、Cape Kerman、Kojave Sands、Polar Research Alpha、Sandy Island等は`AbsolutePlacementInvalid`へ落ちた。

## 根本原因

`Axis Registration Hotfix 1`は、実滑走路面PCAで得た物理軸の広域sanity checkとして、次を比較していた。

- 物理滑走路面のheading
- `snapshot.DeclaredHeadingDeg`

しかしKK/SLEにおける`DeclaredHeadingDeg`は、多くの場合「滑走路方位」ではなく、配置された静的モデル本体のorientationである。モデル内部で滑走路meshが回転している空港では、静的モデルorientationが`0°`のままでも滑走路は`133.57°`等になり得る。

実例：Dundard's Edge

- 静的モデルorientation：`0°`
- 実舗装mesh軸：`133.57°`
- launch/spawn transform方位：実舗装軸と一致

Hotfix 1の15°ゲートでは`0°`対`133.57°`となり、正常滑走路をfail-closedで拒否した。Dull Spotはmesh軸が`179.28°`で、軸等価な`0°/180°`近傍だったため偶然通過した。

## 修正

`ApplyAxisRegistrationConstraint`の独立方位参照を次へ変更した。

- 廃止：KK静的モデル本体の`DeclaredHeadingDeg`
- 採用：`LaunchAnchorHeadingDeg`（launch/spawn transformのworld-space方位）

判定階層は次で固定する。

1. 実配置済み舗装meshから抽出した物理軸が主証拠。
2. 候補軸と物理軸の差が1°以内なら直接採用。
3. 12°以内なら全スカラー形状を物理軸へ再登録。
4. launch/spawn方位は15°の広域sanity gateとしてのみ使う。
5. launch方位は物理軸の代用や強制上書きには使わない。
6. 独立舗装軸、launch anchor、支持点数、aspectのいずれかが不足すればfail-closedを維持する。

## キャッシュ回復

`CurrentAxisRegistrationRevision`を`1`から`2`へ更新した。Source Fingerprintの`KK_RUNWAY_AXIS_REGISTRATION`枝だけが変化するため、Hotfix 1が保存したKK/SLEの`AbsolutePlacementInvalid`失敗レコードを対象限定で再測量する。バニラ滑走路や無関係キャッシュは意図的に失効させない。

## 診断ログ

`[RUNWAY_AXIS]`へ次を明示する。

- `meshRunwayHeadingDeg`
- `launchTransformHeadingDeg`
- `registeredHeadingBeforeDeg`
- `registeredHeadingAfterDeg`
- `headingCorrectionDeg`
- `axisReference=LAUNCH_ANCHOR`
- `axisReferenceErrorDeg`
- `surfaceAspect`
- `surfacePoints`
- `axisRegistrationValid`

旧`runwayDesignatorErrorDeg`という誤解を招く名称は内部契約とログから除去した。

## 変更しないもの

- Auto Preload Progression 1の選択・完了・昇格アルゴリズム
- Terrain Tile/LOD/Cache/GPU表示
- AP、AA、PROTECT、Ground Assist、Auto Takeoff
- LANDの観測専用境界
- 旧NAV削除状態
- 操縦面、スロットル、ブレーキへの書込み権限

## 実機合格条件

- 初回起動で対象KK/SLE滑走路がAxis Revision 2により再測量される。
- Kola Island、Dundard's Edge、Mahi、Uberdam、Kojave Sands、Sandy Islandで`axisRegistrationValid=True`。
- 同空港で`absolutePlacementValid=True`。
- 最低でも修正前の正常水準`CERTIFIED 14 RWY / 24 APP`へ回復し、`CERTIFIED 3 / FAILED 40`を脱する。
- 代表空港がLAND/ND選択一覧に現れ、滑走路線が実舗装へ一致する。
- 再起動時に対象キャッシュが安定して再利用される。
- native Mono/xbuild成功。

これらの実機証拠が揃うまでCP2はCLOSEしない。
