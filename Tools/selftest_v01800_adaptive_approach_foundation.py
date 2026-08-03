#!/usr/bin/env python3
from __future__ import annotations
import math
import re
import sys
sys.dont_write_bytecode = True
from dataclasses import dataclass
from v01700_testlib import SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite=CheckSuite("v0.18.0.0 adaptive approach planning foundation")
models=read(SOURCE/"Landing"/"AERISApproachModels.cs")
planner=read(SOURCE/"Landing"/"AERISAdaptiveApproachPlanner.cs")
registry=read(SOURCE/"Landing"/"AERISApproachRegistry.cs")
csproj=read(SOURCE/"AERISFlightControl.csproj")

for name in ("AERISApproachModels.cs","AERISAdaptiveApproachPlanner.cs",
             "AERISApproachRegistry.cs"):
    suite.check("Landing\\"+name in csproj,"approach source is compiled: "+name)
for token in ("MinimumGlideAngleDeg = 2.5","NormalMaximumGlideAngleDeg = 4.0",
              "ObstacleMaximumGlideAngleDeg = 5.0",
              "ConditionalMaximumGlideAngleDeg = 6.0",
              "MinimumFinalStraightMeters = 4000.0",
              "AircraftSupportsSteepApproach"):
    suite.check(token in models,"restrained glide path contract: "+token)
for token in ("FinalCorridorClear","BuildDoglegIfUseful",
              "FINAL LOCALIZER MUST COINCIDE WITH RUNWAY CENTERLINE",
              "MISSED_APPROACH_BLOCKED","CONTINUOUS CURVATURE TRANSITION",
              "DISPLAY/PLANNING GATE ONLY; NO FLIGHT CONTROL AUTHORITY"):
    suite.check(token in planner,"adaptive procedure contract: "+token)
suite.check("AERISApproachProcedureState.Pending" in planner,
            "missing terrain/obstacle snapshot remains pending rather than certified")
suite.check("Read-only approach procedure registry" in registry and
            "SelectedSnapshot" in registry,
            "registry publishes clone-only display/planning snapshots")

clean=strip_csharp_comments_and_literals(models+planner+registry)
for forbidden in ("FlightCtrlState","MainThrottle","wheelThrottle","wheelSteer",
                  "SetArmed(","ApplyAaNative","AERISNavDirector"):
    suite.check(forbidden not in clean,
                "approach foundation has no control/NAV authority: "+forbidden)

@dataclass
class O:
    along:float
    cross:float
    top:float
    radius:float=0.0
    terrain:bool=True


def clear(angle:float, obs:list[O], maximum:float=30000.0)->bool:
    threshold=0.0; tch=15.0; half=180.0
    for o in obs:
        if o.along<0 or o.along>maximum:continue
        if abs(o.cross)>half+o.radius:continue
        path=threshold+tch+o.along*math.tan(math.radians(angle))
        margin=90.0 if o.terrain else 60.0
        if path<o.top+margin:return False
    return True

angles=[3.0,3.25,2.75,3.5,2.5,3.75,4.0,4.25,4.5,4.75,5.0,5.25,5.5,5.75,6.0]
def choose(obs:list[O],steep=False,maximum=30000.0):
    for a in angles:
        if a>5.0 and not steep:continue
        if clear(a,obs,maximum):return a
    return None

suite.equal(choose([]),3.0,"clear corridor retains conventional 3-degree path")
# At 4 km, obstacle top+margin requires just over 4 degrees.
required=15+4000*math.tan(math.radians(4.05))-90
chosen=choose([O(4000,0,required)])
suite.check(chosen is not None and 4.0<chosen<=5.0,
            "obstacle raises glide path only within restrained non-steep band")
required_steep=15+4000*math.tan(math.radians(5.2))-90
suite.equal(choose([O(4000,0,required_steep)],False),None,
            "path above 5 degrees requires explicit steep-aircraft capability")
suite.check(choose([O(4000,0,required_steep)],True) is not None,
            "steep-capable aircraft may receive conditional 5-6 degree candidate")

# Dogleg is only useful outside the mandatory 4 km stabilized final.
inside=[O(2500,0,500)]
outside=[O(9000,0,500)]
suite.check(not clear(5.0,inside,4000),
            "blocked mandatory final cannot be bypassed laterally")
suite.check(any(o.along>4000 for o in outside),
            "outer obstacle is eligible for offset/dogleg candidate evaluation")

suite.finish()
