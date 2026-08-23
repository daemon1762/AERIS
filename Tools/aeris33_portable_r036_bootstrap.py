#!/usr/bin/env python3
from __future__ import print_function

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from datetime import datetime

TOOL_VERSION = "1.0"
PREFIX = "[AERIS R036 PORTABLE]"
ACCEPTED_R036 = "faee6082e650e78f9154b607ded5c611b11f6ad2"

DESKTOP_KSP = Path.home() / ".steam/debian-installation/steamapps/common/Kerbal Space Program"
LAPTOP_KSP = Path.home() / ".local/share/Steam/steamapps/common/Kerbal Space Program"

STEPS = [
    {
        "id": "r031_base",
        "branch": "agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact",
        "commit": "0ffa927b7dbee8a5bc77a3340441ba2088177eb4",
        "script": "Tools/build_aeris32_rev3_5_r031_ptc_source_resolver_cpu_file_exact_shadow.py",
    },
    {
        "id": "r031_dep",
        "branch": "agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact",
        "commit": "0ffa927b7dbee8a5bc77a3340441ba2088177eb4",
        "script": "Tools/build_aeris32_rev3_5_r031_gilly_dependency_closure_hotfix1_math_type.py",
    },
    {
        "id": "r032",
        "branch": "agent/aeris33-rev3-5-r032-gilly-pure-cpu-exact-worker-poc",
        "commit": "76fd13d78d98e8e5fe6524bd4bfdf56f012191d2",
        "script": "Tools/build_aeris32_rev3_5_r032_gilly_pure_cpu_exact_worker_poc.py",
    },
    {
        "id": "r033",
        "branch": "agent/aeris33-rev3-5-r033-pure-procedural-dependency-inventory",
        "commit": "b74724f0d6151002f65063ddfabdf0c56a142d8d",
        "script": "Tools/build_aeris32_rev3_5_r033_pure_procedural_dependency_inventory.py",
    },
    {
        "id": "r034",
        "branch": "agent/aeris33-rev3-5-r034-pqslandcontrol-height-path-audit",
        "commit": "2a56c33d53e063fe4676ef2d4be2f92a58be89c8",
        "script": "Tools/build_aeris32_rev3_5_r034_pqslandcontrol_height_path_audit.py",
    },
    {
        "id": "r035",
        "branch": "agent/aeris33-rev3-5-r035-landcontrol-write-semantics-il-audit",
        "commit": "5ee37eef182b337b9cca3d64b145d141f06e420b",
        "script": "Tools/build_aeris32_rev3_5_r035_landcontrol_write_semantics_il_audit.py",
    },
    {
        "id": "r036",
        "branch": "agent/aeris33-rev3-5-r036-common-pure-cpu-exact-formula-closure",
        "commit": ACCEPTED_R036,
        "script": "Tools/build_aeris33_rev3_5_r036_common_pure_cpu_exact_formula_closure.py",
    },
]

RUNTIME_VERIFIER = "Tools/verify_runtime_aeris33_rev3_5_r036_common_pure_cpu_exact_formula_closure.py"
RUNTIME_LOG_REL = Path("GameData/AERISFlightControl/Logs/AERISFlightControl.log")


class BootstrapError(RuntimeError):
    pass


def say(msg=""):
    print(PREFIX + (" " + msg if msg else ""), flush=True)


def run(cmd, cwd=None, capture=True, check=True, stdout=None):
    kwargs = {
        "cwd": str(cwd) if cwd else None,
        "text": True,
        "stderr": subprocess.STDOUT,
    }
    if stdout is not None:
        kwargs["stdout"] = stdout
    elif capture:
        kwargs["stdout"] = subprocess.PIPE
    p = subprocess.run(cmd, **kwargs)
    if check and p.returncode != 0:
        out = p.stdout if capture and p.stdout else ""
        raise BootstrapError("command failed rc={}: {}\n{}".format(
            p.returncode, " ".join(map(str, cmd)), out[-8000:]))
    return p


def git(repo, *args, capture=True, check=True, stdout=None):
    return run(["git", *args], cwd=repo, capture=capture, check=check, stdout=stdout)


def find_source_repo(explicit=None):
    candidates = []
    if explicit:
        candidates.append(Path(explicit).expanduser())
    candidates.extend([Path.cwd(), Path.home() / "AERIS32"])
    seen = set()
    for p in candidates:
        try:
            p = p.resolve()
        except Exception:
            continue
        if str(p) in seen:
            continue
        seen.add(str(p))
        if (p / ".git").exists():
            q = run(["git", "-C", str(p), "rev-parse", "--show-toplevel"], check=False)
            if q.returncode == 0:
                return Path(q.stdout.strip()).resolve()
    raise BootstrapError("AERIS git repository not found; use --repo PATH")


