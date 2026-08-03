# AERIS14 引き継ぎ — CP2 Closure Candidate 1

## 現在地

CP2 Gate 3–5の最終実機受入候補。静的受入後、KolaIsland ILSとGPU地形品質を実機確認する。

## 修正済み

- ND LAND観測キャッシュの`OnApproachSide`／`RunwayGeometryDirectionValid`コピー漏れ。
- 塗りメッシュと海岸線の境界不一致。
- 海岸セルでの陸色はみ出しを保守的な陸側insetで低減。
- REL／TOPOの過大な陰影による塗りむら。

## 維持事項

- AP/BANK/HDG/PITCH/V/S/ALT/ACC/VEL/Ground Stabilityは変更しない。
- LANDは表示・観測専用。
- legacy NAVは復活させない。
- CP3、新NAVはCP2実機合格までBLOCKED。

## 次の判断

実機試験カードを全PASSした場合、CP2をCLOSEDとしてCP3 Gate 6–8（Approach Procedure Registry、可変Glide Profile、3D障害物Corridor）へ進む。
