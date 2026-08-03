#!/usr/bin/env python3
"""Deterministic reference tests for the v0.16.4 straight-runway safety gates.

This deliberately mirrors the published numeric contract without importing Unity/KSP.
The companion static verifier ties every tested gate to the production C# source.
"""
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from dataclasses import dataclass, field
from v01640_testlib import CheckSuite


@dataclass
class Strip:
    east: float
    north: float
    heading: float
    length: float
    width: float
    semantic: str = "runway"
    group: int = 1
    flatness: float = 0.0
    physical_only: bool = False
    threshold: bool = False


@dataclass
class Result:
    state: str
    code: str = "NONE"
    axes: list[dict] = field(default_factory=list)


def angle180(value: float) -> float:
    value %= 180.0
    return value + 180.0 if value < 0 else value


def adiff(a: float, b: float) -> float:
    d = abs(angle180(a) - angle180(b))
    return min(d, 180.0 - d)


def recognize(strips: list[Strip], *, provider=True, pqs=True,
              readable=True, approach=(True, True)) -> Result:
    if not readable:
        return Result("FAILED", "NO_GEOMETRY_EVIDENCE")
    usable = [s for s in strips if s.semantic not in
              {"taxiway", "apron", "platform", "obstacle"}]
    if not usable:
        return Result("FAILED", "WHOLE_SITE_BOUNDS_ONLY")
    orientations: list[float] = []
    for strip in usable:
        if strip.length <= 0 or strip.width <= 0:
            continue
        h = angle180(strip.heading)
        if not any(adiff(h, old) <= 3.0 for old in orientations):
            orientations.append(h)
    axes: list[dict] = []
    for heading in orientations:
        rad = math.radians(heading)
        ae, an = math.sin(rad), math.cos(rad)
        ne, nn = -an, ae
        members = [s for s in usable if adiff(s.heading, heading) <= 15.0]
        members.sort(key=lambda s: s.east * ne + s.north * nn)
        bands: list[list[Strip]] = []
        centers: list[float] = []
        for member in members:
            across = member.east * ne + member.north * nn
            seam = max(8.0, min(60.0, member.width * 0.55)) + member.width * 0.5
            target = next((i for i, center in enumerate(centers)
                           if abs(across - center) <= seam), None)
            if target is None:
                bands.append([member]); centers.append(across)
            else:
                bands[target].append(member)
                centers[target] = sum(x.east * ne + x.north * nn
                                      for x in bands[target]) / len(bands[target])
        for band in bands:
            starts, ends, usable_starts, usable_ends = [], [], [], []
            widths, groups, slopes = [], set(), []
            for member in band:
                along = member.east * ae + member.north * an
                starts.append(along - member.length / 2)
                ends.append(along + member.length / 2)
                widths.append(member.width)
                groups.add(member.group)
                slopes.append(abs(member.flatness))
                if not member.physical_only:
                    usable_starts.append(along - member.length / 2)
                    usable_ends.append(along + member.length / 2)
            physical_start, physical_end = min(starts), max(ends)
            if not usable_starts:
                usable_starts, usable_ends = starts, ends
            usable_start, usable_end = min(usable_starts), max(usable_ends)
            width = sum(widths) / len(widths)
            length = physical_end - physical_start
            if max(slopes) > 8.0:
                return Result("FAILED", "SURFACE_SLOPE_EXCEEDED")
            if length < 250.0:
                return Result("FAILED", "RUNWAY_TOO_SHORT")
            if width < 8.0:
                return Result("FAILED", "RUNWAY_TOO_NARROW")
            if length / width < 4.0:
                continue
            families = {"geometry"}
            if provider:
                families.add("metadata")
            if pqs:
                families.add("terrain")
            if len(groups) >= 2:
                families.add("operational")
            if any(s.threshold for s in band):
                families.add("marking")
            if len(families) < 3:
                continue
            safety = max(2.0, min(length * 0.08, 5.0))
            axes.append({"heading": heading, "across": sum(
                         s.east * ne + s.north * nn for s in band) / len(band),
                         "physical": length,
                         "usable": max(0.0, usable_end - usable_start - 2 * safety),
                         "width": width, "directions":
                         ("CERTIFIED" if approach[0] else "FAILED",
                          "CERTIFIED" if approach[1] else "FAILED")})
    # The same physical strip can be rediscovered from a reciprocal/equivalent prior.
    dedup: list[dict] = []
    for axis in axes:
        if not any(adiff(axis["heading"], old["heading"]) < 3 and
                   abs(axis["physical"] - old["physical"]) < 1 and
                   abs(axis["across"] - old["across"]) <
                   max(axis["width"], old["width"]) for old in dedup):
            dedup.append(axis)
    if len(dedup) > 4:
        return Result("FAILED", "MULTIPLE_GEOMETRY_SOLUTIONS")
    return Result("CERTIFIED", axes=dedup) if dedup else Result(
        "FAILED", "INSUFFICIENT_EVIDENCE")


