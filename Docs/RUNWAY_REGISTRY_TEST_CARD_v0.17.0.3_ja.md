# v0.17.0.3 Startup／Cache／Archive 実KSP試験カード

対象: `AERISFlightControl-v0.17.0.3_StartupCacheArchiveHotfix_Source`  
目的: v0.17.0.2実機証拠で残った三件を同一環境で閉じる

## 0. 事前条件

- v0.17.0.3を正式原本からbuild済み
- AERIS DLLは`GameData/AERISFlightControl/Plugins/AERISFlightControl.dll`の1個だけ
- KSPを完全終了してからDLLを更新
- `Airfields`、`Config`、`FlightPlans`、`FlightData`、`Logs`を削除しない
- 次の既存cacheを削除せず保全する

```text
GameData/AERISFlightControl/PluginData/AirfieldCertificationCache.cfg
GameData/AERISFlightControl/PluginData/AirfieldCertificationCache.cfg.bak
```

存在しない場合は「存在なし」と記録する。試験中に手動ZIPを作らない。

## 1. Build前受入

期待値:

```text
13 / 13 scripts PASS
609 / 609 assertions PASS
```

デスクトップ:

```bash
cd ~/Downloads/AERISFlightControl-v0.17.0.3_StartupCacheArchiveHotfix_Source
python3 Tools/run_v01703_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
```

ノートPC:

```bash
cd ~/Downloads/AERISFlightControl-v0.17.0.3_StartupCacheArchiveHotfix_Source
python3 Tools/run_v01703_acceptance.py
chmod +x build_ubuntu.sh
./build_ubuntu.sh "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
```

## 2. STARTUP走査

1. KSPを起動する。
2. Main MenuではSTARTUP走査がcommitしないことをログで確認する。
3. 航空機をロードしてFlightシーンへ入る。
4. Vesselがgo-off-railsとなり、`SYSTEM > AIRFIELDS`の走査が始まるまで待つ。
5. `COMPLETE`まで操作しない。
6. AIRFIELDS画面を動画またはスクリーンショットで保存する。

合格:

- STARTUP走査開始がFlightシーン進入後
- `atomic commit cause=STARTUP`
- revision 1以上
- AIRFIELDS／RUNWAYS／APPROACHが0ではない
- `AIRFIELD_PROVIDER_SNAPSHOT cause=STARTUP`が1件ある
- `DISC_STOCK_KSP`、`DUPLICATE AIRFIELD`、`STAGED DATABASE INVALID`がない

記録:

```text
generation / database revision
REGISTERED RWY / APP
CERTIFIED RWY / APP
FAILED RWY / APP
Provider records / runways / signature
MEASURED / CACHE / FAILED / PENDING / REVALIDATE
SNAPSHOT max / overrun
```

## 3. Manual再走査2回

1. `[↻ RELOAD / RESCAN]`を1回押す。
2. COMPLETEまで待ち、画面とログを保存する。
3. 同じ操作をもう1回行う。

合格:

- `STARTUP → MANUAL 1 → MANUAL 2`でrevisionが単調増加
- 三回の`REGISTERED/CERTIFIED/FAILED/PENDING/REVALIDATE`件数が完全一致
- 三回のProvider `records/runways/signature`が完全一致
- Manual 1とManual 2だけでなくSTARTUPとも一致
- KSC Main RunwayとIsland Airfieldが別施設
- 09／27が別進入方向
- KK/SLEの同一空港内滑走路だけが同じairfieldへまとまる

一項目でも違えば、件数が非ゼロでもFAILとして三つのProvider snapshot行を提出する。

## 4. Cache互換・保存検証

ログで次を確認する。

```text
[AIRFIELD_CACHE] load accepted; certified=...; failures=...
[AIRFIELD_CACHE] save verified; certified=...; failures=...; fullRoundTrip=True.
```

不合格文字列:

```text
CACHE ROOT MISSING
CACHE UNREADABLE
CACHE SAVE FAILED
CACHE LOAD FAILED
CACHE SCHEMA
```

Manual 2完了後、KSPを終了せずcacheとbackupが存在することを確認する。KSP終了後に両方を提出用へコピーする。

## 5. 自動FlightData archive

1. Flightシーンで数分記録する。AP/LAND操作は不要。
2. Space CenterまたはMain Menuへ通常遷移する。
3. KSPを終了せず、次の順序をログで確認する。

```text
[FDR][ARCHIVE] queued
[FDR][ARCHIVE] scheduler accepted
[FDR][ARCHIVE] ZIP verified
```

4. `GameData/AERISFlightControl/FlightData`にAERISが作成したZIPが現れることを確認する。
5. `ZIP verified`前にrawフォルダが消えていないこと、成功後だけ`sourceDeleted=True`となることを確認する。
6. ZIPに対して`unzip -t`を実行する。

合格:

- archive jobがMain Menuでactiveとなる
- `archive_completed`が1以上
- `archive_failed=0`
- `archive_pending=0`
- AERIS自動ZIPのCRC検査PASS
- ZIP内容検証成功後だけraw削除

不合格時はrawを削除・手動圧縮せず、そのままログとperformance CSVを回収する。

## 6. 再起動cache永続性

1. 自動archive確認後、KSPを通常終了する。
2. KSPを再起動して同じ航空機をFlightへ入れる。
3. STARTUP走査を完了させる。

合格:

- `CACHE ROOT MISSING`なし
- `load accepted`あり
- `CACHE` hitが0より大きい、または全対象が意図したrevalidation理由を持つ
- 前回Manual 2とProvider signatureおよび最終件数が一致
- database revisionは新プロセス内で正常にcommit

## 7. 提出物

```text
AERISFlightControl.log
今回のsession.log
KSP.log
performance_runtime.csv
AERIS自動生成FlightData ZIP
AirfieldCertificationCache.cfg
AirfieldCertificationCache.cfg.bak（存在時）
STARTUP／Manual 1／Manual 2／再起動STARTUPの画面または連続動画
AERIS13_versions.txt
```

提出ZIP自体には`unzip -t`とSHA256を付ける。

## 8. この試験で行わないこと

- LAND ARM／自動着陸
- AP/BANK制御則の調整
- 認証閾値の緩和
- 重複検査の無効化
- 旧NAV復活
- 新NAV開発
- 3°固定／可変進入角の最終判定

この試験は三件の基盤ホットフィックスだけを判定する。滑走路ごとの認証完成は次工程で行う。
