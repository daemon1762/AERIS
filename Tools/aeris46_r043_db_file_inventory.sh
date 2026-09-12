#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris46_r043_db_file_inventory.sh <KSP root>" >&2
  exit 2
fi

KSP="$1"
DB="$KSP/GameData/AERISFlightControl/PluginData/TerrainPreloadDatabaseV3"

echo "=== AERIS46 DB FILE INVENTORY ==="
echo "KSP=$KSP"
echo "DB=$DB"

[[ -d "$DB" ]] || { echo "EXISTS=false"; exit 3; }

echo "EXISTS=true"
echo "du_bytes=$(du -sb "$DB" | awk '{print $1}')"
echo "file_count=$(find "$DB" -type f | wc -l)"
echo

python3 - "$DB" <<'PY'
import os, sys
from collections import defaultdict

root=sys.argv[1]
groups=defaultdict(lambda:[0,0])
files=[]

for base, dirs, names in os.walk(root):
    for n in names:
        p=os.path.join(base,n)
        try:
            sz=os.path.getsize(p)
        except OSError:
            sz=0
        rel=os.path.relpath(p,root)
        files.append((rel,sz))

        low=n.lower()
        if low.endswith(".atb.bak"):
            g="chunk_atb_bak"
        elif low.endswith(".atb"):
            g="chunk_atb"
        elif low.endswith(".pending"):
            g="journal_pending"
        elif low.endswith(".tmp"):
            g="journal_tmp"
        elif low=="manifest.atm":
            g="manifest"
        elif low=="manifest.atm.bak":
            g="manifest_bak"
        elif low=="preload_state.aps":
            g="state"
        elif low=="preload_state.aps.bak":
            g="state_bak"
        else:
            g="other"
        groups[g][0]+=1
        groups[g][1]+=sz

for g in (
    "chunk_atb","chunk_atb_bak","journal_pending","journal_tmp",
    "manifest","manifest_bak","state","state_bak","other"
):
    c,b=groups[g]
    print("%s_count=%d" % (g,c))
    print("%s_bytes=%d" % (g,b))

print()
print("=== TOP 20 NON-ATB FILES BY SIZE ===")
other=[x for x in files if not x[0].lower().endswith(".atb")]
for rel,sz in sorted(other,key=lambda x:x[1],reverse=True)[:20]:
    print("%12d %s" % (sz,rel))

print()
print("=== CHUNK TREE BY ENV-DIRECTORY ===")
dirs=defaultdict(lambda:[0,0])
for rel,sz in files:
    parts=rel.replace("\\","/").split("/")
    if len(parts)>=4 and parts[0]=="Chunks" and rel.lower().endswith(".atb"):
        key=parts[1]
        dirs[key][0]+=1
        dirs[key][1]+=sz
for key,(c,b) in sorted(dirs.items(), key=lambda kv:(-kv[1][1],kv[0])):
    print("%s files=%d bytes=%d" % (key,c,b))

print()
print("KSP_NOT_LAUNCHED=true")
print("FILES_MODIFIED=false")
PY
