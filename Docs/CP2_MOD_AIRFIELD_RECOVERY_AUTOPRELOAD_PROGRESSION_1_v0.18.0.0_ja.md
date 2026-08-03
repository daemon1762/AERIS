# CP2 MOD Airfield Recovery Hotfix 1 + Auto Preload Progression 1

## 対象不具合

実機ログでは、起動時キャッシュに12本の認証済みMOD滑走路が存在したにもかかわらず、Axis Registration再検証後は3本まで減少した。失敗40本の主な内訳は、`AbsolutePlacementInvalid` 14本、`SurfaceSlopeExceeded` 12本、`InsufficientEvidence` 7本、`RunwayWidthUnresolved` 6本、`WholeSiteBoundsOnly` 1本だった。

PreloadはKerbinのFar走査が完了しても、現在天体優先によって完成済みKerbinを毎フレーム再選択し続け、Mun・Minmus・他の固体天体へ進まなかった。

## MOD空港回復

- 独立した実滑走路面軸は、後から追加されるProvider／Primitive候補で上書きしない。
- 候補軸と実滑走路面軸の差が1度以内なら従来どおり直接認証する。
- 1～12度で、実滑走路面の支持点・Aspect・RWY番号整合が成立する場合は、候補の物理端点距離、使用可能距離、両Operational Threshold距離を保ったまま実滑走路面軸へ再登録する。
- 12度を超える補正、RWY番号との15度超不整合、支持点不足は引き続き安全拒否する。
- Surface slope判定はRunway／Pavement／Centerlineの着陸面だけを使用する。Edge light、Approach light、Platform、Apron、建物、自然地形等はTopology証拠には使えるが、滑走路面勾配を失格させない。
- `KK_MOD_AIRFIELD_RECOVERY / Revision 1`をFingerprintへ追加し、KK／SLEの旧失敗キャッシュを再測量する。バニラ滑走路キャッシュは対象外。

## Auto Preload Progression

自動モードでは以下の順序を固定する。

1. Kerbinだけでなく、PQSを持つ全固体天体を現在の品質上限まで生成する。
2. ある天体が一周の索引走査で欠損なしと確認されたら、完成状態を永続保存して自動選択から除外する。
3. 完成済みKerbinはMun、Minmus、Duna等へ処理枠を譲る。
4. 全固体天体の標準Far被覆が完成した後、手動品質指定のないHigh／Pinned天体について、登録滑走路・現在地点等のPreload pointだけをLand品質へ自動昇格する。
5. この自動Land昇格はPoint-onlyであり、全球Route生成を暗黙に開始しない。容量爆発を防ぐ。
6. ユーザーが品質を手動指定した天体は自動昇格しない。
7. PQS／地形MOD環境、Preload point集合、品質設定、Rebuildが変われば完成状態を無効化して安全に再走査する。

Preload stateはVersion 2へ更新し、Version 1を読み込み可能なまま維持する。

## 安全境界

- LANDは引き続き観測専用で操縦権限を持たない。
- 角度補正は実滑走路面軸が独立して取得できた場合だけ行う。
- 不明確なMOD空港を無条件に有効化しない。
- Preloadは既存共有Scheduler・有界Queueを維持し、専用ThreadPoolを追加しない。
