#!/usr/bin/env python3
"""
AERIS53 one-time fail-closed recovery for the AERIS52 global-GameData ENV4
invalidation incident.

It only restores a body when:
  * preload_state.aps is legacy v6,
  * the current state is incomplete,
  * an AERIS44 OBSERVED record proves the immediately previous environment was
    automatic_complete=true and coastline_complete=true,
  * that record's live_environment exactly equals the current state's
    EnvironmentHash.

No database chunks are changed or deleted. The original state file is preserved
under a timestamped AERIS53 backup before the atomic replacement.
"""

import os
import re
import shutil
import struct
import sys
import tempfile
from datetime import datetime

STATE_MAGIC = "AERIS_PRELOAD_TERRAIN_STATE_V2"
SUPPORTED_VERSION = 6
COASTLINE_FORMAT_VERSION = 2


def read_7bit(stream):
    value = 0
    shift = 0
    while shift < 35:
        raw = stream.read(1)
        if not raw:
            raise EOFError("unexpected EOF in 7-bit integer")
        byte = raw[0]
        value |= (byte & 0x7F) << shift
        if (byte & 0x80) == 0:
            return value
        shift += 7
    raise ValueError("invalid 7-bit integer")


def write_7bit(stream, value):
    value = int(value)
    if value < 0:
        raise ValueError("negative 7-bit integer")
    while value >= 0x80:
        stream.write(bytes(((value | 0x80) & 0xFF,)))
        value >>= 7
    stream.write(bytes((value & 0xFF,)))


def read_string(stream):
    length = read_7bit(stream)
    data = stream.read(length)
    if len(data) != length:
        raise EOFError("unexpected EOF in string")
    return data.decode("utf-8")


def write_string(stream, value):
    data = (value or "").encode("utf-8")
    write_7bit(stream, len(data))
    stream.write(data)


def read_i32(stream):
    return struct.unpack("<i", stream.read(4))[0]


def write_i32(stream, value):
    stream.write(struct.pack("<i", int(value)))


def read_i64(stream):
    return struct.unpack("<q", stream.read(8))[0]


def write_i64(stream, value):
    stream.write(struct.pack("<q", int(value)))


def read_bool(stream):
    return struct.unpack("<?", stream.read(1))[0]


def write_bool(stream, value):
    stream.write(struct.pack("<?", bool(value)))


def read_state(path):
    with open(path, "rb") as stream:
        magic = read_string(stream)
        version = read_i32(stream)
        if magic != STATE_MAGIC or version != SUPPORTED_VERSION:
            return None
        mode = read_i32(stream)
        point_signature = read_string(stream)
        count = read_i32(stream)
        if count < 0 or count > 10000:
            raise ValueError("invalid body count")
        bodies = []
        for _ in range(count):
            plan = {
                "BodyName": read_string(stream),
                "Priority": read_i32(stream),
                "PriorityOverride": read_bool(stream),
                "QualityLimit": read_i32(stream),
                "QualityOverride": read_bool(stream),
                "AutomaticPointRefinementOnly": read_bool(stream),
                "AutomaticComplete": read_bool(stream),
                "CompletedQualityLimit": read_i32(stream),
                "CompletedPointRefinementOnly": read_bool(stream),
                "CompletedEnvironmentHash": read_string(stream),
                "StorageLimitBytes": read_i64(stream),
                "LastVisitedUtcTicks": read_i64(stream),
                "GlobalCursor": read_i64(stream),
                "FarCursor": read_i64(stream),
                "RouteCursor": read_i64(stream),
                "PointCursor": read_i32(stream),
                "EnvironmentHash": read_string(stream),
                "Paused": read_bool(stream),
                "CoastlineCursor": read_i64(stream),
                "CoastlineComplete": read_bool(stream),
                "CompletedCoastlineFormatVersion": read_i32(stream),
                "CompletedCoastlineEnvironmentHash": read_string(stream),
                "CoastlineProgressBodyRadiusMillimetres": read_i64(stream),
                "CoastlineProgressTerrainFormatVersion": read_i32(stream),
                "CoastlineProgressFormatVersion": read_i32(stream),
                "CoastlineProgressEnvironmentHash": read_string(stream),
                "CoastlineProgressLatitudeTiles": read_i32(stream),
                "CoastlineProgressLongitudeTiles": read_i32(stream),
            }
            bitmap_length = read_i32(stream)
            if bitmap_length < 0 or bitmap_length > 1024 * 1024:
                raise ValueError("invalid coastline bitmap length")
            bitmap = stream.read(bitmap_length)
            if len(bitmap) != bitmap_length:
                raise EOFError("unexpected EOF in coastline bitmap")
            plan["CoastlineProcessedBitmap"] = bitmap
            bodies.append(plan)
        if stream.read(1):
            raise ValueError("trailing state bytes")
        return {
            "magic": magic,
            "version": version,
            "mode": mode,
            "point_signature": point_signature,
            "bodies": bodies,
        }


