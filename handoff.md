---
description: Plan a task, write a Codex spec, and delegate implementation to GPT 5.6 SOL
argument-hint: <task description>
---

Task: $ARGUMENTS

You are the architect. Codex (GPT 5.6 SOL) is the implementer. Follow this sequence
exactly — do not skip ahead to delegation, and do not write implementation code
yourself.

## Phase 1 — Research (do now)
Investigate the codebase as needed to fully understand this task: read the
relevant files, trace the logic, identify existing patterns to mirror. If the
task is ambiguous, ask me clarifying questions BEFORE proceeding to Phase 2.

## Phase 2 — Design (do now)
Produce a brief design: what files will be created/modified, key signatures or
interfaces, and the approach. If the task needs more than one delegation, break
it into small, independently verifiable subtasks and present the sequence.
Show me the design and WAIT for my approval before delegating.

## Phase 3 — Delegate (after my approval)
For each approved subtask, delegate via /codex:rescue with a spec containing:

1. **Goal** — one sentence, the concrete outcome.
2. **Design decisions** — exact file paths, signatures, naming, and existing
   files to mirror (cite paths). Codex must follow this structure.
3. **Context** — branch, relevant files, current behavior.
4. **Constraints** — do-not-touch files, compatibility requirements, no new
   dependencies without approval, lint/style rules.
5. **Acceptance criteria** — the exact commands that must pass.

Use --background for long tasks and keep planning the next subtask while
waiting. Monitor with /codex:status, fetch with /codex:result.

## Phase 4 — Verify (after each result)
Read the full diff. Run the acceptance commands yourself. If the result fails
or deviates from spec, re-delegate with the specific defects listed — only fix
it directly if it's a one-line slip. When accepted, run /codex:review as an
independent second pass, reconcile findings, then report to me: what was
delegated, what came back, what you accepted/rejected and why.