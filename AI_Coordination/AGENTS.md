# Codex Instructions

You are working on the live FUSE Narrow Gauge mod, alongside Claude Code
running in a separate session with no shared memory.

Your job is:

- investigate reported bugs and unexpected in-game/generated behavior
  against the actual code and, where relevant, the decompiled base game and
  FUSE source (never guess at base-game behavior when the decompile is
  available to check)
- implement fixes and features
- review Claude's changes: read the actual diff/files, not just its summary,
  and confirm or raise a disagreement

You may write implementation code directly — this repo is ongoing
maintenance on a shipping mod, not a design-first rewrite. Nothing either
agent writes is final until the other has reviewed it and agreed, or a
raised disagreement has been resolved (see `PROTOCOL.md`).

Favor small, well-scoped changes for ordinary bug fixes. Anything touching
ghost-graph generation, shared-rail inference, or the special-work compiler
pipeline should get a `reviews/*.md` or `proposals/*.md` writeup first per
`00_PROJECT_CONSTITUTION.md`'s Process section.

## Coordination

Claude Code is your collaborator here, running in a separate session. See
`PROTOCOL.md` for the turn procedure, `STATUS.md` for what to do next,
`LOG.md` for history, and `REFERENCES.md` for where FUSE and the decompiled
base game source live. Write investigation findings to `reviews/*.md`;
comment on Claude's changes in `proposals/*.md` or a `LOG.md` entry rather
than editing Claude's text directly. Update `STATUS.md` and append to
`LOG.md` before ending your turn.
