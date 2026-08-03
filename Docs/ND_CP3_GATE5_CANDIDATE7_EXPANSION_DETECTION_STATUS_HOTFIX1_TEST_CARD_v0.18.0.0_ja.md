# CP3 Gate 5 Candidate 7 — Expansion Detection / DLC Runtime Status Hotfix 1 試験カード

## 目的
DLCの「インストール状態」と「現在のセーブ/セッションで滑走路がruntime公開されているか」を分離して確認する。

## 期待表示
AIRFIELDS上部に以下の2系統が別々に表示されること。

- `EXPANSIONS: MH ... | BG ...`
- `DLC RUNWAY: DESSERT AIRFIELD ... | RUNTIME x/y`

## ケース
1. Making History / Breaking Ground導入済みでKSPを再起動する。
   - MH = `LOADED`
   - BG = `LOADED`
2. Making History導入済みだが現在のsaveでDessert Airfieldがruntime公開されない場合。
   - DLC RUNWAY = `SAVE-LOCKED / NOT EXPOSED`
   - `RUNTIME 0/1`でもDLC未導入とは解釈しない。
3. Dessert Airfieldがruntime公開されるsave/設定の場合。
   - DLC RUNWAY = `AVAILABLE`
4. KSP起動中にDLCを追加しAIRFIELDS RELOAD/RESCANした場合。
   - disk scan後 `INSTALLED / RESTART REQUIRED` を許容する。
   - KSP再起動後は `LOADED` へ変わること。
5. DLC滑走路が見えても、自動CERTIFIEDにならないこと。
   - 非Stockは `MANUAL A/B REQUIRED` のまま。

## 退行禁止
- Candidate 6 field-verified runway defaultsを変更しない。
- CPU terrain fallbackを復活させない。
- Terrain/ND/AP/AA/PROTECTの挙動を変更しない。
