# 添付証拠・整合性報告

作成日: 2026-07-23

## 1. 受領物

| ファイル | サイズ | SHA-256 | 判定 |
|---|---:|---|---|
| `AERISFlightControl.zip` | 28,835,840 B | `b02effbad541eef5503ede22c123e50c971a1237ea34760857eb426c3060b4b4` | 外側ZIP末尾破損 |
| `KSP(4).log` | 16,969,728 B | `2d38371f0b4df615129316d4982bf758837eceaa4c1733b9b9045b42d518f211` | 読取可能、ただし飛行シーン前に終了 |
| BANK参照画像 | 431,705 B | `13527c824cb71379f7aae37559b0ac17a2328a7261e296fb819fb76d6b9ec1e6` | 読取可能 |

## 2. 破損アーカイブの復旧

外側ZIPにはローカルヘッダが残っていたが、中央ディレクトリと末尾が欠落していた。最後の完全なエントリまでを切り出してZIP構造を再構成し、CRC検査に合格した。

救出できた完全データ:

- `AERISFlightControl.version`
- `AERISSettings.cfg`
- 完全なFlightDataセッションZIP 12本
- 最終完全セッション: `2026-07-22_173536_ARA-2.zip`

復旧済み外側ZIP:

```text
size    28,361,829 B
SHA-256 32c9c87bee84a26d0d1e36b1ecd786c578dc9dba831f3c5a85c5352a56f1dfcc
ZIP CRC test: PASS
```

不完全な最終エントリ:

```text
2026-07-23_023322_863_000001_ARA-2_3317133d.zip
```

この内側ZIPから先頭の`cvr_events.csv`を完全なCRCで救出した。後続ファイルは途中で切れているため、証拠として使用しない。

```text
recovered cvr_events.csv size    110,751 B
recovered cvr_events.csv SHA-256 1237287f0b8c49c161ee1a8edfe3fec15c17a68f14d37ea574e8bfa492f7b98e
```

## 3. 時系列

| 時刻 | 証拠 | 解釈 |
|---|---|---|
| 2026-07-23 02:33 UTC | 救出CVR開始 | v0.17.0.2作成前の実行 |
| 02:34 UTC | 手動reload generation 2 | 旧`DISC_STOCK_KSP`衝突で全体拒否 |
| 02:35 UTC | 手動reload generation 3 | 同じ衝突で全体拒否 |
| 03:07 UTC前後 | v0.17.0.2成果物確定 | ID衝突ホットフィックス |
| 12:09 JST | `KSP(4).log`がv0.17.0.2読込み | 修正版DLLの読込みは確認 |
| 12:11 JST | `KSP(4).log`終了 | ModuleManager処理中。飛行・reloadなし |

したがって、救出CVRの失敗をv0.17.0.2の失敗として扱ってはならない。同時に、`KSP(4).log`だけでv0.17.0.2の成功を主張してもならない。

## 4. 認証失敗と性能警告の分離

救出CVRの実障害は次のステージ全体エラー。

```text
DUPLICATE AIRFIELD Kerbin / DISC_STOCK_KSP
```

一方、`AIRFIELD_SNAPSHOT ... exceeded the 1.50 ms component slice`は処理時間警告であり、滑走路認証失敗コードではない。

集計:

- 性能警告: 98件
- 1.50 ms超過slice: 180回
- 警告が記録された施設: 33
- カタログ中、警告が記録されなかった施設: 8

警告なしは「認証合格」も「未検出」も意味しない。通常時間内で処理された、ログが残らなかった、または別条件で処理されなかった可能性を区別できないため、修正版実行ログで再判定する。

## 5. v0.17.0.2ソースの照合

確定保存されていた基準ZIPを再取得し、公開済みSHA-256と一致した。

```text
0c20572af23d47741c82bed7eaaa2e5d737ca861d68ff7b9a1b243abf7f00db7
```

展開後、`Tools/run_v01702_acceptance.py`を再実行し、12 / 12スクリプト、561 / 561アサーションが合格した。

この受入は、次を証明しない。

- KSP参照DLLを使用したネイティブビルド
- v0.17.0.2の実ゲーム内atomic commit
- 各滑走路方向の障害物安全性
- 可変グライドパスの実装
- 自動着陸制御

## 6. 証拠保存上の注意

今後の実KSP試験では、KSP終了後に次を個別ファイルとして保存し、その後ZIP化する。

- `KSP.log`
- AERIS通常ログ
- `cvr_events.csv`
- AIRFIELDS画面スクリーンショット
- KSP、DLC、KK、SLE、AERISの版一覧
- 起動時、手動1回目、手動2回目の件数

ZIP作成後は展開検査とSHA-256生成を行い、原本は検査が終わるまで削除しない。
