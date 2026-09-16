---
name: yolo
description: Use when the user types /yolo, or asks for a ticket or task in this repository to be carried end to end on its own — planned, implemented, built, tested, reviewed, committed, pushed and offered as a pull request — without being consulted along the way.
argument-hint: "<issue number | task text | resume> [--no-plan-needed] [--no-flake-retry] [--no-staleness-check] [--max-cycles:N]"
disable-model-invocation: true
---

# YOLO — implement → validate → loop

Carry one task from ticket to an offered pull request, unattended. The user is away. Exactly
**three questions** are allowed (below). Everything else is either decided by you and logged, or
ends the run as `STOPPED` or `BLOCKED` with the final report. Source spec: GitHub issue #42.

Arguments: `$ARGUMENTS`

| Argument | Meaning |
| :--- | :--- |
| `57`, `#57`, issue URL | `gh issue view 57 --json title,body,comments,labels` |
| free text | the task itself |
| `resume` or nothing | continue from the newest run state file (see Run state) |
| `--no-plan-needed` | answers question 1 with yes without asking |
| `--no-flake-retry` | every test failure is real; no rerun |
| `--no-staleness-check` | skip the plan staleness check |
| `--max-cycles:N` | fix-cycle limit, default `5` |

## The three questions — the whole budget

Ask each with `AskUserQuestion`, and only when its condition holds:

1. **Plan gate** — no plan doc was found and `--no-plan-needed` is absent. *"No plan doc covers
   this task. Proceed without one?"* First option: **No — stop (default)**.
2. **Split approval** — proceeding without a plan and the work may need more than one PR. Show the
   proposed PRs, one line each.
3. **PR confirmation** — before every `gh pr create`, once per PR. Show title, base, summary and
   the open review findings.

"Go", "ship it", "I trust you" and `/yolo` itself start the run. They answer none of the three.
Anything else you would like to ask is either a *how* (decide it, log it under Decisions) or an
unsettled *what* (end `BLOCKED` — see Hard stops). A progress note that needs no reply is fine;
never wait on one.

## Run state

Keep `.superpowers/yolo/<slug>.md` in the **main checkout** (`.superpowers/` is gitignored). Write
it before the first edit and rewrite it after every step and gate: task source and text, branch,
worktree path, plan doc (or `none — approved` / `none — --no-plan-needed`), PR split, steps with
status, cycle `n/max`, error signatures seen, decisions, pre-existing dirty paths, ledger so far.

**Resume:** take the newest state file by modification time. Its branch must exist and
`git log origin/master..<branch>` must match the steps it marks done — no missing step commit, no
commit it doesn't know. Continue at the first step or gate not done, with its cycle count and seen
signatures. Any mismatch → `BLOCKED — run state and branch disagree: <what>`.

## 1 · Read the task

Issue number → read body and comments. Free text → take as written. Pick a short kebab-case
`<slug>`; the branch is `claude/<slug>`.

## 2 · Readiness gate

Design lands in `docs/` before code (CLAUDE.md). `BLOCKED — design owed` when any holds:

- the task changes game behaviour — kernel rules, content semantics, save format, a new shell
  surface — and no spec in `docs/superpowers/specs/` covers it;
- the covering spec lists an open item the task depends on;
- the issue or its comments say design or spec work is pending.

Fixes, behaviour-preserving refactors, tests, tooling and docs owe nothing.

## 3 · Plan gate

Find a plan in `docs/superpowers/plans/` that cites the issue (`**Issue:** [#N]`, `Ticket #N`) or
the covering spec's filename.

- **Found, `Status: Built`** → `BLOCKED — plan says this is already built`.
- **Found, `Status: Draft`** → staleness check unless `--no-staleness-check`: every file it says to
  modify exists; every file it says to create does not (unless this run created it); every type and
  member it names is found by grep; `git log --since=<plan date> -- <its files>` shows no commit the
  plan does not account for. Any miss → `BLOCKED — plan stale: <each miss>`.
