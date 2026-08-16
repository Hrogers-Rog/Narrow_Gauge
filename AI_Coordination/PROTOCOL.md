# Coordination Protocol

Claude Code and Codex CLI run in separate terminal sessions with no shared
memory. This directory is the only channel between them. The user runs one
agent, then the other, alternating turns; each agent reads these files at the
start of its turn and writes to them before stopping.

## Files

| File | Mutability | Purpose |
|---|---|---|
| `STATUS.md` | Overwritten each turn | One-page snapshot: current phase, whose turn is next, open questions/blockers. Read this first. |
| `LOG.md` | Append-only | Chronological narration of what each agent did and why. Never edit or delete a past entry — append a correction instead. This is the audit trail. |
| `REFERENCES.md` | Updated as needed | Absolute paths to code this repo depends on or is reviewed against (FUSE, decompiled base game) and what lives where. |
| `reviews/*.md` | Living documents | One file per subsystem being investigated. Edited in place as understanding deepens. This is where findings about *existing* code accumulate. |
| `proposals/*.md` | Living documents | One file per design decision for a non-trivial change (see `00_PROJECT_CONSTITUTION.md`'s Process section for what needs one). Revised in response to the other agent's investigation notes and challenges. |
| `00_PROJECT_CONSTITUTION.md` | Status-gated | Project goals and principles. Amendments follow the agreement rule below like any other truth claim. |
| `CLAUDE.md` / `AGENTS.md` | Stable | Per-agent role instructions, read automatically by each CLI on startup in this repo. |

## Nothing is truth until both sides agree

A review finding, a constitution change, or a proposal is a claim, not a
fact, until the other agent has actually looked at it and signed off. One
agent writing something down does not make it settled.

- Every `proposals/*.md` file (and `00_PROJECT_CONSTITUTION.md`) starts with
  a status line: `Status: Draft` → `Status: Discussing` → `Status: Agreed
  (Claude + Codex, YYYY-MM-DD)`.
- Only `Agreed` content may be treated as decided or implemented against.
  `Draft`/`Discussing` content is a working hypothesis from whoever wrote it.
- To challenge something, don't silently rewrite the other agent's words or
  delete their reasoning. Add a clearly attributed entry under an `## Open
  disagreements` section in that same file: what you disagree with, why, and
  what you'd do instead. Set the file's status to `Discussing`.
- The other agent responds in that same section on their next turn: concede
  (fold the change into the main text, remove the resolved disagreement) or
  counter-argue. Keep going back and forth in that section, file to file,
  turn to turn — this is the debate, it doesn't need to happen anywhere else.
- If neither side concedes after a round or two, stop looping: record the
  disagreement in `STATUS.md` under "Open questions / blockers" and set
  "Next turn" to `user`. The user breaks the tie.
- `reviews/*.md` findings about *existing* code (what a file currently does)
  don't need a status header — that's observation, not a design decision.
  But if one agent thinks a review's *interpretation* is wrong (e.g. what's
  load-bearing vs. incidental), that's a disagreement too and follows the
  same challenge/respond pattern in the review file itself.

## Turn procedure

1. Read `STATUS.md`, then the tail of `LOG.md` (last few entries), then any
   `reviews/` or `proposals/` files relevant to the stated next step.
2. Check whether anything relevant to your turn has an open
   `## Open disagreements` entry addressed to you — respond to it before
   starting new work.
3. Do the work for your role (see `CLAUDE.md` or `AGENTS.md`). Both agents
   may write implementation code (see `00_PROJECT_CONSTITUTION.md`) — this
   is ongoing maintenance on a shipping mod, not a design-first rewrite.
4. Update or create the relevant `reviews/*.md` or `proposals/*.md` file(s)
   with your findings/design — these should read as current-best-understanding,
   not a diff or transcript. Follow the agreement rule above for anything
   that's a decision rather than an observation.
5. Append one entry to `LOG.md` summarizing what you did, what you found, and
   what should happen next.
6. Rewrite `STATUS.md` to reflect the new phase/blockers and set "Next turn"
   to the other agent (or "user" if a decision is needed from the user).
7. Build and, where applicable, test before committing: confirm the mod
   still builds against a real Railroader install (`dotnet build
   .\NarrowGaugeMod.csproj`) and describe how a change was exercised
   (unit test, or a concrete manual/in-game check) in the `LOG.md` entry.
8. Commit everything from this turn: `git add -A && git commit -m "..."`.
   One commit per turn, message summarizing what changed and who made the
   turn (e.g. `[Codex] fix shared-rail side inference for branching wye`).
   Commit every turn, including pure coordination-file edits — this is the
   history of the debate, not just the code.
9. Stop. Do not try to invoke the other agent yourself.

## Roles (summary — full detail in CLAUDE.md / AGENTS.md)

- **Claude**: investigates, implements, and reviews. Keeps architecture-level
  judgment primary — favors the existing layering, flags anything a fix
  reveals is wrong above the immediate bug.
- **Codex**: investigates, implements, and reviews. No fixed split beyond
  whoever picks up the next item in `STATUS.md` — both agents do both kinds
  of work.

## Log entry format

```
### [Claude|Codex] YYYY-MM-DD HH:MM — Short title
What you did. What you found. What's next, and for whom.
```
