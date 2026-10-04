# AERIS53 REG-02 facility provenance verification

Verified on 2026-10-04 in the user's KSP installation with Kerbal Konstructs.
Production source/build commit: `e65d8156e81650b4927e2407d766ecb3ea6eda06`.
Branch retained: `agent/aeris53-reg-02`; no main or accepted branch merge.

## Problem and scope

The KSP provider treated entries in `PSystemSetup.SpaceCenterFacilities` as
Squad-owned Stock, including facilities dynamically inserted by KK/SLE.
The pre-fix runtime observer found all 26 linked non-Squad runways incorrectly
classified Stock. The detached KSP-only missing-provider-twin scenario accepted
all 26 at the automatic-certification gate. The detached KSP+KK scenario accepted
none because federation preferred the non-Stock twin.

The production change is confined to `AERISAirfieldProviders.cs`. It resolves
exact KK object ownership, uses native KSC internal IDs and registered transform
paths for fallback, and recognizes localized native facility IDs. Failed or
partial KK discovery does not stop KSP collection; observed non-Squad links
remain authoritative. Geometry, federation, certification gates, cached/manual
A/B authority and flight control implementation are unchanged.

## Build and runtime evidence

The user supplied the terminal output in `貼り付けたテキスト（1）(2).txt`.
The candidate build ran 122 successful checks: 34 production provenance cases,
14 temporary observer cases and 74 existing flight-control cases.
The full Release build completed with 77 warnings and 0 errors, matching the
previous build's warning count. Built and installed DLL SHA-256 both equal:

`0f93061f542f596a0af38e14f54ea8e2f89bceb8278be67fcdc97e31d58b7972`

The one-shot runtime report at `2026-10-04T14:26:46.7927763Z` records:

| Observation | Result |
|---|---:|
| KK launch sites | 93 |
| Non-Stock KK provider records | 90 |
| KSP provider records | 68 |
| Linked non-Squad runways | 26 |
| Misclassified Stock runways | 0 |
| Non-Stock origin mismatches | 0 |
| Detached KSP+KK automatic gate eligibility | 0 |
| Detached KSP-only missing-twin automatic gate eligibility | 0 |
| Native KSC Runway records | 1 |
| Native KSC Runway Stock / Squad-owned | 1 |
| Native KSC Runway automatic gate eligibility | 1 |

Report result: `NO_MISCLASSIFICATION_OBSERVED`.
The Squad-owned `KSC2` record also remained Stock.

Eligibility means permission at the existing automatic-certification gate;
these observations do not assert that any approach was certified. The detached
provider scenarios exclude the survey catalog and are not the live registry.
KK-absent behavior was covered by a separate real-source boundary executable;
this was not a full game session with KK removed. The runtime native baseline
covers the observed KSC Runway, not every stock/DLC facility configuration.

## Finalization

The user ran `collect`, which retired the entire probe directory to:

`/home/de-mon/.cache/AERIS/reg02-repro/20261004-232821-467364893`

The temporary observer source, observer fixture and repro installer are removed
from the branch. The durable production regression remains; the build/install
helper now runs 108 checks (34 REG-02 and 74 existing controls) and installs only
the production DLL. Finalization changes no production source, so the verified
installed DLL remains valid without rebuilding. No permanent audit feature was
added. Untracked candidate identity/shader artifacts are left intact.