def write_state(path, state):
    directory = os.path.dirname(path)
    fd, temporary = tempfile.mkstemp(
        prefix="preload_state.aeris53.", suffix=".tmp", dir=directory)
    try:
        with os.fdopen(fd, "wb") as stream:
            write_string(stream, state["magic"])
            write_i32(stream, state["version"])
            write_i32(stream, state["mode"])
            write_string(stream, state["point_signature"])
            bodies = state["bodies"]
            write_i32(stream, len(bodies))
            for plan in bodies:
                write_string(stream, plan["BodyName"])
                write_i32(stream, plan["Priority"])
                write_bool(stream, plan["PriorityOverride"])
                write_i32(stream, plan["QualityLimit"])
                write_bool(stream, plan["QualityOverride"])
                write_bool(stream, plan["AutomaticPointRefinementOnly"])
                write_bool(stream, plan["AutomaticComplete"])
                write_i32(stream, plan["CompletedQualityLimit"])
                write_bool(stream, plan["CompletedPointRefinementOnly"])
                write_string(stream, plan["CompletedEnvironmentHash"])
                write_i64(stream, plan["StorageLimitBytes"])
                write_i64(stream, plan["LastVisitedUtcTicks"])
                write_i64(stream, plan["GlobalCursor"])
                write_i64(stream, plan["FarCursor"])
                write_i64(stream, plan["RouteCursor"])
                write_i32(stream, plan["PointCursor"])
                write_string(stream, plan["EnvironmentHash"])
                write_bool(stream, plan["Paused"])
                write_i64(stream, plan["CoastlineCursor"])
                write_bool(stream, plan["CoastlineComplete"])
                write_i32(stream, plan["CompletedCoastlineFormatVersion"])
                write_string(stream, plan["CompletedCoastlineEnvironmentHash"])
                write_i64(stream, plan["CoastlineProgressBodyRadiusMillimetres"])
                write_i32(stream, plan["CoastlineProgressTerrainFormatVersion"])
                write_i32(stream, plan["CoastlineProgressFormatVersion"])
                write_string(stream, plan["CoastlineProgressEnvironmentHash"])
                write_i32(stream, plan["CoastlineProgressLatitudeTiles"])
                write_i32(stream, plan["CoastlineProgressLongitudeTiles"])
                bitmap = plan["CoastlineProcessedBitmap"] or b""
                write_i32(stream, len(bitmap))
                stream.write(bitmap)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    except Exception:
        try:
            os.unlink(temporary)
        except OSError:
            pass
        raise


FIELD_RE = re.compile(r";\s*([A-Za-z0-9_]+)=([^;\r\n]*)")


def fields(line):
    return {key: value.strip() for key, value in FIELD_RE.findall(line)}


def latest_recovery_evidence(log_path):
    evidence = {}
    if not os.path.isfile(log_path):
        return evidence
    with open(log_path, "r", encoding="utf-8", errors="replace") as stream:
        for line in stream:
            if "[AERIS44][R043_PRELOAD_ENV_OBSERVED]" not in line:
                continue
            data = fields(line)
            body = data.get("body", "")
            old_environment = data.get("persisted_environment", "")
            live_environment = data.get("live_environment", "")
            if not body or not old_environment or not live_environment:
                continue
            if data.get("environment_match", "").lower() != "false":
                continue
            if data.get("automatic_complete", "").lower() != "true":
                continue
            if data.get("completed_environment", "") != old_environment:
                continue
            if data.get("coastline_complete", "").lower() != "true":
                continue
            evidence[body.lower()] = data
    return evidence


def as_int(data, key, fallback):
    try:
        return int(data.get(key, fallback))
    except (TypeError, ValueError):
        return fallback


