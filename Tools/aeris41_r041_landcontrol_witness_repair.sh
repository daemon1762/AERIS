#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_landcontrol_witness_repair.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
BASE_RUNNER="$ROOT/Tools/aeris39_r041_allbody_height_modifier_chain_shadow.sh"
CANONICAL_OBSERVER="$ROOT/Source/AERISFlightControl/Terrain/AERIS39AllBodyHeightModifierChainShadowObserver.cs"
PURE="$ROOT/Source/AERISFlightControl/Terrain/AERIS39LandControlPureCpuExact.cs"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before AERIS41 LandControl witness repair" >&2
  git status -sb >&2
  exit 11
}

[[ -f "$BASE_RUNNER" ]] || { echo "STOP: base R041 runner missing" >&2; exit 12; }
[[ -f "$CANONICAL_OBSERVER" ]] || { echo "STOP: canonical observer missing" >&2; exit 13; }
[[ -f "$PURE" ]] || { echo "STOP: LandControl pure source missing" >&2; exit 14; }

grep -Fq 'double vLat = v + ((double)LatitudeBlend * latitudeNoise);' "$PURE" || {
  echo "STOP: per-vertex LandControl latitude semantics not installed" >&2
  exit 15
}
grep -Fq 'double vLon = u + ((double)LongitudeBlend * longitudeNoise);' "$PURE" || {
  echo "STOP: per-vertex LandControl longitude semantics not installed" >&2
  exit 16
}
if grep -Fq 'SphereSx' "$PURE" || grep -Fq 'SphereSy' "$PURE"; then
  echo "STOP: body-wide LandControl sx/sy survived pure-source repair" >&2
  exit 17
fi

TMPDIR="$(mktemp -d /tmp/AERIS41_R041_LANDCONTROL.XXXXXX)"
SHADOW_OBSERVER="$TMPDIR/AERIS39AllBodyHeightModifierChainShadowObserver.AERIS41.cs"
SHADOW_RUNNER="$TMPDIR/aeris39_r041_allbody_height_modifier_chain_shadow.AERIS41.sh"
cleanup() { rm -rf "$TMPDIR"; }
trap cleanup EXIT

python3 - "$CANONICAL_OBSERVER" "$SHADOW_OBSERVER" <<'PY'
import pathlib, sys

source_path = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
src = source_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V1"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V2"
old_reference = "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN"
new_reference = "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_LANDCONTROL_MANAGED_SHADOW"

if src.count(old_candidate) != 1:
    raise SystemExit("AERIS41 observer candidate marker not unique")
src = src.replace(old_candidate, new_candidate, 1)
src = src.replace(old_reference, new_reference)

mod_record_old = '''        sealed class ModRecord
        {
            internal PQSMod Mod;
            internal string TypeName;
            internal string ShortTypeName;
            internal int Order;
            internal int Index;
        }'''
mod_record_new = '''        sealed class ModRecord
        {
            // Mod is the callback target. For PQSLandControl it is replaced, only
            // after primitive snapshot capture, by an isolated managed shadow.
            internal PQSMod Mod;
            internal string TypeName;
            internal string ShortTypeName;
            internal int Order;
            internal int Index;

            // Main-thread-only LandControl production/isolation audit state.
            internal PQSMod ProductionMod;
            internal object ProductionSphere;
            internal object ReferenceSphere;
            internal object ProductionLandClasses;
            internal object ProductionLcList;
            internal string ProductionStateBefore;
            internal long ProductionSphereSxBits;
            internal long ProductionSphereSyBits;
        }'''
if src.count(mod_record_old) != 1:
    raise SystemExit("AERIS41 ModRecord insertion point not unique")
src = src.replace(mod_record_old, mod_record_new, 1)

callback_old = '''                for (int i = 0; i < mods.Count; i++)
                    mods[i].Mod.OnVertexBuildHeight(data);'''
