<!--
Data-only contributions: delete everything below except "What changed" and the data checklist.
Feature work: fill in the whole thing. An unfilled checklist means the PR is not ready.
-->

## What changed

<!-- One paragraph. What this does and why. -->

Closes #

## Type

- [ ] Feature (`feat/fNN-slug`)
- [ ] Fix
- [ ] Data
- [ ] Docs
- [ ] CI or tooling

---

## For feature work

**Feature id:** F__

### Definition of Done

Copy the checklist for this feature from
[docs/planning/FEATURES.md](../docs/planning/FEATURES.md) and tick each item.

- [ ]
- [ ]
- [ ]

### Test round

<!--
Real evidence, not a claim that it should work. For anything touching the phone, that means
results from the phone: what you scanned, what happened, how many times it failed.
-->

**How it was tested:**

**Results:**

### Left out on purpose

<!-- Anything in scope that is not in this PR, and why. Write "nothing" if nothing. -->

---

## Checks

- [ ] CI is green.
- [ ] Docs updated, including `DECISIONS.md` if a decision changed.
- [ ] No new dependency, or the reason for it is explained above.
- [ ] No credential, token or key anywhere in the change.
- [ ] No platform-specific code outside a platform abstraction.
- [ ] No medical claim or absolute health statement in any user-facing text.

## For data changes

- [ ] Every value comes from the packet or a cited source. Nothing is guessed.
- [ ] Unknown values are recorded as unknown, not as zero or blank.
- [ ] The nutrition basis is stated and no basis is mixed.
- [ ] Raw ingredient text is preserved exactly.
- [ ] The source is approved in [DATA_SOURCES.md](../DATA_SOURCES.md).
- [ ] The validator passes locally.
