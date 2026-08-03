# AERIS v0.18.0.0 CP2.5 Quality Migration Hotfix 1

## 目的

CP2.5 Gate 2としてTerrain品質体系を整理し、通常ユーザー向け設定を
`AUTO / LOW / MEDIUM / HIGH`へ統一する。

## 変更内容

- 旧ECOプロファイルをLOWへ改称
- 旧BALANCEDプロファイルをMEDIUMへ改称
- HIGHを維持
- 旧ULTRA相当の詳細プロファイルをLANDへ改称
- AUTOの自動回復上限をHIGHへ固定
- LANDは通常のTerrain品質選択肢から除去
- LAND runtime品質とbody別LAND preload品質をSYSTEM > DIAGNOSTICSへ隔離
- 品質モデルrevision 1を追加し、旧設定を一度だけ移行して保存

## 旧設定移行

| 旧設定 | CP2.5設定 |
|---|---|
| AUTO / Automatic | AUTO |
| ECO | LOW |
| BALANCED | MEDIUM |
| HIGH | HIGH |
| ULTRA | HIGH |

旧ULTRAをLANDへ自動移行しない。LANDはDeveloperが明示的に有効化した場合だけ
品質プロファイルとして選択可能とする。

## Gate境界

本Hotfixは品質モデルと設定移行だけを扱う。通常巡航時のLAND requestを0にする
activation policyは次のGate 3で実装する。AP、AA、PROTECT、APP、FlightCtrlState、
旧NAV削除状態および滑走路登録データは変更しない。
