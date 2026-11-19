# Contributing to TheStrat Suite

TheStrat Suite is a single Pine Script v6 file with no compiler, no test runner, and one runtime: a TradingView chart. That shapes everything about how contributions work here. Correctness rules were learned the hard way — most of them are written down as `FIX` tags in the source and as rules in the engineering docs — and the job of this document is to keep a well-meaning PR from re-learning them.

Current build: `pine/TheStratSuite_v3.2.0.pine`. Code references throughout this repo are **function names and `FIX` tags** — grep the source for them. Never line numbers.

---

## Before you write code

Read, in this order:

1. `docs/engineering/repaint-prevention.md` — the no-repaint contract. Non-negotiable; see below.
2. `docs/engineering/htf-correctness.md` — how six timeframes are read from one chart, and the session/calendar traps.
3. `docs/DESIGN_CONSTRAINTS.md` — decisions that are **intentional**. Do not open a PR to "fix" conditional creation, the unrolled FTFC chain, the dual F2 detection paths, or anything else on that list. PRs that fix an implementation flaw *within* a constraint are welcome; PRs that fight the constraint will be closed with a pointer to the file.

The concepts docs (`docs/concepts/`) define the trading vocabulary — bar types, signals, notation. If a PR touches user-facing behavior, its description should use that vocabulary.

---

## PR flow

1. Fork, branch from `main`, keep the branch to **one concern** — one bug, one feature, one doc. The engine is one large file; a small diff is the only thing that makes review possible.
2. Edit the **current build file** in place. Do not create a new versioned file — cutting versions is a release action (see "Versioned pine files" below), and do not touch older snapshots ever.
3. Add a CHANGELOG entry under `[Unreleased]`, following the existing format: one line per change, citing its `FIX` tag if it is a bug fix.
4. Run the pre-review gates (next section) and the TradingView test workflow, and say so in the PR description: symbol, chart timeframe, monitored timeframes, and the result of the reload test.
5. Expect review to be about state and repaint first, style second. A reviewer asking "does bar history rebuild this on reload?" is not being pedantic — that question is the product.

Contributions are accepted under the repo license, MPL-2.0. New files need the MPL header block (copy it from the top of the current build).

### Pre-review gates

The engineering docs each end with a **contributor checklist**. Those checklists are the review gates for this repo — answer them before requesting review, in the PR description, not in your head:

- Anything that adds state, reads `request.security` data, or emits alerts → the checklist in `docs/engineering/repaint-prevention.md`.
- Anything that touches timeframes, sessions, calendar parsing, or the preview path → the checklist in `docs/engineering/htf-correctness.md`.

A PR that can't answer its checklist isn't ready for review yet.

---

## The no-repaint rule — zero tolerance

**What you saw live must be what you see after a reload.** No exceptions, no "minor" cases, no "it only differs for a few bars." A signal, level, or alert that reads differently after a refresh is a release blocker regardless of how useful the feature is.

The full contract — the eight rules, the failure modes, and the case studies behind them (`FIX P0-1`, `FIX P0-2`, `FIX P1-a`, `FIX GLUE-1`) — lives in `docs/engineering/repaint-prevention.md`. This document does not restate it; go read it. Two consequences worth repeating because they decide most PRs:

- **When freshness and reload-stability conflict, choose reload-stability** (the `FIX P0-2` case study in that doc records this tradeoff being taken deliberately).
- Forming-bar movement is not repaint — a live trigger updating tick by tick *is* the product. The sin is letting forming-bar data leak into anything the indicator remembers.

---

## The `FIX` tag convention

Every solved bug in this codebase leaves a grep-able marker at the code it changed:

```
// FIX GLUE-1 (2026-07-06): measure from time_close, not time (the open). CME glues holiday
// half-sessions into the next trade date (Fri Jul 3 2026 lives inside Mon Jul 6's bar), so
// ...
```

The format:

- **Tag** — `FAMILY-n`, optionally with a letter suffix for follow-ups (`GLUE-2b`) or lettered series (`P1-a` … `P1-j`). Families in the current source: severity-graded review findings (`P0`/`P1`/`P2` — the scale is defined in `docs/DESIGN_CONSTRAINTS.md`), further review-finding families from the same audit (`BTC`, `CSS`, `TAW`, `MOD`, `U2`), and named campaigns for themed bug hunts (`GLUE`, `HTF-STRADDLE`, `RECON-KEY`, `LEAD-F2-TIEBREAK`, `TFCOLOR`). A new bug either joins an existing family or starts a new short, meaningful one.
- **Inline comment, at the fix site** — the tag plus the *why*: what broke, what the rule is now. Dated (`YYYY-MM-DD`) for anything non-trivial; attribution in parentheses if the finding came from an external review. If one fix touches several code sites, the same tag appears at each of them.
- **One rule per tag.** A tag names exactly one bug and the rule that prevents its recurrence. Two rules means two tags, even if they land in the same PR. This is what lets docs, the CHANGELOG, and review comments cite `FIX P1-c` and mean one precise thing.

Tags are permanent. Never renumber, reuse, or delete one — the docs and CHANGELOG cite them, and a stale citation is worse than a crowded namespace. If a later fix supersedes an earlier one, it gets its own tag and its comment says what it replaced.

