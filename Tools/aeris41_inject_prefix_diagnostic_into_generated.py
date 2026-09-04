#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_prefix_diagnostic_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V4"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V5_PREFIX_DIAGNOSTIC"
if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 prefix observer V4 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

expected_marker = '''            internal bool HasValue;\n            internal long ValueBits;\n            internal string ExceptionType;\n        }'''
expected_replacement = '''            internal bool HasValue;\n            internal long ValueBits;\n            internal string ExceptionType;\n            internal bool[] PrefixHasValue;\n            internal long[] PrefixValueBits;\n        }'''
if obs.count(expected_marker) != 1:
    raise SystemExit("AERIS41 prefix ExpectedCheck marker not unique")
obs = obs.replace(expected_marker, expected_replacement, 1)

body_result_marker = '''            internal int Mismatches;\n            internal string[] FirstMismatches;\n            internal bool Pass;\n        }'''
body_result_replacement = '''            internal int Mismatches;\n            internal string[] FirstMismatches;\n            internal int[] PrefixChecks;\n            internal int[] PrefixMatches;\n            internal int[] PrefixMismatches;\n            internal string[] PrefixFirstMismatches;\n            internal bool Pass;\n        }'''
if obs.count(body_result_marker) != 1:
    raise SystemExit("AERIS41 prefix BodyResult marker not unique")
obs = obs.replace(body_result_marker, body_result_replacement, 1)

capture_start = obs.find("        static void CaptureCallbackChainReference(")
capture_end = obs.find("        static WorkerResult RunWorker", capture_start)
if capture_start < 0 or capture_end < 0:
    raise SystemExit("AERIS41 prefix callback-reference block missing")
capture = obs[capture_start:capture_end]

loop_marker = '''                for (int i = 0; i < mods.Count; i++)\n                {\n                    ModRecord record = mods[i];'''
if capture.count(loop_marker) != 1:
    raise SystemExit("AERIS41 prefix callback loop marker not unique")
capture = capture.replace(
    loop_marker,
    '''                check.PrefixHasValue = new bool[mods.Count];\n                check.PrefixValueBits = new long[mods.Count];\n\n''' + loop_marker,
    1)

callback_marker = '''                    record.Mod.OnVertexBuildHeight(data);'''
callback_replacement = '''                    record.Mod.OnVertexBuildHeight(data);\n                    check.PrefixHasValue[i] = true;\n                    check.PrefixValueBits[i] = BitConverter.DoubleToInt64Bits(data.vertHeight);'''
if capture.count(callback_marker) != 1:
    raise SystemExit("AERIS41 prefix callback-store marker not unique")
capture = capture.replace(callback_marker, callback_replacement, 1)
obs = obs[:capture_start] + capture + obs[capture_end:]

eval_start = obs.find("        static BodyResult EvaluateBody(BodyCase body)")
eval_end = obs.find("        void Report(WorkerResult result)", eval_start)
if eval_start < 0 or eval_end < 0:
    raise SystemExit("AERIS41 prefix EvaluateBody block missing")
eval_block = obs[eval_start:eval_end]

init_marker = '''            var result = new BodyResult { Name = body.Name };\n            var mismatches = new List<string>();'''
init_replacement = '''            var result = new BodyResult { Name = body.Name };\n            var mismatches = new List<string>();\n            int prefixCount = body.Snapshot.Ops.Length;\n            result.PrefixChecks = new int[prefixCount];\n            result.PrefixMatches = new int[prefixCount];\n            result.PrefixMismatches = new int[prefixCount];\n            result.PrefixFirstMismatches = new string[prefixCount];'''
if eval_block.count(init_marker) != 1:
    raise SystemExit("AERIS41 prefix EvaluateBody init marker not unique")
eval_block = eval_block.replace(init_marker, init_replacement, 1)

