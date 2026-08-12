Apply only with:
python3 Tools/apply_aeris23_staggered_exact_refresh_candidate.py

Then run:
PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py
git diff --check

Expected build/runtime candidate:
AERIS23_AFFINE_STAGGERED_EXACT_REFRESH
