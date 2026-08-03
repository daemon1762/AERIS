# CP2 Generic Runway Placement Final Candidate 3 Compile Hotfix 1

## 発生した問題

Ubuntuのnative Mono/xbuildで次のエラーが発生した。

```text
Landing/AERISAirfieldRegistry.cs(1368,47): error CS0165: Use of unassigned local variable `stored'
```

`VerifyRunwayPlacement`では、隔離保存結果の詳細を受け取る`stored`を未初期化で宣言していた。条件式は次の短絡評価を含む。

```csharp
witnessLibrary == null || !witnessLibrary.RecordPlacementMismatch(..., out stored)
```

`witnessLibrary == null`が真の場合、右辺は実行されず、`stored`へ代入されない。しかし失敗ブロック内で`stored`を参照するため、Mono C#コンパイラがCS0165を正しく検出した。

## 修正

```csharp
string stored = string.Empty;
```

として条件評価前に必ず初期化する。Witness Libraryがnullの場合は空文字のため、既存のfail-closedメッセージへフォールバックする。

## 非変更範囲

- 汎用`CHECK HERE`判定式
- provider隔離と永続保存
- `MARK A/B`手動二点校正
- Kramax Witness / Anchor Surface Scan / Provisional境界
- Auto Preload Progression
- LAND、AP、操縦権限
- CP2デバッグ表示撤去状態

## 再発防止

専用退行試験と共通C# compile regressionへ、初期化位置、短絡条件、`out stored`、未初期化宣言不在を検査するassertionを追加した。