def detect_ksp(machine, explicit):
    if explicit:
        p = Path(explicit).expanduser().resolve()
        if not p.is_dir():
            raise BootstrapError("KSP path missing: " + str(p))
        label = "custom-" + hashlib.sha1(str(p).encode("utf-8")).hexdigest()[:8]
        return label, p

    if machine == "desktop":
        p, label = DESKTOP_KSP, "desktop"
    elif machine == "laptop":
        p, label = LAPTOP_KSP, "laptop"
    else:
        desktop = DESKTOP_KSP.is_dir()
        laptop = LAPTOP_KSP.is_dir()
        if desktop:
            p, label = DESKTOP_KSP, "desktop"
            if laptop:
                say("both KSP paths exist; desktop selected (override with --machine laptop)")
        elif laptop:
            p, label = LAPTOP_KSP, "laptop"
        else:
            raise BootstrapError("KSP not found at desktop or laptop path; use --ksp PATH")
    if not p.is_dir():
        raise BootstrapError("KSP path missing: " + str(p))
    return label, p.resolve()


def atomic_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n")
    tmp.replace(path)


def install_cli(source_script):
    bindir = Path.home() / ".local/bin"
    sharedir = Path.home() / ".local/share/aeris-tools"
    bindir.mkdir(parents=True, exist_ok=True)
    sharedir.mkdir(parents=True, exist_ok=True)
    installed = sharedir / "aeris33_portable_r036_bootstrap.py"
    shutil.copy2(str(source_script), str(installed))
    launcher = bindir / "aeris-r036"
    launcher.write_text(
        "#!/bin/sh\nexec python3 \"$HOME/.local/share/aeris-tools/"
        "aeris33_portable_r036_bootstrap.py\" \"$@\"\n")
    launcher.chmod(0o755)
    return launcher


def ensure_cached_repo(source_repo, cache_repo):
    if (cache_repo / ".git").exists():
        return
    cache_repo.parent.mkdir(parents=True, exist_ok=True)
    if cache_repo.exists():
        raise BootstrapError("cache path exists but is not a git repo: " + str(cache_repo))
    say("creating isolated bootstrap repo: " + str(cache_repo))
    run(["git", "clone", "--no-hardlinks", str(source_repo), str(cache_repo)])


def require_commit(repo, sha):
    p = git(repo, "cat-file", "-t", sha, check=False)
    if p.returncode != 0 or p.stdout.strip() != "commit":
        raise BootstrapError("required accepted commit missing: " + sha)


def current_branch(repo):
    return git(repo, "branch", "--show-current").stdout.strip()


def head_sha(repo):
    return git(repo, "rev-parse", "HEAD").stdout.strip()


def branch_sha(repo, branch):
    p = git(repo, "rev-parse", "--verify", "refs/heads/" + branch, check=False)
    return p.stdout.strip() if p.returncode == 0 else None


def switch_exact_branch(repo, branch, sha):
    require_commit(repo, sha)
    cur = current_branch(repo)
    if cur == branch:
        if head_sha(repo) != sha:
            raise BootstrapError("branch {} HEAD mismatch: {} expected {}".format(
                branch, head_sha(repo), sha))
        return

    existing = branch_sha(repo, branch)
    if existing is not None and existing != sha:
        raise BootstrapError("local branch {} points to {} expected {}; refusing to rewrite".format(
            branch, existing, sha))

    cmd = ["switch", branch] if existing else ["switch", "-c", branch, sha]
    p = git(repo, *cmd, check=False)
    if p.returncode != 0:
        raise BootstrapError("git switch failed; no reset/clean/stash attempted.\n" + (p.stdout or ""))
    if current_branch(repo) != branch or head_sha(repo) != sha:
        raise BootstrapError("post-switch branch/HEAD gate failed for " + branch)


def tail_text(path, lines=100):
    try:
        data = path.read_text(errors="replace").splitlines()
        return "\n".join(data[-lines:])
    except Exception:
        return ""


def build_step(repo, step, ksp, log_dir):
    switch_exact_branch(repo, step["branch"], step["commit"])
    script = repo / step["script"]
    if not script.is_file():
        raise BootstrapError("build script missing: " + str(script))
    log = log_dir / (step["id"] + ".log")
    say("{} build start -> {}".format(step["id"], log))
    with log.open("w") as f:
        p = run([sys.executable, str(script), str(ksp)], cwd=repo,
                capture=False, check=False, stdout=f)
    if p.returncode != 0:
        say("{} FAILED rc={}".format(step["id"], p.returncode))
        print(tail_text(log, 120))
        raise BootstrapError("build failed: " + step["id"])
    say("{} PASS".format(step["id"]))
    return log


def load_state(path):
    if not path.is_file():
        return {"tool_version": TOOL_VERSION, "completed": []}
    try:
        data = json.loads(path.read_text())
    except Exception as e:
        raise BootstrapError("state file unreadable: {}: {}".format(path, e))
    if not isinstance(data.get("completed", []), list):
        raise BootstrapError("invalid state file: " + str(path))
    return data


