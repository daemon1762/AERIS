#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

from v01630_testlib import *  # noqa: F401,F403 - shared package test helpers


def enum_members(text: str, enum_name: str) -> list[str]:
    match = re.search(r"\benum\s+" + re.escape(enum_name) + r"(?:\s*:\s*\w+)?\s*\{(.*?)\}",
                      text, re.S)
    if not match:
        return []
    members = []
    for part in match.group(1).split(","):
        value = re.sub(r"//.*", "", part).strip()
        if not value:
            continue
        members.append(value.split("=", 1)[0].strip())
    return members


def method_body(text: str, name: str) -> str:
    return extract_method(text, name)


def code_without_comments(path: Path) -> str:
    return strip_csharp_comments_and_literals(read(path))
