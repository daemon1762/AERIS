# CP3.75 — Candidate 4 後の残存課題候補

Candidate 4 は baseline 不具合修正として coastline のサブセル補間と Candidate 3 後の line-cache accounting を対象とする。以下は Candidate 4 の実機合格後も残り得る課題であり、Candidate 4 の修正範囲には含めない。

## 1. 高速域の全体 FPS 低下

Candidate 2/3 で ND repaint の速度依存暴走と forced recovery 増殖は大幅に改善したが、機体速度上昇に伴う KSP 全体 frame time 増加は残る。

次の性能ゲートでは同一飛行条件で最低 15–20 秒ずつ以下を比較する。

1. ND ON / Terrain ON
2. ND ON / Terrain OFF
3. ND OFF

これにより KSP 本体、高速飛行そのもの、ND symbology、terrain presentation の寄与を分離する。

## 2. 瞬間的 GPU coverage drop / repaint spike

Candidate 3 実機ログでは通常 coverage=1.0 だが、ごく少数の瞬間に 160 km で coverage が 0.40–0.99 へ低下し、ND repaint/frame time spike を伴う例がある。
Candidate 4 の cache accounting 修正で改善する可能性はあるが、直接の根治を目的としていない。

Candidate 4 runtime で再発する場合、tile residency / visible-set transition / generation bridge / range transition を時系列で監査する。

## 3. 33x33 source information ceiling

Candidate 4 は既存 Golden LOW sampling の情報をサブセル補間するだけで、新しい地理情報を生成しない。
したがって 160 km coastline がなお粗い場合、人工 smoothing ではなく coastline/cartographic base layer 用の独立した高密度 source を検討する必要がある。
LOW の terrain sampling を軽量な 33x33 のまま維持しても、地図基盤の海岸線情報量は別 authority にできる。

## 4. Prediction / LAND funnel の実機最終確認

Candidate 2 で presented-map-center authority と 60 s prediction horizon を修正済み。静的受入は継続 PASS しているが、ユーザー視認で伸縮/崩壊が完全に消えたかは最終 runtime sign-off が必要。

## 5. Range transition 時の一時 recovery

Candidate 3 ログでは forced recovery は定常飛行中に増殖せず 10 で固定されたが、その10回は主に短時間のrange変更/再構成期間に発生した。
実害（blank/blue flash）が無ければ許容可能。視覚破綻がある場合のみ transition continuity を追加修正する。

## 後回し

- Factory Terrain Seed の標準同梱。
- MIDDLE/HIGH Terrain Quality 復活。
- 10 Hz 以外のユーザー ND Update 設定復活。
- 大規模 worker/GPU 最適化。
