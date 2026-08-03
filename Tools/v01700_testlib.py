#!/usr/bin/env python3
from __future__ import annotations

import hashlib
from typing import List

from v01640_testlib import *  # noqa: F401,F403 - accepted PC1 helpers


def extract_typed_array_expressions(method_text: str,
                                    element_type: str = "AERISCsvField") -> List[str]:
    marker = "new " + element_type + "[]"
    start = method_text.find(marker)
    if start < 0:
        raise ValueError(marker + " initializer not found")
    brace = method_text.find("{", start + len(marker))
    if brace < 0:
        raise ValueError("initializer brace not found")
    clean = strip_csharp_comments_and_literals(method_text)
    depth = 0
    end = -1
    for index in range(brace, len(clean)):
        if clean[index] == "{":
            depth += 1
        elif clean[index] == "}":
            depth -= 1
            if depth == 0:
                end = index
                break
    if end < 0:
        raise ValueError("initializer unterminated")
    body = method_text[brace + 1:end]
    clean_body = strip_csharp_comments_and_literals(body)
    values: List[str] = []
    last = 0
    paren = bracket = nested_brace = 0
    for index, character in enumerate(clean_body):
        if character == "(": paren += 1
        elif character == ")": paren -= 1
        elif character == "[": bracket += 1
        elif character == "]": bracket -= 1
        elif character == "{": nested_brace += 1
        elif character == "}": nested_brace -= 1
        elif character == "," and paren == bracket == nested_brace == 0:
            value = body[last:index].strip()
            if value: values.append(value)
            last = index + 1
    tail = body[last:].strip()
    if tail: values.append(tail)
    return values


def text_sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()
