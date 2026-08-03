# AERIS v0.18.0.0 CP3 Gate 5 Candidate 10 実機テストカード

## 目的
空港MOD未導入時の幽霊/残留滑走路表示を完全に隔離し、Making History導入時のDessert AirfieldをAIRFIELDS一覧へ正しく表示する。

## A. 空港MOD未導入
1. Kerbal Konstructs / KerbinSide系空港MODを導入していない構成でKSPを完全終了状態から起動する。
2. `SYSTEM > AIRFIELDS` を開く。
3. `KK: KERBAL KONSTRUCTS NOT DETECTED` を確認する。
4. Cape Kerman、Dununda、Harvester等、過去キャッシュ/既定校正にのみ存在するMOD空港が各カテゴリに表示されないことを確認する。
5. LANDの空港選択にも同じMOD空港が出ないことを確認する。
6. NDにもそれらの空港/滑走路シンボルが出ないことを確認する。

合格: provider不在のMOD空港がAIRFIELDS / LAND / NDのいずれにも現れない。既定校正ファイル自体は保持される。

## B. Making History導入済み・runtime未公開
1. Making Historyを導入した状態で起動する。
2. `EXPANSIONS: MH INSTALLED ...` または `MH LOADED` を確認する。
3. AIRFIELDSのPENDINGカテゴリに `Dessert Airfield` が1件表示されることを確認する。
4. 行は `RWY --` とgeometry未公開/未校正状態を示し、LAND選択・ND滑走路表示・LAND ARM対象にならないことを確認する。
5. Woomerangは固定翼滑走路一覧には表示されないことを確認する。

合格: DLC自体の存在は一覧に出るが、未確認geometryをAERISが捏造しない。

## C. Making History未導入
Making History未導入構成ではDessert Airfield行が表示されないこと。

## D. 空港MOD導入済み
Kerbal Konstructsと対象空港MODを導入してreload/rescanする。現在providerが検出した施設だけがAIRFIELDSへ復帰すること。providerが検出していない過去キャッシュ施設は引き続き非表示であること。

## E. Candidate 9回帰
- AIRFIELDS行以外のボタン寸法/位置が文字列や改行で変動しない。
- AIRFIELDSの空港/滑走路行だけは内容に応じた高さ変更を許容する。
- PRELOAD画面開閉でCandidate 8以前のような大幅FPS低下が再発しない。

## 判定
STATIC PASSだけではCP3をCLOSEしない。Native build + 上記runtime確認後にGate 5 closure判定へ進む。
