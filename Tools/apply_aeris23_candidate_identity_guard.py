#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
build_path = ROOT / 'build_ubuntu.sh'
bootstrap_path = ROOT / 'Source/AERISFlightControl/Core/AERISBootstrap.cs'

CANDIDATE = 'AERIS23_SINGLE_AUTHORITY_TERRAIN_PACK'


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        print('[PASS] ' + label + ' already applied')
        return text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'[AERIS23 IDENTITY GUARD] {label}: expected 1 anchor, found {count}')
    print('[PASS] ' + label)
    return text.replace(old, new, 1)

# -----------------------------------------------------------------------------
# build_ubuntu.sh: candidate source preflight + source identity + DLL hash match.
# -----------------------------------------------------------------------------
b = build_path.read_text()

semver_anchor = '''SEMVER="$(python3 -c 'import json,sys; v=json.load(open(sys.argv[1]))["VERSION"]; print("%s.%s.%s.%s"%(v["MAJOR"],v["MINOR"],v["PATCH"],v.get("BUILD",0)))' "$VERSION_FILE")"\n'''
semver_new = semver_anchor + r'''CANDIDATE_NAME="AERIS23_SINGLE_AUTHORITY_TERRAIN_PACK"
SOURCE_GIT_SHA="$(git -C "$ROOT" rev-parse HEAD)"
SOURCE_TREE_SHA256="$(
  {
    printf '%s\n' "$SOURCE_GIT_SHA"
    git -C "$ROOT" diff --no-ext-diff --binary HEAD -- \
      Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs \
      Source/AERISFlightControl/Core/AERISBootstrap.cs \
      build_ubuntu.sh
  } | sha256sum | awk '{print $1}'
)"
echo "[AERIS23_CANDIDATE_SOURCE] candidate=$CANDIDATE_NAME; git=$SOURCE_GIT_SHA; source_tree_sha256=$SOURCE_TREE_SHA256"
'''
b = replace_once(b, semver_anchor, semver_new, 'build source identity')

# After the normal generated build-version file is written, append immutable candidate
# identity constants into the generated class. The DLL can then identify itself at runtime.
gen_anchor = '''printf 'using System.Reflection;\\n[assembly: AssemblyVersion("%s")]\\n[assembly: AssemblyFileVersion("%s")]\\nnamespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "%s"; internal const string Display = "%s"; internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT"; internal const string Cp2FrozenBaselineDisplay = "%s"; } }\\n' "$SEMVER" "$SEMVER" "$SEMVER" "$DISPLAY" "$BASELINE_DISPLAY" > "$GEN"\n'''
identity_append = gen_anchor + r'''python3 - "$GEN" "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" <<'PYAERISCANDIDATE'
from pathlib import Path
import sys
path = Path(sys.argv[1])
candidate, git_sha, tree_sha = sys.argv[2:5]
text = path.read_text()
needle = 'internal const string Cp2FrozenBaselineDisplay = '
if needle not in text:
    raise SystemExit('[AERIS23] generated identity insertion anchor missing')
insert = (
    'internal const string CandidateName = "' + candidate + '"; '
    'internal const string SourceGitSha = "' + git_sha + '"; '
    'internal const string SourceTreeSha256 = "' + tree_sha + '"; '
)
text = text.replace(needle, insert + needle, 1)
path.write_text(text)
PYAERISCANDIDATE
'''
b = replace_once(b, gen_anchor, identity_append, 'generated candidate identity')

prebuild_anchor = '''PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/run_v01800_operation_health_pass3_prebuild.py"\n'''
prebuild_new = r'''PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_single_authority_source.py"
''' + prebuild_anchor
b = replace_once(b, prebuild_anchor, prebuild_new, 'candidate source preflight before build')

built_copy_anchor = '''cp -f bin/Release/AERISFlightControl.dll "$ROOT/GameData/AERISFlightControl/Plugins/"\n'''
built_copy_new = built_copy_anchor + r'''BUILT_DLL="$ROOT/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
BUILT_DLL_SHA256="$(sha256sum "$BUILT_DLL" | awk '{print $1}')"
printf 'candidate=%s\ngit=%s\nsource_tree_sha256=%s\nbuilt_dll_sha256=%s\n' \
  "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" "$BUILT_DLL_SHA256" \
  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
echo "[AERIS23_CANDIDATE_BUILT] candidate=$CANDIDATE_NAME; dll_sha256=$BUILT_DLL_SHA256"
'''
b = replace_once(b, built_copy_anchor, built_copy_new, 'built DLL identity')

install_echo_anchor = '''echo "[AERIS] Installed $DISPLAY: $KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"\n'''
install_echo_new = r'''INSTALLED_DLL="$KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
INSTALLED_DLL_SHA256="$(sha256sum "$INSTALLED_DLL" | awk '{print $1}')"
if [[ "$BUILT_DLL_SHA256" != "$INSTALLED_DLL_SHA256" ]]; then
  echo "[AERIS23] ERROR: installed DLL hash mismatch; built=$BUILT_DLL_SHA256 installed=$INSTALLED_DLL_SHA256" >&2
  exit 1
fi
echo "[AERIS23_CANDIDATE_INSTALLED] candidate=$CANDIDATE_NAME; git=$SOURCE_GIT_SHA; source_tree_sha256=$SOURCE_TREE_SHA256; built_dll_sha256=$BUILT_DLL_SHA256; installed_dll_sha256=$INSTALLED_DLL_SHA256; MATCH=YES"
''' + install_echo_anchor
b = replace_once(b, install_echo_anchor, install_echo_new, 'installed DLL SHA verification')

build_path.write_text(b)

# -----------------------------------------------------------------------------
# Runtime: compute the SHA-256 of the assembly actually loaded by KSP and publish it
# beside the candidate/source identity compiled into the DLL.
# -----------------------------------------------------------------------------
c = bootstrap_path.read_text()
c = replace_once(c, 'using System;\nusing System.Collections;\n',
                 'using System;\nusing System.Collections;\nusing System.IO;\nusing System.Security.Cryptography;\n',
                 'runtime SHA imports')

awake_anchor = '''  void Awake(){\n'''
runtime_hash_method = r'''  static string RuntimeAssemblySha256(){
   try{
    string path=typeof(AERISBootstrap).Assembly.Location;
    if(string.IsNullOrEmpty(path)||!File.Exists(path))return "UNAVAILABLE";
    using(var stream=File.OpenRead(path))using(var sha=SHA256.Create()){
     byte[] hash=sha.ComputeHash(stream);
     return BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
    }
   }catch(Exception ex){return "ERROR_"+ex.GetType().Name;}
  }
'''
c = replace_once(c, awake_anchor, runtime_hash_method + awake_anchor,
                 'runtime assembly SHA helper')

log_anchor = '''  AERISLogger.Info(""+AERISBuildVersion.Display+" loaded.'''
runtime_log = '''  AERISLogger.Info("[AERIS23_RUNTIME_CANDIDATE] candidate="+AERISBuildVersion.CandidateName+"; git="+AERISBuildVersion.SourceGitSha+"; source_tree_sha256="+AERISBuildVersion.SourceTreeSha256+"; dll_sha256="+RuntimeAssemblySha256());\n'''
c = replace_once(c, log_anchor, runtime_log + log_anchor,
                 'runtime candidate identity log')

bootstrap_path.write_text(c)
print('[AERIS23 IDENTITY GUARD] applied for ' + CANDIDATE)