callback_new = '''                for (int i = 0; i < mods.Count; i++)
                {
                    ModRecord record = mods[i];
                    if (record.ProductionMod != null)
                    {
                        // Stock PQS.BuildVertexMapCoords establishes sphere.sx/sy
                        // and then copies them to data.u/data.v for this vertex.
                        // The isolated PQS shadow therefore receives the exact
                        // per-vertex map-coordinate state without touching the
                        // production PQS instance.
                        WriteMemberStrict(record.ReferenceSphere, "sx", data.u);
                        WriteMemberStrict(record.ReferenceSphere, "sy", data.v);
                    }
                    record.Mod.OnVertexBuildHeight(data);
                }'''
if src.count(callback_old) != 1:
    raise SystemExit("AERIS41 callback loop insertion point not unique")
src = src.replace(callback_old, callback_new, 1)

audit_call = '''            string topologyText = string.Join(",", topology.ToArray());'''
if src.count(audit_call) != 1:
    raise SystemExit("AERIS41 audit call insertion point not unique")
src = src.replace(
    audit_call,
    '''            AuditLandControlReferenceIsolation(bodyName, mods);\n\n''' + audit_call,
    1)

helper_marker = '''        static IList GetModifierList(object pqs)'''
if src.count(helper_marker) != 1:
    raise SystemExit("AERIS41 helper insertion point not unique")