- **Not found** → `--no-plan-needed` present: proceed. Otherwise ask question 1. No → `BLOCKED —
  plan owed`.

Do not write a plan doc to get past this gate — the plan gate exists to ask whether one is owed.
Without a plan, draft the steps internally. If they fall into more than one reviewable unit (a
kernel change and a separable shell change; more than ~8 steps; more than ~20 files) → ask
question 2. One PR clearly → no question.

## 4 · Branch and implement

```bash
git fetch origin master
git status --porcelain
```

- **Empty** → `git switch --no-track -c claude/<slug> origin/master`
- **Not empty** → isolate: `git worktree add --no-track -b claude/<slug> ../DimenshipGame-<slug> origin/master`,
  and run everything from there. Record the dirty paths; never touch them. An untracked `.cs` inside
  a test project compiles into that suite, and a modified file follows a branch switch.

Branch from `origin/master`, never local `master`, and never pull into the user's `master`.

`mkdir -p artifacts/yolo`, then restore once — `dotnet restore` on `tests/Dimenship.Core.Tests`,
`tests/Dimenship.Shell.Tests` and `dimenship/Dimenship.csproj` — and everything below runs
`--no-restore`. Re-restore only a project
whose `.csproj` a step changes. If the Godot project cannot restore and the task never touches
`dimenship/`, log its build `SKIPPED (Godot.NET.Sdk unavailable)`; if it does touch it → `BLOCKED`.

**Baseline** before step 1: build and run both suites. Red → `BLOCKED — baseline red: <tests>`.
Pre-existing failures are not yours, and they would hide yours.

**Steps:** numbered; each touches **≤ 5 files** (a Godot `.uid` / `.import` sidecar rides with its
file and does not count); each ends green. Where behaviour changes, write the test first —
**REQUIRED SUB-SKILL:** superpowers:test-driven-development. If a plan doc drives the run, its
final step sets `Status: Draft` → `Status: Built`.

**Verify each step** — Release, no restore, logs under `artifacts/yolo/` (gitignored), named by
step (`s3-…` below is step 3):

| Step touched | Build | Test |
| :--- | :--- | :--- |
| `src/Dimenship.Core/**` | Core.Tests, Godot assembly | Core.Tests |
| `src/Dimenship.Shell/**` | Shell.Tests, Godot assembly | Shell.Tests |
| `tests/<Suite>/**` | that suite | that suite |
| `dimenship/content/**` | Core.Tests | Core.Tests (the fidelity test reads the shipped tree) |
| other `dimenship/**` | Godot assembly | — |
| `*.csproj`, `Directory.Build.props`, `*.sln` | everything | both |
| `docs/**`, `.claude/**`, `*.md` | — | — |

```bash
dotnet build tests/Dimenship.Core.Tests -c Release --no-restore -nologo -v q > artifacts/yolo/s3-build-core.log 2>&1
dotnet test  tests/Dimenship.Core.Tests -c Release --no-build --logger "trx;LogFileName=s3-core.trx" --results-directory artifacts/yolo > artifacts/yolo/s3-test-core.log 2>&1
dotnet build dimenship/Dimenship.csproj -c Release --no-restore -nologo -v q > artifacts/yolo/s3-build-godot.log 2>&1
```

Both suites together take about 15 s — run whole projects, don't filter. Exit 0 → read only the
summary line (`Passed!  - Failed: 0, …`). Non-zero → a fix cycle — except the red run a new test
is written to produce: when the only failures are the tests this step just added, failing for the
reason they were written, that is the test working, not a cycle.

**Green step → commit it** (Commit rules). Local only; nothing is pushed until Phase B.

**Fix cycle:**

1. **haiku** parses the log → failing test or compiler error, `file:line`, first message line.
2. Signature = test name or error code + file + first message line. Already seen this run →
   `STOPPED — same error twice`.
3. **sonnet** finds the root cause — **REQUIRED SUB-SKILL:** superpowers:systematic-debugging.
4. Re-plan the step, `cycle += 1`. Over the limit → `STOPPED — cycle limit`.
5. Back out a failed attempt by editing it back — never by a discarding git command.

