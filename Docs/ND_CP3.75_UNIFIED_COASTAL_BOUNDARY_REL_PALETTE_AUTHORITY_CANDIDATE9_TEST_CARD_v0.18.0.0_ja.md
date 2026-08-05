# CP3.75 Candidate9 実機テストカード

## 目的
Candidate8で成立した「33×33基底 + 129由来Sparse海岸補正」の美観と性能を維持しつつ、実機で確認された2件の不具合を閉じる。

1. High Contrast + AUTO→REL時の色意味異常
2. 地形fillがHD海岸線を越える局所的不一致

## 試験A: 最優先 KSC / 20 km
- Candidate8不具合再現時と同じ視点。
- ND RANGE 20 km。
- AUTO、High Contrast。
- AUTOがRELへ入った際、通常色意味が以下であること。
  - 危険: 赤
  - 注意: 黄
  - 安全寄り: 高輝度緑
  - 十分離隔: 暗緑
- シアンの安全帯が出ないこと。

## 試験B: 色authority切替
- Standard → High Contrast → Standard を数回切替。
- AUTOのREL/TOPO遷移も可能なら確認。
- 切替直後に旧色FRONTが残存し続けないこと。
- ブラックアウト、全面BUILDING固着が無いこと。

## 試験C: 海岸線/fill一致
- KSC周辺の複雑な海岸線を20/40/80/160 kmで観察。
- 水色のHD coastlineより海側へ陸地fillが明確にはみ出さないこと。
- 逆に水fillが陸側へ明確にはみ出さないこと。
- 線幅/AA由来の1px程度の見え方は許容するが、帯状・三角形・矩形の越境はFAIL。

## 試験D: 性能
- Candidate8と同じ視点・同じ設定でFPS比較。
- Candidate7級（約2 FPS）は即FAIL。
- Candidate8と同等クラスならPASS。
- 160 kmも確認し、移動時の長時間ブラックアウトや強いカクつき増大が無いこと。

## ログ確認
`[CP3_GATE4C_VIRTUAL_DETAIL]` で以下を確認。
- `coast_hd_entries > 0`
- `coast_sparse_entries > 0`
- `coast_sparse_parents` が有限
- `forced_recovery` が定常状態で暴走しない
- `ready_build_violation = 0` を基本とする

## 基礎構造完成判定
以下を同時に満たせば、NDのCP3.75基礎描画構造を完成扱いとしてよい。
- Candidate8級以上の画質
- 実用FPS
- REL High Contrast正常
- 海岸線/fillの目視一致
- 新規ブラックアウト/追従/投影回帰なし

以後は基礎再設計ではなく、品質向上・機能追加・性能微調整として扱う。
