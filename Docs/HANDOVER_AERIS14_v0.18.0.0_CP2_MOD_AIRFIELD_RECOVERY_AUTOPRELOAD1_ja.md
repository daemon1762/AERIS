# AERIS14 引き継ぎ — MOD Airfield Recovery Hotfix 1 + Auto Preload Progression 1

## 基準原本

`AERISFlightControl-v0.18.0.0_DEV_CP2_ModAirfieldRecoveryHotfix1_AutoPreloadProgression1_Source`

## 修正理由

実機ログでAxis Registration版はMOD滑走路43本中40本を失敗扱いとし、認証済み滑走路は3本まで減少した。主な新規回帰は、独立滑走路面軸が近接Primitiveで上書きされること、1度超の候補差を補正せず即拒否すること、付属物のFlatnessを滑走路勾配として集計することだった。

Preload Builderは完成済みKerbinを現在天体優先で選び続け、他天体へ進まなかった。

## 実装済み

- 独立物理軸の上書き防止
- 最大12度までの物理軸再登録
- 12度超／支持不足／RWY番号不整合の安全拒否
- 着陸面限定Slope判定
- KK/SLE対象キャッシュFingerprint更新
- 天体ごとの自動完成マーカー
- 完成済み現在天体の選択除外
- 全固体天体Far被覆の自動ローテーション
- 全Far完了後のHigh/Pinned登録地点Land point-only昇格
- 手動品質Override
- Preload state V2＋V1互換読込み

## 未実施

- native Mono/xbuild
- KSP実機再測量後の認証本数確認
- 長時間で全固体天体Far完了までの試験
- 自動Land point-only昇格の実機完走

## CP2判定

OPEN。MOD空港の実滑走路一致とPreload自動天体遷移の実機証明後に閉じる。

## 次の再開位置

1. native build
2. 起動後Airfield reload集計
3. KolaIsland等の軸一致確認
4. 非Flight放置でKerbin→Mun／Minmus等への自動遷移確認
5. 合格ならCP2 CLOSE、CP3 Gate 6へ移行
