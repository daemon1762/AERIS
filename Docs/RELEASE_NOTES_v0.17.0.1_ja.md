# AERIS Flight Control v0.17.0.1 リリースノート

状態: **Performance Runtime Compile Hotfix Source RC**  
日付: 2026-07-23  
基準: v0.17.0.0 Performance Runtime Source RC

## 修正

`Source/AERISFlightControl/UI/AERISWindow.cs`のAIRFIELDS詳細表示で使う比較指定を、無修飾の`StringComparison.Ordinal`から`System.StringComparison.Ordinal`へ変更しました。

これによりMono/xbuildの次のコンパイルエラーを解消します。

```text
UI/AERISWindow.cs(238,94): error CS0103: The name `StringComparison' does not exist in the current context
```

## 回帰防止

- `System.StringComparison.Ordinal`による完全修飾を静的に確認
- 無修飾`StringComparison`を使う全C#ファイルに`using System;`があることを検査
- v0.17.0.0のPerformance Runtime、PC1、LAND、表示、安全、旧NAV削除の全受入試験を継承
- 完成済みAPおよび`AERISBankDirector.cs`は変更なし

## 変更していないもの

- AP制御則、BANK先行制動・捕捉・保持則
- LAND認証・観測基盤
- Performance Runtime、非同期記録、アーカイブ
- NAV（旧NAVは削除済み、新NAVは未搭載・開発中）

実KSP向けネイティブコンパイルはKSP参照DLLとMono/xbuildを持つ環境で実施してください。本パッケージの`build_ubuntu.sh`は、v0.17.0.1受入試験を実行してからコンパイルします。
