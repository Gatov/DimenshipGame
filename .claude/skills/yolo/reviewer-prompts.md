# YOLO subagent prompts

Fill the `<…>` slots and send the prompt as written. Every prompt tells the agent to change nothing.

## Severity scale (both reviewers)

- 🔴 **Critical** — sends the run back to implementation. Wrong behaviour; the change does not do
  what the task asks; a rule CLAUDE.md enforces is broken (`Dimenship.Core` naming Godot or
  `Dimenship.Shell`; `float` / `double` in Core; a state type holding a content record; content,
  executor or item order changed; the save contract or determinism broken; a `RngDomain` inserted
  mid-list; a colour literal outside `ShellPalette`; a persisted panel id renamed; the NUnit pin or
  `net8.0` target moved); material or data loss; a test that can no longer fail; a comment or name
  that says the opposite of what the code does.
- 🟡 **Major** — correct but costly: a missing test for new behaviour, an edge case unhandled, a
  public type without the house-style *why* comment, a name that fights the GDD vocabulary.
- 🔵 **Minor** — polish.

## Code review (opus)

```
You are reviewing a finished branch in <worktree or repo path>. Change nothing: no edits, no git
writes, no GitHub writes.

1. Read CLAUDE.md.
2. Read `git diff origin/master...HEAD` and `git log --format='%h %s%n%n%b' origin/master..HEAD`.
3. Read each changed file in full, and whatever it calls that you need to judge correctness.

The task was: <task text, or issue number and title>.

Report every finding as one line:
<emoji> <file>:<line> — <problem>. <what would fix it>.
using this scale:
<paste the severity scale above>

Last line, exactly: CRITICAL: <count of 🔴>
If you find nothing, write "no findings" and then the CRITICAL line.
```

## Cold read (opus)

Give this reader no task context — the point is to learn what the change says on its own.

```
You are reading a change with no background on why it was made. Change nothing: no edits, no git
writes, no GitHub writes.

In <worktree or repo path>, read CLAUDE.md, then `git diff origin/master...HEAD`, the commit
messages (`git log --format='%s%n%n%b' origin/master..HEAD`) and each changed file in full.

Answer:
1. In at most three sentences: what does this change do, and why?
2. Where did you have to guess — a name, a comment, a number or a test whose intent you could not
   read off the page?
3. Where does it break the house style: doc comments that restate the signature instead of saying
   why and naming the failure they prevent; test names that don't read as sentences; vocabulary
   that disagrees with the GDD?

Report 2 and 3 as one line each:
<emoji> <file>:<line> — <problem>. <what would fix it>.
using this scale:
<paste the severity scale above>

Last line, exactly: CRITICAL: <count of 🔴>
```

Compare the reader's answer 1 with the task. If it contradicts the task, that is a 🔴 of its own:
the change does not say what it does.

## Log parse (haiku)

```
Read <log file path>. It is the output of `<command>`. Change nothing.
List each failure as one line:
<test name or error code> | <file>:<line> | <first line of the message>
If there are more than 20, list the first 20 and then "+<n> more".
If the log shows no failure, reply "no failures" and quote its summary line.
```

## Root cause (sonnet)

```
In <worktree or repo path>, step <i> of a change made this fail:
<parsed failure line(s)>
The step's diff is `git diff <last green commit>` (uncommitted) — read it. The full log is at
<log file path>. Change nothing.

Find the root cause, not a patch. Read the code under test and the test itself. Answer:
1. The cause, with file:line evidence.
2. Why the step's change triggered it.
3. The smallest change that fixes the cause, and what it must not break.
If the evidence does not settle the cause, say so and say what would.
```

## Pass on rerun (sonnet)

```
<test> in <suite> failed once and passed when rerun alone. Change nothing. Read the test and the
code it exercises. Answer: can its outcome depend on test order, shared static state, the file
system, time, or anything outside the inputs it builds? Name the dependency with file:line, or say
there is none you can find.
```