suite = CheckSuite("v0.16.4.0 deterministic consensus-runway reference cases")

r = recognize([Strip(0, 0, 90, 1000, 45)])
suite.equal((r.state, len(r.axes)), ("CERTIFIED", 1), "01 single straight runway")

r = recognize([Strip(0, -70, 90, 1000, 45, group=1),
               Strip(0, 70, 90, 1000, 45, group=2)])
suite.equal(len(r.axes), 2, "02 parallel runway centerlines remain separate")

r = recognize([Strip(0, 0, 45, 1000, 40, group=1),
               Strip(0, 0, 135, 900, 40, group=2)])
suite.equal(len(r.axes), 2, "03 X layout produces two physical axes")

r = recognize([Strip(0, 0, 0, 900, 40, group=1),
               Strip(0, 0, 90, 700, 35, group=2)])
suite.equal(len(r.axes), 2, "04 cross/T candidates do not contaminate each other")

r = recognize([Strip(0, 0, 90, 1000, 45),
               Strip(0, 75, 90, 900, 18, "taxiway"),
               Strip(300, 100, 0, 250, 220, "apron")])
suite.equal(len(r.axes), 1, "05 taxiway and apron are excluded")

r = recognize([Strip(0, 0, 90, 2000, 900, "platform"),
               Strip(0, 0, 135, 1000, 45, "runway")])
suite.equal(len(r.axes), 1, "06 mountain platform whole-site bounds are excluded")

r = recognize([Strip(-300, 0, 90, 400, 40, group=1),
               Strip(300, 0, 90, 400, 40, group=2)])
suite.equal(len(r.axes), 1, "07 aligned multi-static modules consolidate")

r = recognize([Strip(0, 0, 90, 800, 35)], readable=True)
suite.equal(r.state, "CERTIFIED", "08 collider-only numeric geometry remains usable")

r = recognize([], readable=False)
suite.equal(r.code, "NO_GEOMETRY_EVIDENCE", "09 unreadable mesh fails safely")

r = recognize([Strip(0, 0, -270, abs(-900), abs(-40))])
suite.equal((r.state, round(r.axes[0]["heading"])), ("CERTIFIED", 90),
            "10 negative-scale/rotated orientation is normalized")

r = recognize([Strip(0, 0, 90, 1200, 45, physical_only=True),
               Strip(0, 0, 90, 1000, 45, group=2, threshold=True)])
suite.check(r.axes[0]["physical"] > r.axes[0]["usable"],
            "11 blast/stopway physical extent is separate from usable length")

r = recognize([Strip(0, 0, 90, 850, 30, "runway")], provider=True, pqs=True)
suite.equal(r.state, "CERTIFIED", "12 unmarked/natural straight strip can use non-marking evidence")

r = recognize([Strip(0, 0, 90, 850, 30, flatness=12.0)])
suite.equal(r.code, "SURFACE_SLOPE_EXCEEDED", "13 cliff/step candidate is rejected")

r = recognize([Strip(i * 300, 0, i * 20, 600, 25, group=i + 1)
               for i in range(5)])
suite.equal(r.code, "MULTIPLE_GEOMETRY_SOLUTIONS", "14 unresolved multi-axis ambiguity fails")

r = recognize([Strip(0, 0, 90, 1000, 45)], approach=(False, True))
suite.equal(r.axes[0]["directions"], ("FAILED", "CERTIFIED"),
            "15 approach blockage is direction-specific")
suite.finish()
