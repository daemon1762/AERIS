#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Iterable, List, Tuple

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source" / "AERISFlightControl"

class CheckSuite:
    def __init__(self, name: str) -> None:
        self.name = name
        self.passed = 0
        self.failed = 0

    def check(self, condition: bool, label: str, detail: str = "") -> None:
        if condition:
            self.passed += 1
            print(f"PASS: {label}")
        else:
            self.failed += 1
            suffix = f" — {detail}" if detail else ""
            print(f"FAIL: {label}{suffix}")

    def equal(self, actual, expected, label: str) -> None:
        self.check(actual == expected, label, f"actual={actual!r}, expected={expected!r}")

    def finish(self) -> None:
        total = self.passed + self.failed
        print(f"[{self.name}] {self.passed}/{total} PASS")
        if self.failed:
            raise SystemExit(1)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def source_files(suffix: str = "") -> List[Path]:
    values = [p for p in SOURCE.rglob("*") if p.is_file()]
    if suffix:
        values = [p for p in values if p.suffix.lower() == suffix.lower()]
    return sorted(values)


def package_files() -> List[Path]:
    return sorted(p for p in ROOT.rglob("*") if p.is_file())


def all_text(paths: Iterable[Path]) -> str:
    chunks = []
    for path in paths:
        try:
            chunks.append(read(path))
        except UnicodeDecodeError:
            continue
    return "\n".join(chunks)


def parse_version() -> Tuple[str, str, str]:
    version_txt = read(ROOT / "VERSION").strip()
    data = json.loads(read(ROOT / "GameData" / "AERISFlightControl" / "AERISFlightControl.version"))
    v = data["VERSION"]
    version_json = f"{int(v['MAJOR'])}.{int(v['MINOR'])}.{int(v['PATCH'])}.{int(v.get('BUILD', 0))}"
    generated = read(SOURCE / "Properties" / "AERISBuildVersion.generated.cs")
    match = re.search(r'internal const string Semantic\s*=\s*"([^"]+)"', generated)
    version_cs = match.group(1) if match else ""
    return version_txt, version_json, version_cs


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def compile_includes(csproj_text: str) -> List[str]:
    return re.findall(r'<Compile\s+Include="([^"]+)"\s*/>', csproj_text)


def strip_csharp_comments_and_literals(text: str) -> str:
    out: List[str] = []
    i = 0
    n = len(text)
    state = "code"
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if state == "code":
            if c == "/" and nxt == "/":
                out.extend("  "); i += 2; state = "line_comment"; continue
            if c == "/" and nxt == "*":
                out.extend("  "); i += 2; state = "block_comment"; continue
            if c == "@" and nxt == '"':
                out.extend("  "); i += 2; state = "verbatim_string"; continue
            if c == '"':
                out.append(" "); i += 1; state = "string"; continue
            if c == "'":
                out.append(" "); i += 1; state = "char"; continue
            out.append(c); i += 1; continue
        if state == "line_comment":
            if c == "\n": out.append("\n"); state = "code"
            else: out.append(" ")
            i += 1; continue
        if state == "block_comment":
            if c == "*" and nxt == "/": out.extend("  "); i += 2; state = "code"
            else: out.append("\n" if c == "\n" else " "); i += 1
            continue
        if state == "string":
            if c == "\\":
                out.append(" ");
                if i + 1 < n: out.append(" "); i += 2
                else: i += 1
                continue
            out.append("\n" if c == "\n" else " ")
            i += 1
            if c == '"': state = "code"
            continue
        if state == "verbatim_string":
            if c == '"' and nxt == '"': out.extend("  "); i += 2; continue
            out.append("\n" if c == "\n" else " ")
            i += 1
            if c == '"': state = "code"
            continue
        if state == "char":
            if c == "\\":
                out.append(" ")
                if i + 1 < n: out.append(" "); i += 2
                else: i += 1
                continue
            out.append("\n" if c == "\n" else " ")
            i += 1
            if c == "'": state = "code"
            continue
    return "".join(out)


def csharp_balance(path: Path) -> Tuple[bool, str]:
    text = strip_csharp_comments_and_literals(read(path))
    pairs = {')': '(', ']': '[', '}': '{'}
    stack: List[Tuple[str, int]] = []
    opening = set(pairs.values())
    for index, c in enumerate(text):
        if c in opening:
            stack.append((c, index))
        elif c in pairs:
            if not stack or stack[-1][0] != pairs[c]:
                return False, f"unexpected {c!r} at index {index}"
            stack.pop()
    if stack:
        return False, f"unclosed {stack[-1][0]!r} at index {stack[-1][1]}"
    return True, ""


def extract_method(text: str, method_name: str) -> str:
    match = re.search(r'\b' + re.escape(method_name) + r'\s*\(', text)
    if not match:
        raise ValueError(f"method not found: {method_name}")
    brace = text.find("{", match.end())
    if brace < 0:
        raise ValueError(f"method body not found: {method_name}")
    clean = strip_csharp_comments_and_literals(text)
    depth = 0
    for i in range(brace, len(clean)):
        if clean[i] == "{": depth += 1
        elif clean[i] == "}":
            depth -= 1
            if depth == 0:
                return text[brace:i + 1]
    raise ValueError(f"method body unterminated: {method_name}")


def extract_string_array_expressions(method_text: str) -> List[str]:
    marker = "new string[]"
    start = method_text.find(marker)
    if start < 0:
        raise ValueError("new string[] initializer not found")
    brace = method_text.find("{", start + len(marker))
    if brace < 0:
        raise ValueError("initializer brace not found")
    clean = strip_csharp_comments_and_literals(method_text)
    depth = 0
    end = -1
    for i in range(brace, len(clean)):
        if clean[i] == "{": depth += 1
        elif clean[i] == "}":
            depth -= 1
            if depth == 0:
                end = i
                break
    if end < 0:
        raise ValueError("initializer unterminated")
    body = method_text[brace + 1:end]
    clean_body = strip_csharp_comments_and_literals(body)
    expressions: List[str] = []
    last = 0
    paren = bracket = brace_depth = 0
    for i, c in enumerate(clean_body):
        if c == "(": paren += 1
        elif c == ")": paren -= 1
        elif c == "[": bracket += 1
        elif c == "]": bracket -= 1
        elif c == "{": brace_depth += 1
        elif c == "}": brace_depth -= 1
        elif c == "," and paren == bracket == brace_depth == 0:
            value = body[last:i].strip()
            if value: expressions.append(value)
            last = i + 1
    tail = body[last:].strip()
    if tail: expressions.append(tail)
    return expressions


def extract_csv_header(text: str, writer_name: str) -> List[str]:
    pattern = re.escape(writer_name) + r'\.WriteLine\("([^"]*)"\);'
    match = re.search(pattern, text)
    if not match:
        raise ValueError(f"header not found for {writer_name}")
    return match.group(1).split(",") if match.group(1) else []
