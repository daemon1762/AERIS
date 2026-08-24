#!/usr/bin/env python3

from pathlib import Path
from collections import OrderedDict
import hashlib
import json
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: script <AERISFlightControl.log>")

log = Path(sys.argv[1])

if not log.is_file():
    raise SystemExit("ERROR log missing: " + str(log))

lines = log.read_text(errors="replace").splitlines()

BEGIN = "[R039][PRIMITIVE_DEEP_BEGIN]"
COMPLETE = "[R039][PRIMITIVE_DEEP_COMPLETE]"
FAIL = "[R039][PRIMITIVE_DEEP_FAIL]"
OBJECT = "[R039][PRIMITIVE_OBJECT]"
METHOD = "[R039][PRIMITIVE_METHOD]"
IL = "[R039][PRIMITIVE_IL_CHUNK]"
ARRAY = "[R039][PRIMITIVE_ARRAY]"
ARRAY_CHUNK = "[R039][PRIMITIVE_ARRAY_CHUNK]"
EXTERNAL = "[R039][PRIMITIVE_EXTERNAL_CALL]"

starts = [i for i, x in enumerate(lines) if BEGIN in x]
if not starts:
    raise SystemExit("ERROR: no R039 primitive deep begin")

start = starts[-1]

ends = [i for i in range(start, len(lines)) if COMPLETE in lines[i]]
if not ends:
    raise SystemExit("ERROR: no R039 primitive deep completion")

end = ends[0]
selected = lines[start:end + 1]

def fields(line, marker):
    p = line.find(marker)
    if p < 0:
        return {}

    payload = line[p + len(marker):].strip()

    if payload.startswith(";"):
        payload = payload[1:].strip()

    out = OrderedDict()

    for part in payload.split(";"):
        part = part.strip()
        if "=" not in part:
            continue

        k, v = part.split("=", 1)
        out[k.strip()] = v.strip()

    return out

begin = fields(lines[start], BEGIN)
completion = fields(lines[end], COMPLETE)

if any(FAIL in x for x in selected):
    raise SystemExit("ERROR: selected run contains PRIMITIVE_DEEP_FAIL")

expected_methods = int(completion["methods"])
expected_instructions = int(completion["instructions"])
expected_arrays = int(completion["arrays"])
expected_elements = int(completion["array_elements"])

if int(completion["failures"]) != 0:
    raise SystemExit("ERROR failures=" + completion["failures"])

objects = []
methods = []
arrays = OrderedDict()
external_calls = []

current_method = None

for line_no, line in enumerate(selected, start=start + 1):

    if OBJECT in line:
        objects.append({
            "line": line_no,
            "fields": fields(line, OBJECT)
        })
        continue

    if METHOD in line:
        current_method = {
            "line": line_no,
            "fields": fields(line, METHOD),
            "chunks": [],
            "instructions": []
        }

        methods.append(current_method)
        continue

    if IL in line:
        if current_method is None:
            raise SystemExit(
                "ERROR: IL chunk without method at line %d" % line_no
            )

        f = fields(line, IL)
        payload = f.get("instructions", "")

        decoded = payload.split("/") if payload else []

        current_method["chunks"].append({
            "line": line_no,
            "chunk": int(f.get("chunk", "-1")),
            "count": len(decoded)
        })

        current_method["instructions"].extend(decoded)
        continue

    if ARRAY in line and ARRAY_CHUNK not in line:
        f = fields(line, ARRAY)

        key = (
            f.get("label", ""),
            f.get("name", "")
        )

        if key in arrays:
            raise SystemExit("ERROR duplicate array " + repr(key))

        arrays[key] = {
            "line": line_no,
            "fields": f,
            "chunks": [],
            "values": OrderedDict()
        }

        continue

    if ARRAY_CHUNK in line:
        f = fields(line, ARRAY_CHUNK)

        key = (
            f.get("label", ""),
            f.get("name", "")
        )

        if key not in arrays:
            raise SystemExit(
                "ERROR array chunk before declaration " + repr(key)
            )

        payload = f.get("values", "")
        values = payload.split("~") if payload else []

        arrays[key]["chunks"].append({
            "line": line_no,
            "chunk": int(f.get("chunk", "-1")),
            "count": len(values)
        })

        for pair in values:
            if ":" not in pair:
                raise SystemExit("ERROR malformed value: " + pair)

            index_text, value = pair.split(":", 1)
            index = int(index_text)

            if index in arrays[key]["values"]:
                raise SystemExit(
                    "ERROR duplicate index %d in %r" % (index, key)
                )

            arrays[key]["values"][index] = value

        continue

    if EXTERNAL in line:
        external_calls.append({
            "line": line_no,
            "fields": fields(line, EXTERNAL)
        })


