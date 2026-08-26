# Governance

How decisions get made, who makes them, and how the project survives without depending on any
one person.

---

## The problem this document exists to prevent

A community food database dies when one person becomes the bottleneck for approving every
record. The design answer is that **automation is the primary gate, not a human**. People
handle ambiguity. Machines handle volume.

If the original author disappears tomorrow, the repository, the data, the build pipeline and
the release process must all still work. That is the test every governance decision is measured
against.

## Roles

| Role | Who | Can do |
|---|---|---|
| Contributor | Anyone | Open issues, submit product requests, open pull requests |
| Data reviewer | Trusted contributors | Approve data pull requests |
| Maintainer | Appointed by maintainers | Approve code pull requests, merge, release, change decisions |
| Lead maintainer | Currently Jatin Pasrija | Break ties, hold the signing keys, administer the repository |

There is no application form. Reviewers and maintainers are invited based on a track record of
good contributions. Anyone who stops participating for a long period is moved out of the role,
without prejudice, and can return.

## What needs a human

Automation merges high-confidence data changes on its own. A person is required for:

- Sources that disagree, where a choice has to be made.
- Uncertain product identity, or a barcode that looks reused.
- Incomplete label evidence.
- A large unexplained change to an existing record.
- Anything touching safety, recalls or health claims.
- Any question about a source licence.
- All code changes.
- Any change to `DECISIONS.md`, `DATA_POLICY.md`, `DATA_SOURCES.md` or this file.

## How decisions are made

1. Ordinary changes: open a pull request. One maintainer approval and green CI is enough.
2. Architectural changes: a pull request against
   [docs/planning/DECISIONS.md](docs/planning/DECISIONS.md) stating the decision, the reasoning
   and the consequence. Discussed in the open, decided by maintainer consensus. Silence for
   seven days counts as consent.
3. Disagreement: discussed in the issue or pull request. If consensus fails, the lead
   maintainer decides and records the reasoning in `DECISIONS.md`.

Nothing is decided in a private channel. If a discussion happened elsewhere, its outcome and
reasoning get written into the repository before it takes effect.

## Principles that are not up for negotiation

These are the identity of the project. Changing one means forking, not persuading.

1. No application server and no hosted database are required to run the core product.
2. No mandatory account. No subscription. No marketplace.
3. The app never carries a repository write credential.
4. Published data is evidence-backed. Unknown is a valid answer. A guess is not.
5. The app makes comparative statements, never medical ones.
6. The data stays openly licensed and the project stays forkable.

## Forking

Forking is expected, not a failure. Another country, another language, another community.

Everything needed is in the repository: the schema, the rules, the importer, the builder, the
release workflows and the app. Replace the country data and rules, generate your own signing
keys, build your own catalogue, ship your own app. You need nothing from this project and no
permission from anyone.

If a fork does something better, we would rather adopt it than compete with it.

## Contributor recognition

GitHub already records who did what, and that is the primary record. In-app contributor
statistics may be added later, but they are not a requirement and will never gamify data
submission, because volume incentives produce bad data.

## Funding

The project is designed to cost nothing to run. GitHub provides the repository, the automation
and the distribution. There is no treasury, no donations infrastructure and no plan for either.
If that ever changes, it will be proposed here first.

## Continuity

- The signing keys are the single point of failure. The rotation procedure is in
  [SECURITY.md](SECURITY.md) and must be rehearsed before the first public release.
- Repository administration must be held by more than one person before the first public
  release.
- The catalogue format is documented so that a third party can rebuild a client from scratch
  without any cooperation from this project.