helpers = r'''        static object ManagedMemberwiseClone(object source)
        {
            if (source == null)
                throw new ArgumentNullException("source");

            MethodInfo method = typeof(object).GetMethod(
                "MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException("System.Object", "MemberwiseClone");

            object clone = method.Invoke(source, null);
            if (clone == null || ReferenceEquals(source, clone))
                throw new InvalidOperationException("MANAGED_MEMBERWISE_CLONE_FAILED");
            return clone;
        }

        static void WriteMemberStrict(object target, string name, object value)
        {
            if (target == null)
                throw new ArgumentNullException("target");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = FindField(target.GetType(), name, flags);
            if (field != null)
            {
                field.SetValue(field.IsStatic ? null : target, value);
                return;
            }

            PropertyInfo property = FindProperty(target.GetType(), name);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                MethodInfo setter = property.GetSetMethod(true);
                if (setter != null)
                {
                    property.SetValue(setter.IsStatic ? null : target, value, null);
                    return;
                }
            }

            throw new MissingMemberException(TypeName(target.GetType()), name);
        }

        static PQSMod CreateIsolatedLandControlReference(
            PQSMod production,
            object productionSphere,
            out object shadowSphere)
        {
            if (production == null)
                throw new ArgumentNullException("production");
            if (productionSphere == null)
                throw new ArgumentNullException("productionSphere");

            PQSMod shadow = ManagedMemberwiseClone(production) as PQSMod;
            if (shadow == null || ReferenceEquals(shadow, production))
                throw new InvalidOperationException("LANDCONTROL_REFERENCE_MOD_NOT_ISOLATED");

            shadowSphere = ManagedMemberwiseClone(productionSphere);
            if (ReferenceEquals(shadowSphere, productionSphere))
                throw new InvalidOperationException("LANDCONTROL_REFERENCE_SPHERE_NOT_ISOLATED");
            WriteMemberStrict(shadow, "sphere", shadowSphere);

            Array productionClasses = RequireMember(production, "landClasses") as Array;
            if (productionClasses == null)
                throw new InvalidOperationException("LANDCONTROL_PRODUCTION_CLASSES_MISSING");

            Array shadowClasses = (Array)productionClasses.Clone();
            for (int i = 0; i < productionClasses.Length; i++)
            {
                object productionClass = productionClasses.GetValue(i);
                if (productionClass == null)
                    throw new InvalidOperationException(
                        "LANDCONTROL_PRODUCTION_NULL_CLASS_" +
                        i.ToString(CultureInfo.InvariantCulture));
                object shadowClass = ManagedMemberwiseClone(productionClass);
                if (ReferenceEquals(productionClass, shadowClass))
                    throw new InvalidOperationException("LANDCONTROL_CLASS_CLONE_NOT_ISOLATED");
                shadowClasses.SetValue(shadowClass, i);
            }
            WriteMemberStrict(shadow, "landClasses", shadowClasses);

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic;
            FieldInfo listField = FindField(production.GetType(), "lcList", flags);
            if (listField == null)
                throw new MissingFieldException(TypeName(production.GetType()), "lcList");
            object shadowList = Activator.CreateInstance(listField.FieldType);
            if (shadowList == null)
                throw new InvalidOperationException("LANDCONTROL_REFERENCE_LIST_CREATE_FAILED");
            WriteMemberStrict(shadow, "lcList", shadowList);

            return shadow;
        }

        static string LandControlStateFingerprint(PQSMod mod)
        {
            if (mod == null)
                throw new ArgumentNullException("mod");

            var fields = new List<string>();
            fields.Add("lcListCount=" +
                ReadIntDefault(mod, "lcListCount", int.MinValue).ToString(CultureInfo.InvariantCulture));

            object totalDeltaRaw = ReadMember(mod, "totalDelta");
            double totalDelta = totalDeltaRaw == null
                ? double.NaN
                : Convert.ToDouble(totalDeltaRaw, CultureInfo.InvariantCulture);
            fields.Add("totalDeltaBits=" +
                unchecked((ulong)BitConverter.DoubleToInt64Bits(totalDelta)).ToString(
                    "X16", CultureInfo.InvariantCulture));

            Array classes = RequireMember(mod, "landClasses") as Array;
            if (classes == null)
                throw new InvalidOperationException("LANDCONTROL_FINGERPRINT_CLASSES_MISSING");
            fields.Add("classes=" + classes.Length.ToString(CultureInfo.InvariantCulture));

            IList list = ReadMember(mod, "lcList") as IList;
            fields.Add("listCount=" +
                (list == null ? -1 : list.Count).ToString(CultureInfo.InvariantCulture));
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    int classIndex = -1;
                    object entry = list[i];
                    for (int c = 0; c < classes.Length; c++)
                    {
                        if (ReferenceEquals(entry, classes.GetValue(c)))
                        {
                            classIndex = c;
                            break;
                        }
                    }
                    fields.Add("list[" + i.ToString(CultureInfo.InvariantCulture) + "]=" +
                        classIndex.ToString(CultureInfo.InvariantCulture));
                }
            }

            string[] mutableClassFields = { "altDelta", "latDelta", "lonDelta", "delta" };
            for (int i = 0; i < classes.Length; i++)
            {
                object lc = classes.GetValue(i);
                if (lc == null)
                    throw new InvalidOperationException("LANDCONTROL_FINGERPRINT_NULL_CLASS");
                for (int f = 0; f < mutableClassFields.Length; f++)
                {
                    double value = ReadDouble(lc, mutableClassFields[f]);
                    fields.Add(
                        i.ToString(CultureInfo.InvariantCulture) + "." + mutableClassFields[f] + "=" +
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString(
                            "X16", CultureInfo.InvariantCulture));
                }
            }

            return string.Join("|", fields.ToArray());
        }

        static void AuditLandControlReferenceIsolation(
            string bodyName,
            List<ModRecord> mods)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                if (record.ProductionMod == null)
                    continue;

                object currentProductionSphere = ReadMember(record.ProductionMod, "sphere");
                object currentProductionClasses = ReadMember(record.ProductionMod, "landClasses");
                object currentProductionList = ReadMember(record.ProductionMod, "lcList");

                bool productionStateUnchanged =
                    ReferenceEquals(currentProductionSphere, record.ProductionSphere) &&
                    ReferenceEquals(currentProductionClasses, record.ProductionLandClasses) &&
                    ReferenceEquals(currentProductionList, record.ProductionLcList) &&
                    string.Equals(
                        LandControlStateFingerprint(record.ProductionMod),
                        record.ProductionStateBefore,
                        StringComparison.Ordinal);

                bool productionPqsSxSyUnchanged =
                    record.ProductionSphere != null &&
                    BitConverter.DoubleToInt64Bits(ReadDouble(record.ProductionSphere, "sx")) ==
                        record.ProductionSphereSxBits &&
                    BitConverter.DoubleToInt64Bits(ReadDouble(record.ProductionSphere, "sy")) ==
                        record.ProductionSphereSyBits;

                Array productionClasses = record.ProductionLandClasses as Array;
                Array shadowClasses = ReadMember(record.Mod, "landClasses") as Array;
                bool landClassObjectsIsolated =
                    productionClasses != null &&
                    shadowClasses != null &&
                    !ReferenceEquals(productionClasses, shadowClasses) &&
                    productionClasses.Length == shadowClasses.Length;
                if (landClassObjectsIsolated)
                {
                    for (int c = 0; c < productionClasses.Length; c++)
                    {
                        if (ReferenceEquals(productionClasses.GetValue(c), shadowClasses.GetValue(c)))
                        {
                            landClassObjectsIsolated = false;
                            break;
                        }
                    }
                }

                bool referenceObjectIsolated = !ReferenceEquals(record.ProductionMod, record.Mod);
                bool sphereObjectIsolated =
                    record.ReferenceSphere != null &&
                    !ReferenceEquals(record.ProductionSphere, record.ReferenceSphere) &&
                    ReferenceEquals(ReadMember(record.Mod, "sphere"), record.ReferenceSphere);
                bool lcListIsolated =
                    record.ProductionLcList != null &&
                    ReadMember(record.Mod, "lcList") != null &&
                    !ReferenceEquals(record.ProductionLcList, ReadMember(record.Mod, "lcList"));

                bool pass =
                    productionStateUnchanged &&
                    productionPqsSxSyUnchanged &&
                    referenceObjectIsolated &&
                    sphereObjectIsolated &&
                    landClassObjectsIsolated &&
                    lcListIsolated;

                AERISLogger.Info(
                    "[AERIS39][HEIGHT_CHAIN_LANDCONTROL_AUDIT]" +
                    "; body=" + Safe(bodyName) +
                    "; modifier_index=" + record.Index.ToString(CultureInfo.InvariantCulture) +
                    "; pass=" + Bool(pass) +
                    "; production_state_unchanged=" + Bool(productionStateUnchanged) +
                    "; production_pqs_sx_sy_unchanged=" + Bool(productionPqsSxSyUnchanged) +
                    "; reference_object_isolated=" + Bool(referenceObjectIsolated) +
                    "; sphere_object_isolated=" + Bool(sphereObjectIsolated) +
                    "; landclass_objects_isolated=" + Bool(landClassObjectsIsolated) +
                    "; lc_list_isolated=" + Bool(lcListIsolated) +
                    "; per_vertex_sx_sy=true" +
                    "; sx_sy_source=VERTEXBUILDDATA_UV_EQUIVALENT_BUILDVERTEXMAPCOORDS" +
                    "; reference_callback=REAL_PQSLANDCONTROL_ONVERTEXBUILDHEIGHT_MANAGED_SHADOW" +
                    Invariants());

                if (!pass)
                    throw new InvalidOperationException(
                        bodyName + "_LANDCONTROL_REFERENCE_ISOLATION_AUDIT_FAILED");
            }
        }

'''
src = src.replace(helper_marker, helpers + helper_marker, 1)

