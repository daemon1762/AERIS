# Runtime targets

Compare against the accepted Witness-Bounded Affine runtime:
- 80 km exact projection about 7.9/s; BACK median about 3.07 ms
- 160 km exact projection about 22.4/s; BACK median about 4.40 ms; p95 about 22.8 ms

The successor may slightly increase average exact refresh rate because deadlines are 2.80-3.90 s rather than synchronized 4.00 s. This is acceptable only if steady READY burst size and BACK p95 improve materially while median, visual quality, Runway Map Lock, 10 Hz authority, and mesh churn remain within the accepted affine envelope.

Primary new counters:
- oh_stagger_due
- oh_stagger_defer
- oh_stagger_back_peak
- oh_stagger_back_samples
- oh_stagger_back_gt8
