#!/usr/bin/env python3

from pathlib import Path
from collections import OrderedDict
import hashlib
import json
import struct
import sys

if len(sys.argv) != 2:
    raise SystemExit(
        "usage: AERIS34_R039_extract_libnoise_static.py "
        "<AERISFlightControl.log>"
    )

log = Path(sys.argv[1])

if not log.is_file():
    raise SystemExit("ERROR log missing: " + str(log))

lines = log.read_text(errors="replace").splitlines()

BEGIN = "[R039][LIBNOISE_STATIC_BEGIN]"
ENUM = "[R039][LIBNOISE_STATIC_ENUM]"
ARRAY = "[R039][LIBNOISE_STATIC_ARRAY]"
CHUNK = "[R039][LIBNOISE_STATIC_ARRAY_CHUNK]"
COMPLETE = "[R039][LIBNOISE_STATIC_COMPLETE]"
FAIL = "[R039][LIBNOISE_STATIC_FAIL]"

starts = [
    i for i, line in enumerate(lines)
    if BEGIN in line
]

if not starts:
    raise SystemExit(
        "ERROR no LIBNOISE_STATIC_BEGIN"
    )

start = starts[-1]

ends = [
    i for i in range(start, len(lines))
    if COMPLETE in lines[i]
]

if not ends:
    raise SystemExit(
        "ERROR no completion after latest begin"
    )

end = ends[0]
selected = lines[start:end + 1]


def fields(line, marker):
    p = line.find(marker)

    if p < 0:
        return OrderedDict()

    payload = line[p + len(marker):].strip()

    if payload.startswith(";"):
        payload = payload[1:].strip()

    result = OrderedDict()

    for part in payload.split(";"):
        part = part.strip()

        if "=" not in part:
            continue

        key, value = part.split("=", 1)
        result[key.strip()] = value.strip()

    return result


if any(FAIL in line for line in selected):
    raise SystemExit(
        "ERROR selected run contains "
        "LIBNOISE_STATIC_FAIL"
    )

begin = fields(lines[start], BEGIN)
complete = fields(lines[end], COMPLETE)

if int(complete.get("failures", "-1")) != 0:
    raise SystemExit(
        "ERROR failures=" +
        complete.get("failures", "?")
    )

enum_record = None
array_record = None
chunks = []

values = {}

for line_no, line in enumerate(
    selected,
    start=start + 1
):
    if ENUM in line:
        enum_record = {
            "line": line_no,
            "fields": fields(line, ENUM),
        }

    elif ARRAY in line and CHUNK not in line:
        array_record = {
            "line": line_no,
            "fields": fields(line, ARRAY),
        }

    elif CHUNK in line:
        f = fields(line, CHUNK)

        chunk = int(f["chunk"])
        payload = f.get("values", "")
        pairs = payload.split("~") if payload else []

        chunks.append({
            "line": line_no,
            "chunk": chunk,
            "count": len(pairs),
        })

        for pair in pairs:
            if ":" not in pair:
                raise SystemExit(
                    "ERROR malformed value: " + pair
                )

            index_text, value_text = pair.split(":", 1)

            index = int(index_text)

            if index in values:
                raise SystemExit(
                    "ERROR duplicate index "
                    + str(index)
                )

            values[index] = value_text


if enum_record is None:
    raise SystemExit(
        "ERROR NoiseQuality enum missing"
    )

if array_record is None:
    raise SystemExit(
        "ERROR RandomVectors metadata missing"
    )

meta = array_record["fields"]

if meta.get("name") != "RandomVectors":
    raise SystemExit(
        "ERROR unexpected array "
        + meta.get("name", "?")
    )

expected_length = int(meta["length"])

if expected_length != 1024:
    raise SystemExit(
        "ERROR RandomVectors length="
        + str(expected_length)
    )

indexes = sorted(values.keys())

if indexes != list(range(expected_length)):
    missing = sorted(
        set(range(expected_length)) -
        set(indexes)
    )

    raise SystemExit(
        "ERROR incomplete RandomVectors, "
        "first missing="
        + repr(missing[:16])
    )

text_values = [
    values[i]
    for i in range(expected_length)
]

float_values = [
    float(x)
    for x in text_values
]

# Runtime observer used Buffer.BlockCopy(double[]).
# Desktop is little-endian x86-64, so reproduce exact bits.
raw = b"".join(
    struct.pack("<d", value)
    for value in float_values
)

bit_sha = hashlib.sha256(raw).hexdigest()

expected_bit_sha = meta.get(
    "bit_sha256",
    ""
)

if bit_sha != expected_bit_sha:
    raise SystemExit(
        "ERROR RandomVectors bit SHA mismatch\n"
        "actual=" + bit_sha + "\n"
        "expected=" + expected_bit_sha
    )

quality = enum_record["fields"]

if quality.get("symbolic") != "High":
    raise SystemExit(
        "ERROR NoiseQuality symbolic="
        + quality.get("symbolic", "?")
    )

if quality.get("numeric") != "2":
    raise SystemExit(
        "ERROR NoiseQuality numeric="
        + quality.get("numeric", "?")
    )

# Also expose the natural 256 × 4 grouping.
vectors = []

for i in range(0, expected_length, 4):
    vectors.append({
        "index": i // 4,
        "x": text_values[i + 0],
        "y": text_values[i + 1],
        "z": text_values[i + 2],
        "w": text_values[i + 3],
    })

result = {
    "schema":
        "AERIS34_R039_MINMUS_LIBNOISE_STATIC_CLOSURE_V1",

    "source_log":
        str(log),

    "selected_start_line":
        start + 1,

    "selected_completion_line":
        end + 1,

    "begin":
        begin,

    "noise_quality":
        {
            "symbolic":
                quality["symbolic"],
            "numeric":
                int(quality["numeric"]),
        },

    "random_vectors":
        {
            "declaring_type":
                meta.get(
                    "declaring_type",
                    ""
                ),

            "element_type":
                meta.get(
                    "element_type",
                    ""
                ),

            "length":
                expected_length,

            "bit_sha256":
                bit_sha,

            # Exact textual doubles from runtime logging.
            "values":
                text_values,

            "vectors256":
                vectors,

            "chunks":
                chunks,
        },

    "completion":
        complete,
}

outdir = Path("/tmp/AERIS34_R039")
outdir.mkdir(
    parents=True,
    exist_ok=True
)

out = (
    outdir /
    "R039_latest_libnoise_static_closure.json"
)

out.write_text(
    json.dumps(
        result,
        indent=2,
        ensure_ascii=False
    )
    + "\n"
)

json_sha = hashlib.sha256(
    out.read_bytes()
).hexdigest()

print(
    "=== AERIS34 R039 "
    "LIBNOISE STATIC EXTRACTION ==="
)

print(
    "selected_start_line="
    + str(start + 1)
)

print(
    "selected_completion_line="
    + str(end + 1)
)

print()

print(
    "NoiseQuality="
    + quality["symbolic"]
    + " ("
    + quality["numeric"]
    + ")"
)

print(
    "RandomVectors="
    + str(expected_length)
)

print(
    "vectors256="
    + str(len(vectors))
)

print(
    "array_chunks="
    + str(len(chunks))
)

print(
    "bit_sha256="
    + bit_sha
)

print()
print("json=" + str(out))
print("json_sha256=" + json_sha)

print()
print(
    "[AERIS34 R039 "
    "LIBNOISE STATIC EXTRACTION] PASS"
)