**Flake retry** (unless `--no-flake-retry`): rerun the one failing test with
`--filter "FullyQualifiedName=<name>"`. Passes on rerun → **sonnet** judges it. In Core.Tests a
pass on rerun is a determinism defect — the kernel promises identical replays — so
`STOPPED — nondeterministic <test>`, never retried away. In Shell.Tests log `flake: <test>` and
continue.

## 5 · Phase A — local gate, in parallel

Starts when every step is committed and the tree is clean. Launch together:

- **One background Bash command**: `dotnet build DimenshipGame.sln -c Release --no-restore` (or the
  per-project builds when the Godot build is `SKIPPED`), **then** `dotnet test --no-build` for every
  suite the table maps from `git diff --name-only origin/master...HEAD`. Build and test go in one
  command, in that order: two `dotnet` processes building the same projects fight over `obj/` locks.
- **opus** subagent — code review of `origin/master...HEAD`.
- **opus** subagent — cold read of the same range.

Prompts and the severity scale are in `reviewer-prompts.md` beside this file; send them as
written. Then:

- any 🔴 → back to step 4 as new fix steps, `cycle += 1`;
- 🟡 / 🔵 → listed in the PR body and the report, not fixed;
- a reviewer returned no findings list (crash, context limit, timeout) → re-run it once on the diff
  split per file group; still nothing → `STOPPED — review did not run`. A review that did not run
  is never `PASS`, and your own read of the diff is not a review.

Phase A covers the commits it read and nothing after them. Any later commit — a fix from Phase A,
from CI, or an edit left uncommitted when it started — means Phase A runs again on the new `HEAD`.
There is no flag that skips Phase A.

## 6 · Phase B — push, CI, PR

Only after Phase A has run completely with no 🔴.

```bash
git status --porcelain                              # empty, apart from recorded dirty paths
git fetch origin master
git merge-base --is-ancestor origin/master HEAD     # exit 1: say "master has moved" in the PR; never rebase
git push -u origin claude/<slug>
gh workflow run ci.yml --ref claude/<slug> -f configuration=Release -f include-godot-project=true
gh run list --workflow ci.yml --commit "$(git rev-parse HEAD)" --limit 1 --json databaseId,status
gh run watch <id> --exit-status                     # about a minute; run in the background
```

Take the run id from the URL `gh workflow run` prints; when it prints none, the `gh run list` line
finds it, though a new run can take a few seconds to appear.
CLAUDE.md says CI runs only when someone asks for it — `/yolo` is that request, so dispatch it.
Red → `gh run view <id> --log-failed` to a file, **haiku** parses it. A Godot.NET.Sdk feed failure
with green suites → log `CI FAIL (SDK feed; suites green)` and continue. Anything else → fix cycle,
push the new commits, dispatch again.

Then **question 3**. Yes →

```bash
gh pr create --base master --head claude/<slug> --title "<commit-style subject>" --body-file artifacts/yolo/pr-body.md
```

PR body: `Closes #N`; what changed; verification (build, suites with counts, CI run); decisions
made; open 🟡 / 🔵 findings; "master has moved" if it has; the session's PR attribution line last.
No → the branch stays pushed, `PR DECLINED`.

**Several PRs** (question 2 approved a split): one branch each, `claude/<slug>-1`, `-2`, …; a
dependent branch starts from its predecessor and its PR targets it (`--base claude/<slug>-1`). Each
gets its own Phase A, Phase B and question 3.

## Commit rules

- Stage by path only: `git add -- <paths>`, then `git diff --cached --name-only` must equal the
  step's file list. Never `git add -A`, `git add .` or `git commit -a`.
- Message per CLAUDE.md: `feat:` / `fix:` / `test:` / `docs:` / `refactor:` + a lowercase
  declarative sentence about behaviour; a prose body that says what was rejected; the session's
  commit attribution trailer. Write it normally even when replies are terse.
