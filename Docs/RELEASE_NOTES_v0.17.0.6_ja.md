# AERIS Flight Control v0.17.0.6 — Canonical Source Geometry Cache Hotfix

## 実機証拠から確定した問題

v0.17.0.5ではProvider identity署名は二回のKSP起動で一致した。

```text
records=157 / runways=67
signature=320C1BCE0E271905
```

しかし永続cacheは二回目STARTUPで次の結果となった。

```text
MEASURED 12 / CACHE 4 / REVALIDATE 12
```

同一KSPプロセス内でも、Flightシーンを作り直した後の再走査では`MEASURED 11 / CACHE 5`となった。原因は、cache input fingerprintがProvider identityだけでなく、その時点で実体化されているKK／Unityのlive collider、live mesh、LOD集合とworld transform由来Geometryを含んでいたためである。Provider集合と最終認証結果は同じでも、ロード状態が変わるとfingerprintが変化した。

## 修正

### 1. cache鍵をCanonical Source Geometryへ変更

永続cache鍵は以下から生成する。

- 安定Provider identity
- Provider version
- Prefab優先のcanonical model root
- Mesh asset名、vertex数、submesh数、index数、topology、local bounds
- root相対transform
- material名とsemantic
- Collider型とlocal shape
- Provider配置CFGの位置・標高・方位・scale・宣言寸法
- LaunchPadTransform
- Survey definitionの方式、PairKey、長さ・幅・aspect・surface等の全安全条件

KKでPrefabが存在する場合、cache鍵はlive instanceやLOD activationに依存しない。PrefabがないStock施設では、facility rootのroot相対asset構成を使用する。

### 2. runtime Geometryは測量入力として維持

この変更は認証Geometryを削減しない。live collider、live mesh、Prefab meshからコピーしたPoints／Primitivesは従来どおりworkerの測量・認証入力として使用する。除外されるのは永続cache鍵からだけである。

### 3. 安全な失効条件

次の変更はcacheを無効化し再測量する。

- model／source path／Provider identity変更
- mesh asset構成、bounds、submesh、index topology変更
- collider shape変更
- 配置の有意な移動・回転・scale変更
- Survey method、PairKey、安全限界、surface変更
- algorithmVersion変更

### 4. 診断強化

exact cache miss時に以下を記録する。

```text
[AIRFIELD_CACHE] exact miss;
id=...;
reason=ALGORITHM ... または FINGERPRINT old -> new;
cachedPoints=...; livePoints=...;
cachedPrimitives=...; livePrimitives=...
```

## 互換性

- Cache schemaは4のまま
- algorithmVersionは`1660`
- v0.17.0.5のcacheは読めるが、初回だけ安全に再測量される
- v0.17.0.3以降のSTARTUP待機、schema 4完全往復、自動FlightData ZIPを維持
- AP/BANK制御則は変更なし
- LANDは観測・認証専用のまま
- 旧NAVは復活させていない
