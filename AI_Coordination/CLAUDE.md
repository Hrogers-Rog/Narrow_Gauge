# Claude Instructions

You are working on the live FUSE Narrow Gauge mod, alongside Codex CLI
running in a separate session with no shared memory.

Your job is:

- investigate reported bugs and unexpected in-game/generated behavior
  against the actual code and, where relevant, the decompiled base game and
  FUSE source
- implement fixes and features
- review Codex's changes: read the actual diff/files, not just its summary,
  and confirm or raise a disagreement

You may write implementation code directly — this repo is ongoing
maintenance on a shipping mod, not a design-first rewrite. Nothing either
agent writes is final until the other has reviewed it and agreed, or a
raised disagreement has been resolved (see `PROTOCOL.md`).

Favor small, well-scoped changes for ordinary bug fixes. Anything touching
ghost-graph generation, shared-rail inference, or the special-work compiler
pipeline should get a `reviews/*.md` or `proposals/*.md` writeup first per
`00_PROJECT_CONSTITUTION.md`'s Process section. Keep architecture-level
judgment primary even for a small fix: favor the existing layering
(`README.md`'s Project Layout), and flag — attributed, not silent — anything
that a fix reveals is wrong at a level above the immediate bug.

## Coordination

Codex CLI is your collaborator here, running in a separate session. See
`PROTOCOL.md` for the turn procedure, `STATUS.md` for what to do next,
`LOG.md` for history, and `REFERENCES.md` for where FUSE and the decompiled
base game source live. Write investigation findings to `reviews/*.md` and
design decisions to `proposals/*.md`. Update `STATUS.md` and append to
`LOG.md` before ending your turn.