# Static fail-closed checks on the generated observer source.
required = [
    new_candidate,
    new_reference,
    "ProductionStateBefore",
    "CreateIsolatedLandControlReference",
    "WriteMemberStrict(record.ReferenceSphere, \"sx\", data.u);",
    "WriteMemberStrict(record.ReferenceSphere, \"sy\", data.v);",
    "AuditLandControlReferenceIsolation(bodyName, mods);",
    "HEIGHT_CHAIN_LANDCONTROL_AUDIT",
]
for token in required:
    if token not in src:
        raise SystemExit("AERIS41 transformed observer missing token: " + token)

out_path.write_text(src, encoding="utf-8")
PY

python3 - "$BASE_RUNNER" "$SHADOW_RUNNER" <<'PY'
import pathlib, sys

base = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
src = base.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V1"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V2"
old_reference = "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN"
new_reference = "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_LANDCONTROL_MANAGED_SHADOW"

root_old = 'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"'
root_new = 'ROOT="${AERIS41_ROOT:?}"'
if src.count(root_old) != 1:
    raise SystemExit("AERIS41 runner root marker not unique")
src = src.replace(root_old, root_new, 1)

observer_old = 'OBSERVER_SRC="$SRC/Terrain/AERIS39AllBodyHeightModifierChainShadowObserver.cs"'
observer_new = 'OBSERVER_SRC="${AERIS41_OBSERVER_SRC:?}"'
if src.count(observer_old) != 1:
    raise SystemExit("AERIS41 runner observer marker not unique")
