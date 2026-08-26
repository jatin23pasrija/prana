<div align="center">

# Prana

**Know what is in your food. Offline, open source, community maintained. India first.**

[![CI](https://github.com/jatin23pasrija/prana/actions/workflows/ci.yml/badge.svg)](https://github.com/jatin23pasrija/prana/actions/workflows/ci.yml)
[![Code licence: Apache 2.0](https://img.shields.io/badge/code-Apache--2.0-blue.svg)](LICENSE)
[![Data licence: ODbL 1.0](https://img.shields.io/badge/data-ODbL--1.0-green.svg)](LICENSE-DATA)
[![Phase](https://img.shields.io/badge/phase-1%20in%20progress-orange.svg)](docs/planning/ROADMAP.md)

</div>

---

Prana is a free Android and iOS app that tells you what is actually inside a packaged food
product. Scan the barcode, see the sugar, fibre, protein, sodium, saturated fat, the full
ingredient list, whether it contains palm-derived oil, and what a better substitute might be.

It works **with no internet**. It needs **no account**. There is **no server** behind it and
**no company** running it. The product data lives in this repository, in the open, and anyone
can read it, correct it, or take a copy and build something else with it.

> **Status:** Phase 1 is under construction. There is no installable release yet.
> Follow [the roadmap](docs/planning/ROADMAP.md) to see where we are.

---

## Why this exists

Packaged food labels in India are hard to read, hard to compare, and often only make sense
after you have already bought the product. The apps that do help usually want an account, a
subscription, a network connection, or all three, and the data they hold is theirs, not yours.

Prana takes the opposite position:

- **Offline first.** After the catalogue is installed, scanning and analysis never need the
  internet. Internet only makes the data fresher.
- **No server, ever.** GitHub is the whole backend. Data, rules, review, automation and
  distribution all happen here. Nobody has to pay for hosting to keep this alive.
- **Evidence, not opinion.** Every value can be traced to a source with a retrieval date and a
  confidence level. Where the label does not say, the app says **Unknown**. It never guesses a
  number to fill a gap.
- **Comparative, not medical.** Prana will tell you this biscuit has more sugar per 100 g than
  that one. It will never tell you a food is healthy, unhealthy, or good for your condition.
- **Forkable.** Another country, another community, another language. Swap the data and rules,
  build your own catalogue, ship your own app. You need nothing from us.

## How it works

```
                        GitHub  (source of truth)
                    data - rules - requests - code
                                |
                         GitHub Actions
                                |
              +-----------------+-----------------+
              |                 |                 |
          Research          Validate            Build
              |                 |                 |
              +-----------------+-----------------+
                                |
                        GitHub Release
                 signed catalogue + manifest
                                |
                              HTTPS
                                |
                          Prana on your phone
                          local SQLite catalogue
                                |
                        +-------+-------+
                        |               |
                   internet on     internet off
                        |               |
                  fresher data    everything still works
```

Product records are small human-readable JSON files, one per barcode, so a correction touches
exactly one file and is easy to review. A build step turns thousands of those files into a
single compressed SQLite database, signs it, and attaches it to a GitHub Release. Your phone
downloads it in the background, verifies the signature, and swaps it in atomically. If anything
goes wrong at any point, the catalogue you already had keeps working.

## The loop that makes it grow

The interesting part is what happens when you scan something we do not have.

```
scan barcode
     |
local lookup ---- found ----> show it, instantly, offline
     |
   missing
     |
online discovery from approved open sources
     |
show it, clearly marked as temporary and unverified
     |
you confirm it matches the packet
     |
a product request goes to GitHub
     |
automation researches it independently, validates it, opens a pull request
     |
high confidence and all checks pass ---> merged
ambiguous ---------------------------> a human reviews it
     |
catalogue rebuilds and is released
     |
your next sync, and everyone else, now has it offline
```

The app never carries a GitHub token. It cannot write to this repository, by design.

## Repository layout

| Path | What lives there |
|---|---|
| `app/` | The .NET MAUI application for Android and iOS |
| `data/` | Product, ingredient, brand, category and country records, one file each |
| `rules/` | Versioned, explainable rules for indicators and dietary logic |
| `schema/` | JSON Schema definitions that every record must satisfy |
| `tools/` | Validator, importer, catalogue builder and research agent |
| `requests/` | Incoming product requests, in flight and processed |
| `sources/` | Source adapters and per-source licensing decisions |
| `catalogue/` | Catalogue build configuration and output |
| `docs/` | Specifications, policies and the project plan |
| `.github/` | Workflows, issue templates, automation |

## Documentation

| Document | Purpose |
|---|---|
| [ROADMAP.md](docs/planning/ROADMAP.md) | Phases, milestones, what is in Phase 1 and what is not |
| [FEATURES.md](docs/planning/FEATURES.md) | All 17 Phase 1 features with their Definition of Done |
| [WORKFLOW.md](docs/planning/WORKFLOW.md) | How development runs: branches, PRs, CI, releases |
| [DECISIONS.md](docs/planning/DECISIONS.md) | Every locked architectural decision and why |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute code or data |
| [DATA_POLICY.md](DATA_POLICY.md) | Data quality, provenance, privacy and corrections |
| [DATA_SOURCES.md](DATA_SOURCES.md) | Every data source, its licence and our usage decision |
| [GOVERNANCE.md](GOVERNANCE.md) | Who decides what, and how you become a maintainer |
| [SECURITY.md](SECURITY.md) | Reporting a vulnerability, and how releases are signed |

Specifications arriving with their features: `PRODUCT_SCHEMA.md` (F02),
`CATALOGUE_FORMAT.md` (F05), `RESEARCH_PIPELINE.md` (F14).

## Install the app

Not yet available. Phase 1 ships a signed APK on the
[Releases](https://github.com/jatin23pasrija/prana/releases) page. There will be no Play Store
listing at first, so you will need to allow installing from an unknown source.

iOS is built and tested but is not distributed in Phase 1.

## Build from source

You need the .NET 10 SDK and the MAUI workloads.

```bash
git clone https://github.com/jatin23pasrija/prana.git
cd prana
dotnet build
```

Full instructions, including running the validator and building a catalogue locally, are in
[CONTRIBUTING.md](CONTRIBUTING.md).

## Contribute

You do not need to be a programmer, and for the most useful kind of contribution you do not
even need to leave the app.

- **Missing product?** Scan it in the app and confirm what discovery found. That is the whole
  contribution. Or open a
  [product request](https://github.com/jatin23pasrija/prana/issues/new/choose) here.
- **Wrong number?** Open a product correction with a photo of the packet or a link to the
  manufacturer page.
- **Know a good open data source?** Open a data source proposal. Every source gets a licence
  review before a single record is imported.
- **Write code?** Pick an issue from the
  [project board](https://github.com/jatin23pasrija/prana/projects). Features are built one at
  a time, in order, each on its own branch.

Read [CONTRIBUTING.md](CONTRIBUTING.md) first, and
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) always applies.

## Use the data without the app

The catalogue is a plain SQLite file with a documented schema, attached to every catalogue
release along with a manifest, a SHA-256 and a signature. Build a web client, a shopping
assistant, an accessibility tool, a research dataset. That is the point.

The raw records in `data/` are also yours to use directly. See
[DATA_SOURCES.md](DATA_SOURCES.md) for attribution requirements.

## Licence

This repository carries two licences, because code and data are different things.

| What | Licence |
|---|---|
| Code, tooling, workflows, documentation prose | [Apache License 2.0](LICENSE) |
| Product database, `data/`, and every built catalogue | [ODbL 1.0](LICENSE-DATA) |

The database is ODbL because it is a share-alike licence for databases, which keeps it
compatible with the open datasets we build on and guarantees that improvements made to any
copy of it stay open. If you redistribute a modified catalogue, publish your changes.

Attributions for third-party data are recorded in [NOTICE](NOTICE) and
[DATA_SOURCES.md](DATA_SOURCES.md).

## What Prana is not

No marketplace. No affiliate links. No subscription. No accounts. No hosted database. No
tracking. No medical or nutritional advice. No claim that any food is good or bad for you.

---

<div align="center">
Copyright 2026 Jatin Pasrija and the Prana contributors.
</div>