Bug-fix PRs without a tag and inline comment will be asked to add one before merge.

---

## Versioned pine files

`pine/` holds one file per released version. The newest versioned file is the current build; everything older is an **immutable archive** — never edited, not even to backport a header or fix a typo.

- Day-to-day PRs modify the current build file. Its unreleased delta is tracked in the CHANGELOG's `[Unreleased]` section.
- **A release cuts a new file** (`TheStratSuite_v<major.minor.patch>.pine` — the `-split` suffix was retired with v3.0.0; older snapshots keep their historical names and are never renamed), performed by a maintainer: copy the current build to the new name, bump the version string in the header comment and the `indicator()` title, move `[Unreleased]` into a dated CHANGELOG entry. The old file stays, untouched, as the snapshot of that release.
- **Every new version file carries the MPL header block** — the three-line Mozilla Public License notice at the top of the current build, directly under the `// TheStrat Suite - v...` title line. Older archive snapshots predate the license header and deliberately don't have one; new versions are not optional.
- Not every working state becomes a snapshot — versions that only ever existed in the TradingView editor are folded into the next release's CHANGELOG entry (see how 2.2.5 is handled there).

---

## How to test

There is no local runner. Pine Script executes only on TradingView, so testing is a paste workflow:

1. **Paste the whole file** into the TradingView Pine Editor (a fresh script slot works fine) and hit *Add to chart*. Zero compile warnings is the bar — the editor is the only compiler you get.
2. **Run the reload test.** With your change active, note the current signal/level/table state, then refresh the page. Anything that differs — a level that moved, a latch that vanished, a label that changed — is a repaint bug. This is the single most important test; run it after every meaningful change.
3. **Test on a session-stamped instrument, not just crypto.** 24/7 symbols hide every calendar and session bug this codebase has ever had. Anything touching timeframes, staleness, or preview needs a pass on CME futures (ideally around a holiday or a quarter roll — see `docs/v2.2.3_holiday_glue_fix.md` for why).
4. **Bar replay** with `Preview Mode = Off` (that setting exists precisely to make replay usable — see `docs/engineering/htf-correctness.md`).
5. For timestamp or reconstruction questions, `diagnostics/` contains standalone probe scripts (`HTF_Timestamp_Probe`, `HTF_Recon_Probe_v2`) — paste one next to the indicator instead of guessing what `request.security` serves. Several shipped fixes are marked "probe-validated" in their `FIX` comments; that is the standard for calendar-logic claims.

State what you tested in the PR. "Compiles" is not a test result.

---

## Docs contributions

Docs follow the code's referencing rule: **function names and `FIX` tags, never line numbers** — line numbers rot with every diff, tags don't. Engineering docs state which build they were written against, and they end with a contributor checklist; keep both conventions when adding one. The docs index and roadmap live in `docs/README.md`.

### Voice

Everything public-facing in this repo — docs, README, release notes, listing copy — is written in the author's voice. Hold contributions to the same rules:

- First person and direct where the author speaks: "I built this because...", never "Introducing...".
- No promotional language. If a sentence still works after deleting "powerful" or "seamless", the word was decoration — cut it.
- No exclamation points in prose. No emoji, except when quoting the indicator's own output (alert labels emit 🟢/🔴; quoting output is documentation, not decoration).
- Short, plain sentences. Technical claims over adjectives. Prefer periods and commas; keep em dashes rare.
- Release notes: lead with fixes on patches, features on majors. Short.

---

## Pre-publication scrub checklist

This repository is public, and git history is public forever. Before pushing any branch or opening any PR, scrub your diff — this mirrors the release-time scrub in `docs/RELEASE_CHECKLIST.md` and applies to every contribution, not just releases:

1. **No pricing, subscription, or business references.** The repo documents an indicator, not a product strategy. Commercial positioning, distribution platforms, and payment anything stay out — including in comments and commit messages.
2. **No absolute local paths.** Nothing matching `/Users/`, `/home/`, `C:\`. Repo-relative paths only. `grep -rn '/Users/\|/home/' <changed files>` before you push.
3. **No personal data.** No email addresses, account names, broker or platform usernames, personal trading results, or position details — yours or anyone else's. Chart screenshots count: crop or redact account info before attaching one to a PR or issue.
4. **No private URLs or credentials.** No links to private dashboards, API keys, or tokens. Example symbols and public TradingView docs links are fine.
5. Remember that **history keeps what you committed**, not just what's in the final diff. If something sensitive lands in a commit, rewriting the branch before the PR merges is cheap; after merge it isn't. Ask a maintainer immediately rather than quietly force-pushing over a merged branch.

---

## Contributor checklist

Before opening the PR:

1. One concern, one branch — and the diff touches the current build file only, never an archived snapshot.
2. Bug fix? It has a `FIX` tag, an inline why-comment at every fix site, and one rule per tag.
3. The relevant engineering-doc checklists (`docs/engineering/repaint-prevention.md`, `docs/engineering/htf-correctness.md`) are answered in the PR description.
4. Tested via TradingView paste: clean compile, reload test passed, session-stamped instrument if the change touches timeframes or sessions — and the PR says what was tested.
5. CHANGELOG `[Unreleased]` entry added, citing the tag.
6. Scrub pass done: no business references, no absolute local paths, no personal data anywhere in the diff or commit messages.