src = src.replace(observer_old, observer_new, 1)

src = src.replace(old_candidate, new_candidate)
src = src.replace(old_reference, new_reference)

sxsy_old = '''                            ReadDouble(pqs, "sx"),
                            ReadDouble(pqs, "sy"),
'''
if src.count(sxsy_old) != 1:
    raise SystemExit("AERIS41 fixed sx/sy constructor marker not unique")
src = src.replace(sxsy_old, "", 1)

shadow_marker = '''                            SnapshotSimplex(RequireMember(record.Mod, "longitudeSimplex")),
                            pureClasses);

                        AERISLogger.Info('''
shadow_replacement = '''                            SnapshotSimplex(RequireMember(record.Mod, "longitudeSimplex")),
                            pureClasses);

                        // Preserve production identities/state, then move the REAL
                        // PQSLandControl callback onto a managed shadow. The shadow
                        // deep-isolates every callback-mutated object while sharing
                        // only read-only setup/noise/range payload.
                        record.ProductionMod = record.Mod;
                        record.ProductionSphere = ReadMember(record.Mod, "sphere") ?? pqs;
                        record.ProductionLandClasses = classes;
                        record.ProductionLcList = ReadMember(record.Mod, "lcList");
                        record.ProductionStateBefore = LandControlStateFingerprint(record.Mod);
                        record.ProductionSphereSxBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.ProductionSphere, "sx"));
                        record.ProductionSphereSyBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.ProductionSphere, "sy"));
                        object isolatedSphere;
                        record.Mod = CreateIsolatedLandControlReference(
                            record.Mod,
                            record.ProductionSphere,
                            out isolatedSphere);
                        record.ReferenceSphere = isolatedSphere;

                        AERISLogger.Info('''
if src.count(shadow_marker) != 1:
    raise SystemExit("AERIS41 LandControl shadow insertion marker not unique")
src = src.replace(shadow_marker, shadow_replacement, 1)

old_semantics = '                            "; sphere_sx_sy=RUNTIME_SNAPSHOTTED" +'
new_semantics = (
    '                            "; sphere_sx_sy=PER_VERTEX_DATA_UV_EQUIVALENT_BUILDVERTEXMAPCOORDS" +\n'
    '                            "; reference_object=ISOLATED_MANAGED_SHADOW" +'
)
if src.count(old_semantics) != 1:
    raise SystemExit("AERIS41 LandControl semantics log marker not unique")
src = src.replace(old_semantics, new_semantics, 1)