def main():
    if len(sys.argv) != 2:
        print("usage: aeris53_recover_preload_state.py <KSP root>", file=sys.stderr)
        return 2

    ksp = os.path.abspath(os.path.expanduser(sys.argv[1]))
    root = os.path.join(
        ksp, "GameData", "AERISFlightControl", "PluginData",
        "TerrainPreloadDatabaseV3")
    state_path = os.path.join(root, "preload_state.aps")
    log_path = os.path.join(
        ksp, "GameData", "AERISFlightControl", "Logs",
        "AERISFlightControl.log")

    if not os.path.isfile(state_path):
        print("AERIS53_PRELOAD_STATE_RECOVERY=SKIP_NO_STATE")
        return 0

    state = read_state(state_path)
    if state is None:
        print("AERIS53_PRELOAD_STATE_RECOVERY=SKIP_NOT_LEGACY_V6")
        return 0

    evidence = latest_recovery_evidence(log_path)
    if not evidence:
        print("AERIS53_PRELOAD_STATE_RECOVERY=SKIP_NO_PROVEN_EVIDENCE")
        return 0

    recovered = []
    for plan in state["bodies"]:
        body = (plan["BodyName"] or "").lower()
        data = evidence.get(body)
        if data is None:
            continue
        # Fail closed: the evidence must describe the exact environment that is
        # currently incomplete in state. Never roll back across an unrelated or
        # later terrain change.
        if plan["EnvironmentHash"] != data.get("live_environment", ""):
            continue
        if plan["AutomaticComplete"]:
            continue

        old_environment = data["persisted_environment"]
        plan["EnvironmentHash"] = old_environment
        plan["AutomaticComplete"] = True
        plan["CompletedQualityLimit"] = plan["QualityLimit"]
        plan["CompletedPointRefinementOnly"] =             plan["AutomaticPointRefinementOnly"]
        plan["CompletedEnvironmentHash"] = old_environment
        plan["GlobalCursor"] = as_int(data, "global_cursor",
                                      plan["GlobalCursor"])
        plan["FarCursor"] = as_int(data, "far_cursor",
                                   plan["FarCursor"])
        plan["RouteCursor"] = as_int(data, "route_cursor",
                                     plan["RouteCursor"])
        plan["CoastlineCursor"] = as_int(data, "coastline_cursor",
                                         plan["CoastlineCursor"])
        plan["CoastlineComplete"] = True
        plan["CompletedCoastlineFormatVersion"] = COASTLINE_FORMAT_VERSION
        plan["CompletedCoastlineEnvironmentHash"] = old_environment
        plan["CoastlineProgressBodyRadiusMillimetres"] = 0
        plan["CoastlineProgressTerrainFormatVersion"] = 0
        plan["CoastlineProgressFormatVersion"] = 0
        plan["CoastlineProgressEnvironmentHash"] = ""
        plan["CoastlineProgressLatitudeTiles"] = 0
        plan["CoastlineProgressLongitudeTiles"] = 0
        plan["CoastlineProcessedBitmap"] = b""
        recovered.append(plan["BodyName"])

    if not recovered:
        print("AERIS53_PRELOAD_STATE_RECOVERY=NO_MATCHING_DAMAGE")
        return 0

    stamp = datetime.utcnow().strftime("%Y%m%dT%H%M%SZ")
    backup = state_path + ".aeris53-before-recovery-" + stamp + ".bak"
    shutil.copy2(state_path, backup)
    write_state(state_path, state)

    # Round-trip before reporting success.
    verified = read_state(state_path)
    if verified is None:
        raise RuntimeError("recovered state failed round-trip")
    verified_by_name = {
        item["BodyName"]: item for item in verified["bodies"]
    }
    for body in recovered:
        item = verified_by_name.get(body)
        if item is None or not item["AutomaticComplete"]:
            raise RuntimeError("recovered body missing after round-trip: " + body)

    print("AERIS53_PRELOAD_STATE_RECOVERY=RESTORED")
    print("recovered_bodies=" + ",".join(recovered))
    print("recovered_count=" + str(len(recovered)))
    print("backup=" + backup)
    print("database_chunks_modified=false")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print("AERIS53_PRELOAD_STATE_RECOVERY=FAIL", file=sys.stderr)
        print(type(exc).__name__ + ": " + str(exc), file=sys.stderr)
        sys.exit(1)
