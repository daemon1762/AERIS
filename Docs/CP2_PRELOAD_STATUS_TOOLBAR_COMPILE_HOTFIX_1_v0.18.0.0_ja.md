# CP2 Preload Status Toolbar Compile Hotfix 1

## 原因

`AERISTerrainDisplayMode`の実定義は`Automatic`だが、
`AERISTerrainTileSystem.RefreshTerrainRequestGeneration()`のnull設定時だけ
存在しない`Auto`を参照していた。Mono/xbuildはCS0117で停止した。

## 修正

```csharp
AERISTerrainDisplayMode.Automatic : settings.TerrainDisplayMode;
```

表示モードの意味、Terrain要求世代の更新条件、Toolbar、Preload DB、
Terrain Block Pipeline、AP、LANDには仕様変更を加えていない。

## 再発防止

静的C#回帰試験で、ソース全体に現れる
`AERISTerrainDisplayMode.<member>`を列挙し、enumの宣言済みmember集合と照合する。
未定義memberが1件でもあれば受入をFAILさせる。

## 未実施

成果物作成環境ではMono/xbuildとKSP参照DLLがないため、
ネイティブC#コンパイルとKSP実行は未試験。ユーザー環境のxbuildを実機ゲートとする。
