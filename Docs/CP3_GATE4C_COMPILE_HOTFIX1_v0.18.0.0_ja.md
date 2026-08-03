# CP3 Gate 4C Compile Hotfix 1

Gate 4C の `build_ubuntu.sh` が旧 Gate 4B の受入 runner を実行し、現行 Gate 4C のバージョン表記を旧テストが FAIL と判定してビルド前に停止する不具合を修正する。

修正は build entrypoint とバージョン／受入検査のみ。Terrain / ND / runway / AA / AP / PROTECT / LAND の実行ロジックは変更しない。