def bootstrap(args):
    source_repo = find_source_repo(args.repo)
    label, ksp = detect_ksp(args.machine, args.ksp)
    launcher = install_cli(Path(__file__).resolve())
    say("CLI installed/updated: " + str(launcher))
    say("target={} KSP={}".format(label, ksp))
    say("source repo=" + str(source_repo))

    root = Path.home() / ".cache/aeris/r036-portable-bootstrap" / label
    cache_repo = root / "repo"
    state_path = root / "state.json"
    logs_root = root / "logs"
    ensure_cached_repo(source_repo, cache_repo)

    for step in STEPS:
        require_commit(cache_repo, step["commit"])

    state = load_state(state_path)
    completed = list(state.get("completed", []))
    valid_ids = [s["id"] for s in STEPS]
    if any(x not in valid_ids for x in completed):
        raise BootstrapError("state contains unknown completed step; inspect " + str(state_path))

    if args.reinstall:
        if "r035" not in completed:
            say("--reinstall ignored because parent chain is incomplete")
        else:
            completed = [x for x in completed if x != "r036"]

    if completed == valid_ids and not args.reinstall:
        say("bootstrap already complete for this target")
        say("use 'aeris-r036 verify' after KSP reaches Main Menu")
        say("use 'aeris-r036 bootstrap --reinstall' to rebuild/install R036 again")
        return 0

    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    log_dir = logs_root / stamp
    log_dir.mkdir(parents=True, exist_ok=True)

    for step in STEPS:
        sid = step["id"]
        if sid in completed:
            say("{} already PASS; resume skip".format(sid))
            continue
        build_step(cache_repo, step, ksp, log_dir)
        completed.append(sid)
        state = {
            "tool_version": TOOL_VERSION,
            "target": label,
            "ksp": str(ksp),
            "source_repo": str(source_repo),
            "accepted_r036": ACCEPTED_R036,
            "completed": completed,
            "updated_at": datetime.now().isoformat(timespec="seconds"),
            "last_log_dir": str(log_dir),
        }
        atomic_json(state_path, state)

    say("BOOTSTRAP PASS")
    say("R031 -> R036 accepted chain materialized and R036 installed")
    say("next: fully restart KSP, reach Main Menu, then run: aeris-r036 verify")
    return 0


def verify(args):
    source_repo = find_source_repo(args.repo)
    label, ksp = detect_ksp(args.machine, args.ksp)
    log_path = ksp / RUNTIME_LOG_REL
    verifier = source_repo / RUNTIME_VERIFIER
    if not verifier.is_file():
        cached = Path.home() / ".cache/aeris/r036-portable-bootstrap" / label / "repo" / RUNTIME_VERIFIER
        if cached.is_file():
            verifier = cached
    if not verifier.is_file():
        raise BootstrapError("R036 runtime verifier missing: " + str(verifier))
    if not log_path.is_file():
        raise BootstrapError("AERIS runtime log missing: " + str(log_path))
    say("runtime verify target={} log={}".format(label, log_path))
    p = run([sys.executable, str(verifier), str(log_path)],
            cwd=verifier.parent.parent, capture=False, check=False)
    return p.returncode


def status(args):
    source_repo = find_source_repo(args.repo)
    label, ksp = detect_ksp(args.machine, args.ksp)
    root = Path.home() / ".cache/aeris/r036-portable-bootstrap" / label
    state = load_state(root / "state.json")
    say("tool_version=" + TOOL_VERSION)
    say("source_repo=" + str(source_repo))
    say("target={} KSP={}".format(label, ksp))
    say("state=" + str(root / "state.json"))
    say("completed=" + ",".join(state.get("completed", [])))
    say("runtime_log=" + str(ksp / RUNTIME_LOG_REL))
    return 0


def main():
    ap = argparse.ArgumentParser(
        description="Portable, resumable AERIS R031->R036 accepted-chain bootstrap")
    ap.add_argument("--repo", help="AERIS source repository; default: cwd or ~/AERIS32")
    ap.add_argument("--machine", choices=("auto", "desktop", "laptop"), default="auto")
    ap.add_argument("--ksp", help="explicit KSP root path")
    sub = ap.add_subparsers(dest="command")

    bp = sub.add_parser("bootstrap", help="materialize/build/install accepted R031->R036 chain")
    bp.add_argument("--reinstall", action="store_true",
                    help="rerun only R036 when the parent chain is already complete")
    sub.add_parser("verify", help="run R036 runtime verifier against detected KSP log")
    sub.add_parser("status", help="show target and resumable bootstrap state")
    sub.add_parser("install-cli", help="install/update ~/.local/bin/aeris-r036")

    args = ap.parse_args()
    if args.command is None:
        args.command = "bootstrap"
        args.reinstall = False

    try:
        if args.command == "install-cli":
            launcher = install_cli(Path(__file__).resolve())
            say("CLI installed/updated: " + str(launcher))
            return 0
        if args.command == "bootstrap":
            return bootstrap(args)
        if args.command == "verify":
            return verify(args)
        if args.command == "status":
            return status(args)
        raise BootstrapError("unknown command")
    except BootstrapError as e:
        say("FAIL " + str(e))
        return 1
    except KeyboardInterrupt:
        say("INTERRUPTED; completed stages are preserved for resume")
        return 130


if __name__ == "__main__":
    sys.exit(main())
