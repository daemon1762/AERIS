---
artifact: AERISFlightControl-v0.17.0.1_PerformanceRuntime_CompileHotfix_Source
date: 2026-07-23
baseline: AERISFlightControl-v0.17.0.0_PerformanceRuntime_Source
status: source RC; native KSP compile pending
---

# AI向け引継ぎ — v0.17.0.1 Compile Hotfix

## 現状

- APは完成済み。BANK制御を含むAP制御則は回帰保護対象であり、本版では変更していない。
- 旧NAVは完全削除済み。新NAVは全面新規開発中で、現行版には飛行機能として未搭載。
- 独立LANDは認証・表示・観測基盤であり、現時点ではFlightCtrlState/AP出力を所有しない。
- v0.17.0.0 Performance Runtime実装を維持する。

## この版の唯一の製品コード変更

```csharp
string.Equals(airfieldDetailId, key, System.StringComparison.Ordinal)
```

v0.17.0.0では`StringComparison.Ordinal`が無修飾で、`AERISWindow.cs`に`using System;`もなかったため、Mono/xbuildでCS0103になった。完全修飾により、不要な名前空間導入やUnity型との曖昧性を増やさず修正した。

## 次の手順

1. `python3 Tools/run_v01701_acceptance.py`
2. `./build_ubuntu.sh "<KSP root>"`
3. ネイティブビルド成功後、`Docs/PERFORMANCE_TEST_CARD_v0.17.0.1_ja.md`を使用して2-worker/GPU OFFから実KSP受入
4. 実KSPで別のコンパイルエラーが出た場合、ログ全文を保存し、v0.17.0.1を基準に最小修正

## 禁止事項

- このホットフィックスを理由に完成済みAP/BANK則を調整しない。
- NAVを搭載済み・利用可能と記載しない。
- ソース静的試験の合格を、KSPネイティブビルドや実飛行合格と表現しない。