- Fixes are new commits. Never amend.

## Hard stops

| Condition | Ends |
| :--- | :--- |
| same error signature twice | `STOPPED` |
| cycle limit passed | `STOPPED` |
| a reviewer could not run | `STOPPED` |
| context pressure — a context summary has happened in this run | commit the current step if it is green, otherwise leave its edits uncommitted and list them; update run state; `STOPPED — context pressure; /yolo resume` |
| a requirement unsettled about *what* | `BLOCKED` |
| readiness, plan, staleness or baseline gate | `BLOCKED` |

**What vs how.** *How* — structure, names, which class, which algorithm — is yours: decide and log
it. *What* — behaviour, scope, a player-visible rule, vocabulary — is settled when the task, a
covering spec, the GDD or **existing behaviour** answers it; follow that and log it. Settled by
none → `BLOCKED` with the exact question and the options you see. Don't pick one, don't ship a
no-effect stub in its place, and don't ask it as a fourth question.

## Never

- merge, approve, or enable auto-merge on a PR;
- push to `master`, `release/*` or any tag;
- rewrite pushed history — except `git push --force-with-lease` on this run's own unmerged branch;
- discard uncommitted work: no `git restore`, `git checkout -- <path>`, `git reset --hard`,
  `git clean`, `git stash`;
- skip the review or the cold read;
- ask a fourth question.

## Subagents

Pass a file path to read, never a pasted log. Set the Agent tool's `model`.

| Model | Job |
| :--- | :--- |
| `haiku` | parse build, test and CI logs into failures |
| `sonnet` | root cause of a failure; judging a pass-on-rerun |
| `opus` | code review; cold read |

## Rationalizations seen in testing

| Thought | Reality |
| :--- | :--- |
| "The user said *go, I trust you* — no need to confirm the PR" | That started the run. Question 3 is asked for every PR. |
| "`/yolo` means hands-off, so I'll skip the plan question" | The plan gate is one of the three questions `/yolo` keeps. |
| "No plan exists, so I'll write one and build from it" | Writing a plan is not the gate. Ask question 1. |
| "CLAUDE.md says CI only runs when someone asks" | `/yolo` asked. Dispatch it. |

## Final report

Every run ends with this block, every line filled. Values: `PASS`, `FAIL`, `BLOCKED`,
`SKIPPED (<why>)`, `NOT REACHED`.

```
YOLO #57 "<title>" — COMPLETE | STOPPED — <gate>: <reason> | BLOCKED — <what the user must settle>
Branch      claude/<slug>  [worktree <path>]  ·  cycles <n>/<max>
Readiness   PASS | BLOCKED <why>
Plan        PASS <plan path> | PASS none (approved) | PASS none (--no-plan-needed) | BLOCKED <why>
Staleness   PASS | SKIPPED (<why>) | BLOCKED <misses>
Split       PASS 1 PR | PASS <n> PRs (approved) | NOT REACHED
Baseline    PASS core <n>/<n> · shell <n>/<n> | BLOCKED <tests>
Steps       PASS <k>/<k> | FAIL at <i>/<k>: <signature>
Build       PASS | FAIL <error> | SKIPPED (<why>) | NOT REACHED
Tests       PASS core <n>/<n> · shell <n>/<n> | FAIL <tests> | NOT REACHED
Review      PASS 0🔴 <n>🟡 <n>🔵 | FAIL <n>🔴 | STOPPED (did not run) | NOT REACHED
Cold read   PASS | FAIL <finding> | STOPPED (did not run) | NOT REACHED
Push        PASS | NOT REACHED
CI          PASS run <id> | FAIL <why> | NOT REACHED
PR          PASS <url> | DECLINED | NOT REACHED
Decisions   <one line each, or none>
Next        <the one thing the user does now>
```

`BLOCKED` ends with the question and its options under `Next`. `STOPPED` ends with where the work
is (branch, last green commit, uncommitted edits) and how to pick it up.