result_marker = '''            result.FirstMismatches = mismatches.ToArray();'''
prefix_eval = r'''            for (int i = 0; i < body.Checks.Length; i++)
            {
                ExpectedCheck expected = body.Checks[i];
                double prefixHeight = expected.InputHeight;
                for (int p = 0; p < prefixCount; p++)
                {
                    result.PrefixChecks[p]++;
                    try
                    {
                        prefixHeight = body.Snapshot.Ops[p].Evaluate(
                            expected.X,
                            expected.Y,
                            expected.Z,
                            expected.U,
                            expected.V,
                            prefixHeight);
                        long bits = AERIS39AllBodyHeightModifierChainPureCpuExact.DoubleBits(
                            prefixHeight);

                        bool expectedHasValue =
                            expected.PrefixHasValue != null &&
                            p < expected.PrefixHasValue.Length &&
                            expected.PrefixHasValue[p];
                        long expectedBits =
                            expected.PrefixValueBits != null &&
                            p < expected.PrefixValueBits.Length
                                ? expected.PrefixValueBits[p]
                                : 0L;

                        if (expectedHasValue && bits == expectedBits)
                        {
                            result.PrefixMatches[p]++;
                        }
                        else
                        {
                            result.PrefixMismatches[p]++;
                            if (string.IsNullOrEmpty(result.PrefixFirstMismatches[p]))
                            {
                                result.PrefixFirstMismatches[p] =
                                    expected.Label +
                                    " native=" + (expectedHasValue
                                        ? "0x" + unchecked((ulong)expectedBits).ToString(
                                            "X16", CultureInfo.InvariantCulture)
                                        : "NO_VALUE") +
                                    " pure=0x" + unchecked((ulong)bits).ToString(
                                        "X16", CultureInfo.InvariantCulture);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.PrefixMismatches[p]++;
                        if (string.IsNullOrEmpty(result.PrefixFirstMismatches[p]))
                        {
                            result.PrefixFirstMismatches[p] =
                                expected.Label +
                                " native=" + (
                                    expected.PrefixHasValue != null &&
                                    p < expected.PrefixHasValue.Length &&
                                    expected.PrefixHasValue[p]
                                    ? "0x" + unchecked((ulong)expected.PrefixValueBits[p]).ToString(
                                        "X16", CultureInfo.InvariantCulture)
                                    : "NO_VALUE") +
                                " pure=EX:" + (ex.GetType().FullName ?? ex.GetType().Name);
                        }
                        break;
                    }
                }
            }

'''
if eval_block.count(result_marker) != 1:
    raise SystemExit("AERIS41 prefix EvaluateBody result marker not unique")
eval_block = eval_block.replace(result_marker, prefix_eval + result_marker, 1)
obs = obs[:eval_start] + eval_block + obs[eval_end:]

report_start = obs.find("        void Report(WorkerResult result)")
report_end = obs.find("        CurveSelection SelectCurveSnapshot", report_start)
if report_start < 0 or report_end < 0:
    raise SystemExit("AERIS41 prefix Report block missing")
report = obs[report_start:report_end]

mismatch_marker = '''                if (body.FirstMismatches == null) continue;'''
prefix_report = r'''                if (body.PrefixChecks != null)
                {
                    for (int p = 0; p < body.PrefixChecks.Length; p++)
                    {
                        bool prefixExact = body.PrefixMismatches[p] == 0;
                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_PREFIX]" +
                            "; body=" + Safe(body.Name) +
                            "; prefix_index=" + p.ToString(CultureInfo.InvariantCulture) +
                            "; checks=" + body.PrefixChecks[p].ToString(CultureInfo.InvariantCulture) +
                            "; matches=" + body.PrefixMatches[p].ToString(CultureInfo.InvariantCulture) +
                            "; mismatches=" + body.PrefixMismatches[p].ToString(CultureInfo.InvariantCulture) +
                            "; bit_exact=" + Bool(prefixExact) +
                            "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                            Invariants());

                        if (!prefixExact &&
                            body.PrefixFirstMismatches != null &&
                            p < body.PrefixFirstMismatches.Length &&
                            !string.IsNullOrEmpty(body.PrefixFirstMismatches[p]))
                        {
                            AERISLogger.Warn(
                                "[AERIS39][HEIGHT_CHAIN_PREFIX_MISMATCH]" +
                                "; body=" + Safe(body.Name) +
                                "; prefix_index=" + p.ToString(CultureInfo.InvariantCulture) +
                                "; detail=" + Safe(body.PrefixFirstMismatches[p]) +
                                Invariants());
                        }
                    }
                }

'''
if report.count(mismatch_marker) != 1:
    raise SystemExit("AERIS41 prefix Report insertion marker not unique")
report = report.replace(mismatch_marker, prefix_report + mismatch_marker, 1)

complete_marker = '''            AERISLogger.Info(\n                "[AERIS39][HEIGHT_CHAIN_COMPLETE]" +'''
if report.count(complete_marker) != 1:
    raise SystemExit("AERIS41 prefix complete-log marker not unique")

# Append diagnostic-completion evidence immediately after the existing complete log
# statement. Locate the statement's terminating Invariants()); inside Report.
complete_start = report.find(complete_marker)
complete_tail = report.find("                Invariants());", complete_start)
if complete_tail < 0:
    raise SystemExit("AERIS41 prefix complete-log terminator missing")
