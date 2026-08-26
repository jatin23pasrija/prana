# Development Workflow

How this project is run on GitHub. This is binding for every feature.

---

## The per-feature loop

Every one of the 17 features goes through the same six steps. We do not start a feature
before the previous one is merged.

```
1. QUESTIONS   Discussion round. Open questions answered, scope agreed,
               unknowns turned into decisions. Recorded in the feature issue.
                     |
2. PLAN        Task breakdown posted as a checklist on the feature issue.
               Definition of Done confirmed.
                     |
3. BUILD       Branch feat/fNN-slug. Small commits, Conventional Commits.
               Draft PR opened early so CI runs from the first push.
                     |
4. TEST        The feature test round from FEATURES.md, executed for real.
               Results pasted into the PR. Device tests need the physical phone.
                     |
5. REVIEW      PR checklist complete, CI green, DoD ticked.
                     |
6. MERGE       Squash merge into main. Issue closed. Board column moved.
               Next feature starts.
```

Nothing skips step 1 and nothing skips step 4.

---

## Branches

- `main` is protected. No direct pushes. Linear history via squash merge.
- Feature branches: `feat/fNN-slug`, for example `feat/f03-validator`.
- Fixes on an unreleased feature: `fix/fNN-slug`.
- Data-only contributions: `data/<barcode>` or `data/<short-description>`.
- Automation branches: `bot/request-<issue-number>`, created by Actions.

## Commits

Conventional Commits. Type list: `feat`, `fix`, `data`, `docs`, `chore`, `ci`, `test`,
`refactor`, `perf`. Scope is the feature id where it applies.

```
feat(f03): add GTIN check-digit validation
data: add 8901234567890 Example Biscuit
ci(f06): sign catalogue package before release
```

## Pull requests

One PR per feature. The PR body uses the template and must contain:

- The feature id and a link to its issue.
- What changed and why.
- The completed Definition of Done checklist.
- Test round evidence. For device features, that means real results from the phone,
  not a claim that it works.
- Anything deliberately left out, and why.

Merge requires: CI green, DoD complete, test evidence present.

## Issues

Five templates:

| Template | Used by | Feeds |
|---|---|---|
| Product request | App users and contributors | F14 research automation |
| Product correction | Anyone spotting wrong data | Data PR |
| Bug report | Users and developers | Fix branch |
| Feature proposal | Community | Backlog triage |
| Data source proposal | Contributors | `DATA_SOURCES.md` review |

Product request is the important one. Its body is machine-readable because the research
workflow parses it. Changing that template means updating the parser in the same PR.

## Labels

- Type: `feature`, `bug`, `data`, `docs`, `automation`, `security`.
- Area: `app`, `catalogue`, `pipeline`, `ci`, `schema`.
- State: `needs-discussion`, `ready`, `in-progress`, `blocked`, `needs-review`,
  `needs-human-review`, `good-first-issue`.
- Phase: `phase-1` through `phase-4`.
- Confidence, used by automation: `confidence-high`, `confidence-medium`, `confidence-low`.

## Milestones

`M0 Foundation`, `M1 Data spine`, `M2 Offline app`, `M3 Sync`,
`M4 Discovery and contribution`, `M5 Everyday utility`, `M6 Public release`.
Every feature issue belongs to exactly one.

## Project board

Single board, columns: `Backlog`, `Questions`, `Ready`, `In progress`, `In review`,
`Testing`, `Done`. Exactly one feature is allowed in `In progress` at a time. That rule is
what makes this a single-feature-at-a-time project rather than a wish list.

## Continuous integration

| Workflow | Trigger | Purpose |
|---|---|---|
| `ci.yml` | PR, push to main | Build solution, run unit tests, format check |
| `validate-data.yml` | any `data/**` change | Schema and semantic validation, PR annotations |
| `build-app.yml` | PR touching `app/**` | Android build, iOS compile check |
| `research-product.yml` | product request issue | Automated research, opens a data PR |
| `build-catalogue.yml` | merge to main touching `data/**`, plus schedule | Build SQLite package |
| `release-catalogue.yml` | successful catalogue build | Sign and publish a GitHub Release |
| `data-health.yml` | schedule | Broken sources, duplicates, stale records |
| `request-priority.yml` | schedule | Rank most-requested missing products |
| `release-app.yml` | tag `v*` | Signed APK release |

Required for merge into `main`: `ci.yml`, and `validate-data.yml` when data changed.

## Automation permissions

Least privilege on every workflow. The default `GITHUB_TOKEN` is read-only, and each job
declares only the permissions it needs. Any workflow that processes issue text treats that
text as untrusted input and never interpolates it into a shell command.

## Releases

Two independent release trains.

- **Catalogue releases** are frequent, tagged `catalogue-vNNN`, and carry the package,
  manifest, checksum and signature.
- **App releases** are infrequent, tagged `vX.Y.Z`, and carry the signed APK.

`minimumAppVersion` in the manifest is the contract between them. An app older than that
must refuse the catalogue and tell the user to update the app.

## Definition of Ready

A feature may enter `In progress` only when: questions round complete, scope written,
DoD agreed, dependencies merged, and the test method is known and achievable.

## Definition of Done

Code merged, CI green, tests written where the feature is testable, docs updated, test round
executed with evidence, DoD checklist ticked, issue closed, board updated.