# Validate method totals.

if len(methods) != expected_methods:
    raise SystemExit(
        "ERROR methods actual=%d expected=%d"
        % (len(methods), expected_methods)
    )

instruction_total = sum(
    len(x["instructions"])
    for x in methods
)

if instruction_total != expected_instructions:
    raise SystemExit(
        "ERROR instructions actual=%d expected=%d"
        % (instruction_total, expected_instructions)
    )


# Validate arrays.

if len(arrays) != expected_arrays:
    raise SystemExit(
        "ERROR arrays actual=%d expected=%d"
        % (len(arrays), expected_arrays)
    )

array_total = 0
normalized_arrays = []

for (label, name), array in arrays.items():

    expected_len = int(array["fields"]["length"])
    actual_len = len(array["values"])

    if actual_len != expected_len:
        raise SystemExit(
            "ERROR %s.%s length actual=%d expected=%d"
            % (
                label,
                name,
                actual_len,
                expected_len
            )
        )

    indexes = sorted(array["values"])

    if indexes != list(range(expected_len)):
        raise SystemExit(
            "ERROR incomplete indexes for %s.%s"
            % (label, name)
        )

    array_total += actual_len

    normalized_arrays.append({
        "label": label,
        "name": name,
        "primitive_type":
            array["fields"].get("primitive_type", ""),
        "element_type":
            array["fields"].get("element_type", ""),
        "length": expected_len,
        "values": [
            array["values"][i]
            for i in range(expected_len)
        ],
        "chunks": array["chunks"]
    })

if array_total != expected_elements:
    raise SystemExit(
        "ERROR array_elements actual=%d expected=%d"
        % (array_total, expected_elements)
    )


result = {
    "schema":
        "AERIS34_R039_MINMUS_PRIMITIVE_DEEP_CLOSURE_V1",

    "source_log": str(log),

    "selected_start_line": start + 1,
    "selected_completion_line": end + 1,

    "begin": begin,
    "completion": completion,

    "counts": {
        "objects": len(objects),
        "methods": len(methods),
        "instructions": instruction_total,
        "arrays": len(arrays),
        "array_elements": array_total,
        "external_calls": len(external_calls)
    },

    "objects": objects,
    "methods": methods,
    "arrays": normalized_arrays,
    "external_calls": external_calls
}

outdir = Path("/tmp/AERIS34_R039")
outdir.mkdir(parents=True, exist_ok=True)

out = outdir / "R039_latest_primitive_deep_closure.json"

out.write_text(
    json.dumps(
        result,
        indent=2,
        ensure_ascii=False
    ) + "\n"
)

sha = hashlib.sha256(out.read_bytes()).hexdigest()

print("=== AERIS34 R039 PHASE D EXTRACTION ===")
print("selected_start_line=%d" % (start + 1))
print("selected_completion_line=%d" % (end + 1))
print()
print("methods=%d" % len(methods))
print("instructions=%d" % instruction_total)
print("arrays=%d" % len(arrays))
print("array_elements=%d" % array_total)
print("external_calls=%d" % len(external_calls))

print()
print("=== ARRAY INVENTORY ===")

for a in normalized_arrays:
    print(
        "%-28s %-18s length=%d type=%s"
        % (
            a["label"],
            a["name"],
            a["length"],
            a["element_type"]
        )
    )

print()
print("json=" + str(out))
print("json_sha256=" + sha)
print()
print("[AERIS34 R039 PHASE D EXTRACTION] PASS")