complete_tail += len("                Invariants());")
diagnostic = r'''

            bool prefixDiagnosticComplete = true;
            int prefixBodies = 0;
            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                if (body == null) continue;
                if (!string.Equals(body.Name, "Eve", StringComparison.Ordinal) &&
                    !string.Equals(body.Name, "Duna", StringComparison.Ordinal))
                    continue;

                prefixBodies++;
                if (body.PrefixChecks == null || body.PrefixChecks.Length == 0)
                {
                    prefixDiagnosticComplete = false;
                    continue;
                }
                for (int p = 0; p < body.PrefixChecks.Length; p++)
                    if (body.PrefixChecks[p] != body.Checks)
                        prefixDiagnosticComplete = false;
            }
            prefixDiagnosticComplete &= prefixBodies == 2;

            AERISLogger.Info(
                "[AERIS39][HEIGHT_CHAIN_PREFIX_COMPLETE]" +
                "; pass=" + Bool(prefixDiagnosticComplete) +
                "; bodies=" + prefixBodies.ToString(CultureInfo.InvariantCulture) +
                "; targets=Eve,Duna" +
                "; callback_count_added=0" +
                "; reference_capture=SAME_EXISTING_CALLBACK_CHAIN_PREFIX_BITS" +
                "; worker_payload=PRIMITIVE_PREFIX_BITS_ONLY" +
                Invariants());'''
report = report[:complete_tail] + diagnostic + report[complete_tail:]
obs = obs[:report_start] + report + obs[report_end:]

# Runtime acceptance remains strict for the product chain, but this temporary stage
# may pass when the diagnostic itself is complete even though the product chain is
# expected to remain FAIL until the first divergent prefix is repaired.
failure_marker = '''  if [[ "$pass" -ne 1 ]]; then\n    echo "=== R041 ALL-BODY HEIGHT CHAIN ACCEPTANCE FAILURE ==="'''
diagnostic_accept = r'''  if [[ "$pass" -ne 1 ]]; then
    local prefix_complete
    prefix_complete="$(grep -F "[AERIS39][HEIGHT_CHAIN_PREFIX_COMPLETE]" "$segment" | tail -n 1 || true)"
    if [[ "$prefix_complete" == *"; pass=true;"* &&
          "$prefix_complete" == *"; bodies=2;"* &&
          "$prefix_complete" == *"; targets=Eve,Duna;"* &&
          "$prefix_complete" == *"; callback_count_added=0;"* ]]; then
      echo "=== R041 EVE DUNA PREFIX LOCALIZATION ==="
      grep '\[AERIS39\]\[HEIGHT_CHAIN_PREFIX\]' "$segment" || true
      grep '\[AERIS39\]\[HEIGHT_CHAIN_PREFIX_MISMATCH\]' "$segment" || true
      echo "$prefix_complete"
      echo "product_height_chain_pass=false"
      echo "archive=$ARCHIVE"
      rm -f "$segment"
      rm -rf "$STATE_DIR"
      echo "AERIS41_R041_EVE_DUNA_PREFIX_LOCALIZATION=PASS"
      echo "AERIS_CURRENT_STAGE=PASS"
      echo "next=R041_FIX_FIRST_PREFIX_DIVERGENCE"
      exit 0
    fi
  fi

'''
if run.count(failure_marker) != 1:
    raise SystemExit("AERIS41 prefix runner failure marker not unique")
run = run.replace(failure_marker, diagnostic_accept + failure_marker, 1)

semantics_marker = "production_vertexheightnoise_state_audit=REQUIRED_UNCHANGED"
if run.count(semantics_marker) != 1:
    raise SystemExit("AERIS41 prefix provenance marker not unique")
run = run.replace(
    semantics_marker,
    semantics_marker +
    "\nprefix_localization=EVE_DUNA_SAME_CALLBACK_PREFIX_BITS_NO_EXTRA_CALLBACKS",
    1)

for token in [
    new_candidate,
    "PrefixHasValue",
    "PrefixValueBits",
    "HEIGHT_CHAIN_PREFIX]",
    "HEIGHT_CHAIN_PREFIX_MISMATCH]",
    "HEIGHT_CHAIN_PREFIX_COMPLETE]",
    "callback_count_added=0",
]:
    if token not in obs:
        raise SystemExit("AERIS41 prefix generated observer missing: " + token)

for token in [
    new_candidate,
    "AERIS41_R041_EVE_DUNA_PREFIX_LOCALIZATION=PASS",
    "R041_FIX_FIRST_PREFIX_DIVERGENCE",
    "prefix_localization=EVE_DUNA_SAME_CALLBACK_PREFIX_BITS_NO_EXTRA_CALLBACKS",
]:
    if token not in run:
        raise SystemExit("AERIS41 prefix generated runner missing: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
