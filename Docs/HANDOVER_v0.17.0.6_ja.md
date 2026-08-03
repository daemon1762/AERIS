# AERIS v0.17.0.6 引き継ぎ

## 正式ベース

v0.17.0.5から派生した、永続cache再起動決定性ホットフィックス。

## v0.17.0.5実機判定

```text
Provider identity signature       PASS
STARTUP / Manual DB一致           PASS
Cache schema 4完全往復            PASS
FlightData自動ZIP                 PASS
再起動後exact cache hit           FAIL
```

二回目STARTUPは`MEASURED 12 / CACHE 4`。同一プロセスのFlightシーン再生成後も`MEASURED 11 / CACHE 5`だった。

## v0.17.0.6の変更

- algorithmVersion `1660`
- cache fingerprintをruntime Geometry multisetからcanonical source asset fingerprintへ変更
- PrefabがあるKK/SLEではPrefabをcache identityの唯一のmodel rootにする
- Stockではfacility rootのlocal asset構成を使用
- runtime Points／Primitivesは測量入力として維持
- Survey definition全安全条件をcache鍵へ追加
- cache miss reasonと旧新fingerprint prefixをログ化

## 絶対禁止

- AP/BANK制御則を変更しない
- LANDへFlightCtrlState／スロットル／操舵権限を与えない
- 旧NAVを復活させない
- cache hitを得るために認証失敗をCERTIFIEDへ昇格しない
- runtime Geometryの測量そのものを省略しない

## 次の実機ゲート

1. 現在のschema 4 cacheを削除しない
2. v0.17.0.6初回STARTUPはalgorithm更新により再測量を許容
3. Manual再走査では`MEASURED 0 / CACHE 16`
4. Main Menuへ戻り自動Archive完了を待つ
5. 同じKSPプロセスで再度Flightへ入りManual再走査
6. KSPを通常終了し再起動
7. 二回目STARTUPで`MEASURED 0 / CACHE 16`
8. Provider identity signatureとDB件数が全走査で一致

## 未完

- Snapshot 1.50ms slice超過と単発10～30ms級スパイク
- 滑走路方式別の未認証施設対応
- 可変3.0～6.0°進入回廊
- 独立LAND総合受入
- 新NAVはLAND完成までBLOCKED
