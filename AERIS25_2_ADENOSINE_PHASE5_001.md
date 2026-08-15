# AERIS25-2 — ADENOSINE / OH_PHASE5_001

Parent acceptance point: `9def69bd616f976e5f6b88bdb25f8fc9481b1024` (AERIS25-1 ATROPINE rev009 runtime accepted).

Feature: Persistent Presentation / Submission Batching.

Phase5_001 introduces a compact persistent presentation packet set for the current terrain content snapshot. The packet set is reused across motion-only fixed 10 Hz presentations and rebuilt only when selected tile/Entry authority changes. The same packet set backs an O(1) `HashSet<Entry>` snapshot Mesh lifetime pin lookup, replacing rev008's linear scan for each prune candidate.

Hard invariants:
- visible ND authority remains fixed 10 Hz;
- user-visible 160 km range unchanged;
- per-Entry painter order remains `terrain -> contour -> coastline`;
- no global layer reordering/batching;
- ARGB32/Bilinear unchanged;
- Runway Map Lock and Golden visual floor unchanged;
- AERIS25-1 rev009 burst governor and rev008 Mesh lifetime guard retained;
- AA/AP/PROTECT/LAND unchanged.

Runtime telemetry:
- `oh_presentation_packet_count`
- `oh_presentation_packet_rebuild`
- `oh_presentation_packet_reuse`
- `oh_presentation_packet_slot_skip`
- `oh_presentation_pin_hit`
- `oh_presentation_pin_miss`
- `oh_presentation_packet_draw`

Primary runtime criterion: during steady 160 km motion, packet reuse must materially outgrow packet rebuild while `oh_snapshot_stale_mesh=0`, GPU attr failures remain zero, Golden visual remains intact, and performance is no worse than accepted ATROPINE rev009.
