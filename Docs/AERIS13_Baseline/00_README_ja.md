# AERIS13 精密コーディング終了・引継ぎセット

作成日: 2026-07-23  
基準ソース: AERIS Flight Control v0.17.0.2 Runway Registry Identity Hotfix Source RC

## 結論

このセットをもって、今回の精密コーディング工程を終了する。

ただし、これは「自動着陸認証済み」の宣言ではない。現時点の確定状態は次のとおり。

- AP: 完成済み。変更禁止。
- BANK: 提示された20°ロール図を回帰基準として凍結。
- NAV: 旧NAVは完全削除済み。新NAVは未搭載・再構築待ち。
- LAND: 独立系の表示・観測・滑走路認証基盤。操舵権限なし。
- 滑走路レジストリ: v0.17.0.2で旧ID衝突を修正済み。ただし修正版の実KSPコミット成功ログは未取得。
- 進入経路認証: 現行コードは3.0°固定かつ中心線上の粗い地形検査だけであり、精密着陸認証としては未完成。

## 最初に読む順序

1. `01_FINAL_HANDOVER_ja.md`
2. `02_RUNWAY_BY_RUNWAY_RESPONSE_ja.md`
3. `03_ADAPTIVE_GLIDE_PATH_POLICY_ja.md`
4. `04_FAILURE_CODE_RESPONSE_MAP_ja.md`
5. `05_EVIDENCE_INTEGRITY_REPORT_ja.md`
6. `06_POST_HOTFIX_FIELD_TEST_CARD_ja.md`

次の会話へ移す場合は、`07_NEXT_CHAT_START_PROMPT_ja.txt`をそのまま渡す。

## 同梱物

- `Baseline/`: 照合済みv0.17.0.2ソースZIPとSHA-256
- `Evidence/`: 破損アーカイブからCRC確認付きで救出した最新CVR、性能警告集計
- `Reference/`: BANK回帰基準画像
- `MANIFEST_SHA256.txt`: この引継ぎセット内の全ファイル照合値

元の破損ZIP、完全な`KSP(4).log`、CRC確認済み復旧ZIPは、容量を分離するため別成果物`AERIS13_Raw_Evidence_Recovery_2026-07-23.zip`に収録する。

## 最重要の運用制限

有効なv0.17.0.2実行ログで、起動時および手動2回のレジストリ更新が原子的にコミットされるまで、滑走路検知修正を「実機合格」と扱わない。

方向別の三次元進入回廊、障害物、進入復行、機体能力を検査する次世代認証器が完成するまで、`CERTIFIED`表示を自動着陸の安全保証として使用しない。片方向が不合格でも、反対方向まで一括失格にはしない。