old_build_semantics = 'observer_build_semantics=CANONICAL_PLUS_DETERMINISTIC_LANDCONTROL_CASE_INJECTION'
new_build_semantics = '''observer_build_semantics=AERIS41_TRANSFORMED_CANONICAL_PLUS_LANDCONTROL_SHADOW_INJECTION
landcontrol_reference_architecture=REAL_CALLBACK_MANAGED_MEMBERWISE_SHADOW
landcontrol_sx_sy=PER_VERTEX_DATA_UV_EQUIVALENT_BUILDVERTEXMAPCOORDS
production_landcontrol_state_audit=REQUIRED_UNCHANGED'''
if src.count(old_build_semantics) != 1:
    raise SystemExit("AERIS41 provenance semantics marker not unique")
src = src.replace(old_build_semantics, new_build_semantics, 1)

old_runner_hash = 'runner_source_sha256=$(sha256sum "$ROOT/Tools/aeris39_r041_allbody_height_modifier_chain_shadow.sh" | awk \'{print $1}\')'
new_runner_hash = '''runner_source_sha256=$(sha256sum "${BASH_SOURCE[0]}" | awk '{print $1}')
stage_wrapper_source_sha256=$(sha256sum "$ROOT/Tools/aeris41_r041_landcontrol_witness_repair.sh" | awk '{print $1}')'''
if src.count(old_runner_hash) != 1:
    raise SystemExit("AERIS41 runner hash provenance marker not unique")
src = src.replace(old_runner_hash, new_runner_hash, 1)

accept_marker = '''  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"'''
accept_insert = r'''  for body in Kerbin Eve Duna; do
    local audit
    audit="$(grep -F "[AERIS39][HEIGHT_CHAIN_LANDCONTROL_AUDIT]" "$segment" | grep -F "; body=$body;" | tail -n 1 || true)"
    [[ -n "$audit" ]] || { pass=0; continue; }
    [[ "$audit" == *"; pass=true;"* ]] || pass=0
    [[ "$audit" == *"; production_state_unchanged=true;"* ]] || pass=0
    [[ "$audit" == *"; production_pqs_sx_sy_unchanged=true;"* ]] || pass=0
    [[ "$audit" == *"; reference_object_isolated=true;"* ]] || pass=0
    [[ "$audit" == *"; sphere_object_isolated=true;"* ]] || pass=0
    [[ "$audit" == *"; landclass_objects_isolated=true;"* ]] || pass=0
    [[ "$audit" == *"; lc_list_isolated=true;"* ]] || pass=0
    [[ "$audit" == *"; per_vertex_sx_sy=true;"* ]] || pass=0
  done

'''
if src.count(accept_marker) != 1:
    raise SystemExit("AERIS41 acceptance audit insertion marker not unique")
src = src.replace(accept_marker, accept_insert + accept_marker, 1)

required = [
    new_candidate,
    new_reference,
    'OBSERVER_SRC="${AERIS41_OBSERVER_SRC:?}"',
    "record.ProductionStateBefore = LandControlStateFingerprint(record.Mod);",
    "reference_object=ISOLATED_MANAGED_SHADOW",
    "production_landcontrol_state_audit=REQUIRED_UNCHANGED",
    "HEIGHT_CHAIN_LANDCONTROL_AUDIT",
]
for token in required:
    if token not in src:
        raise SystemExit("AERIS41 transformed runner missing token: " + token)

if 'ReadDouble(pqs, "sx")' in src or 'ReadDouble(pqs, "sy")' in src:
    raise SystemExit("AERIS41 transformed runner still contains fixed body sx/sy")
if "sphere_sx_sy=RUNTIME_SNAPSHOTTED" in src:
    raise SystemExit("AERIS41 transformed runner still advertises fixed sx/sy")

out.write_text(src, encoding="utf-8")
PY

chmod 0700 "$SHADOW_RUNNER"

# The generated files live outside the worktree, so the canonical branch remains
# clean while the existing R041 build/install/harvest gates are reused verbatim.
export AERIS41_ROOT="$ROOT"
export AERIS41_OBSERVER_SRC="$SHADOW_OBSERVER"

set +e
bash "$SHADOW_RUNNER" "$KSP"
RC=$?
set -e
exit "$RC"
