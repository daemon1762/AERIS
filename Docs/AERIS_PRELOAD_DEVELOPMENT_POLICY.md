# AERIS Preload Development Temporary Workflow Policy

Status: **MANDATORY while preload improvement work is active**

This policy temporarily supersedes the stricter “do not commit until runtime certification” workflow for the preload-improvement workstream only.

## Purpose

AERIS preload development is tested on both the desktop and laptop KSP installations. Uncommitted patches that exist on only one machine make cross-machine testing harder to reproduce.

During preload improvement, Git is therefore used not only as an accepted-history archive but also as the transport layer for buildable experimental candidates.

## Dedicated branch

Canonical preload-development branch:

`agent/aeris36-preload-development`

The accepted/certified branch remains the rollback authority.

The preload-development branch may contain buildable but not yet runtime-certified candidate commits.

## Candidate rule

Use **one improvement / one candidate commit** whenever practical.

A candidate may be committed and pushed after:

1. intended source scope is verified;
2. `git diff --check` passes;
3. relevant static guards pass;
4. local Release build succeeds.

Runtime certification is **not required before creating a candidate commit** on this dedicated branch.

Candidate commit messages must identify the state clearly, for example:

`AERIS R040B P1A candidate1 Minmus whole-block worker shadow`

Do not describe an unverified candidate as accepted or certified.

## Cross-machine rule

Desktop and laptop should test the **same Git SHA**.

Normal remote-machine flow:

```bash
git pull --ff-only
./Tools/AERIS_preload_build_and_go.sh
```

Explicit machine forms remain supported:

```bash
./Tools/AERIS_preload_build_and_go.sh desktop
./Tools/AERIS_preload_build_and_go.sh laptop
```

The Build & Go script records branch, Git SHA, dirty-state/diff fingerprint, and DLL SHA256 in the installed KSP tree.

## Build & Go safety

Default Build & Go requires a clean worktree.

This is intentional: a clean tree means the tested DLL corresponds directly to a candidate Git SHA that can be reproduced on the other machine.

`--allow-dirty` is reserved for deliberate development-PC experiments before the candidate commit is created.

`--pull` may be used to fast-forward from the shared branch before building.

## Git discipline

Do not use:

- `git add .`
- `git add -A`
- history rewriting on the shared preload-development branch
- blind `reset --hard`, `clean`, or automatic stash against old intentionally dirty worktrees

Stage exact paths only.

The canonical active worktree is `$HOME/AERIS32_R039_ACCEPT`.

The old intentionally dirty `$HOME/AERIS32` remains read-only unless a historical materialization must be inspected.

## Acceptance boundary

`candidate` means:

- source scope checked;
- static checks passed;
- build passed;
- pushed for reproducible testing.

`certified` / `accepted` means:

- required runtime evidence has passed;
- regressions relevant to that change have been checked;
- the result is approved as a rollback-quality state.

Do not confuse these states.

## End of temporary workflow

This workflow applies **until preload improvement work is completed**.

When preload development is finished:

1. certify the final preload result;
2. preserve/merge the accepted result according to the project’s normal release procedure;
3. retire the temporary preload-development workflow;
4. return to the project’s normal stricter commit/acceptance discipline.

## Mandatory handover requirement

Every handover document written while this workflow is active **must explicitly state this policy**, including:

- the dedicated preload-development branch;
- candidate-before-runtime-certification permission;
- one-improvement/one-candidate principle;
- GitHub as the desktop/laptop transport mechanism;
- Build & Go usage;
- the distinction between candidate and certified/accepted;
- the fact that this workflow is temporary and ends when preload improvement is complete.

This section must not be omitted from future AERIS handover documents while preload work remains active.
