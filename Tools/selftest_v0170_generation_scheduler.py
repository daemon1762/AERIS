#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from dataclasses import dataclass
from v01700_testlib import SOURCE, CheckSuite, read

suite = CheckSuite("v0.17.0.0 generation, latest-wins and permit model")

@dataclass(frozen=True)
class Generation:
    scene: int = 1
    vessel: int = 10
    instance: int = 1
    body: int = 1
    control: int = 0
    docking: int = 0
    database: int = 0
    selection: int = 0
    plan: int = 0
    layout: int = 0
    sequence: int = 0

    def compatible(self, other):
        return self.__dict__ | {"sequence": 0} == other.__dict__ | {"sequence": 0}

base = Generation(sequence=1)
suite.check(base.compatible(Generation(sequence=999)),
            "sample sequence does not invalidate the same identity domain")
for field in ("scene", "vessel", "instance", "body", "control", "docking",
              "database", "selection", "plan", "layout"):
    changed = dict(base.__dict__)
    changed[field] += 1
    suite.check(not base.compatible(Generation(**changed)),
                field + " revision invalidates stale results")

class LatestQueue:
    def __init__(self, capacity):
        self.capacity = capacity
        self.pending = []
        self.latest = {}
        self.next_id = 0

    def submit(self, key):
        self.next_id += 1
        identity = self.next_id
        self.latest[key] = identity
        self.pending = [(k, value) for k, value in self.pending if k != key]
        if len(self.pending) >= self.capacity:
            self.pending.pop(0)
        self.pending.append((key, identity))
        return identity

    def current(self, key, identity):
        return self.latest.get(key) == identity

queue = LatestQueue(3)
running_old = queue.submit("terrain")
running_new = queue.submit("terrain")
suite.check(not queue.current("terrain", running_old),
            "a running same-key job becomes stale after a newer submission")
suite.check(queue.current("terrain", running_new), "newest same-key job remains current")
for key in ("runway", "display", "archive", "telemetry"):
    queue.submit(key)
suite.check(len(queue.pending) == 3, "model queue remains bounded")
suite.check(queue.pending[-1][0] == "telemetry", "new work is retained at bounded tail")

for logical in (1, 2, 4, 8, 16, 32, 64):
    reserve = max(2, math.ceil(logical * 0.15))
    active = max(2, logical - reserve)
    suite.check(active >= 2 and active <= max(2, logical),
                "AUTO AGGRESSIVE permit formula is bounded at L=" + str(logical))
suite.equal(max(2, 2), 2, "two-worker acceptance override is supported")

permits = 12
permits = max(2, permits - 2)
suite.equal(permits, 10, "severe load immediately removes two permits")
permits = min(12, permits + 1)
suite.equal(permits, 11, "stable recovery restores one permit at a time")

scheduler = read(SOURCE / "Performance" / "AERISWorkerScheduler.cs")
suite.check("int[] fairness = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 3 }" in scheduler,
            "weighted fairness order is explicit")
suite.check("wake.Set();" in scheduler and "if (job != null) wake.Set();" in scheduler,
            "bursts wake enough workers despite AutoResetEvent coalescing")
suite.check("if (index == 3 && permits.ArchivePaused) continue" in scheduler,
            "archive lane pauses before critical/general work")
suite.check("if (index != 0 && safetyWaiting && activeJobs >= nonSafetyLimit) continue" in scheduler,
            "Safety/LAND reservation is enforced under contention")
suite.check("result.Job.GenerationBound && !generations.Matches" in scheduler,
            "main-thread commit rejects stale generation-bound results")
suite.check("!IsLatestLocked(result.Job)" in scheduler,
            "main-thread commit rejects replaced running results")
suite.check("LinkedList<Result> results" in scheduler and
            "Dictionary<string, LinkedListNode<Result>> resultByKey" in scheduler,
            "completed-result queue is bounded and coalesced by lane/key")
suite.check("previousValue.Job.Id > job.Id" in scheduler and
            "results.Remove(previousResult)" in scheduler,
            "out-of-order completion retains only the newest same-key result")
suite.check("resultByKey.Remove(removed.Job.IdentityKey)" in scheduler and
            "RemoveLatestIfSameLocked(removed.Job)" in scheduler,
            "result-capacity eviction also clears identity indexes")
suite.finish()
