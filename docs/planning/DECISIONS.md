# Architecture Decision Record

Every decision below is locked for Phase 1. Changing one needs a PR that edits this file
and states what broke. Format: decision, rationale, consequence.

---

### ADR-0001 - Identity

**Decision.** App name `Prana`. Repository name `prana`. .NET namespace root `Prana`.
Android application id `com.prana.app`. iOS bundle id `com.prana.app`.
Hosted on a personal GitHub account.

**Consequence.** All package ids and deep links are fixed from F01. Renaming later breaks
installed apps, so it must happen before the first public APK.

---

### ADR-0002 - Code licence: Apache-2.0

**Decision.** All source code, workflows and tooling are Apache-2.0.

**Rationale.** Permissive like MIT but adds an explicit patent grant and a NOTICE mechanism,
which matters for a project that may be forked by other countries and companies.

**Consequence.** No per-file licence header is required, but `NOTICE` must list third-party
attributions. Contributions are inbound equals outbound under Apache-2.0 section 5, so no
separate CLA is required.

---

### ADR-0003 - Data licence: ODbL 1.0

**Decision.** The product database (`data/`, the built catalogue, and every release package)
is licensed **ODbL 1.0**. Individual content such as prose explanations is CC-BY-SA 4.0.

**Rationale.** Open Food Facts is ODbL and is our primary bulk data source. ODbL is
share-alike for databases, so any derived database must also be ODbL. Choosing ODbL now keeps
the door open to import OFF. Choosing CC0 would close it permanently.

**Consequence.** Two licences in one repo: Apache-2.0 for `app/`, `tools/`, `.github/`;
ODbL for `data/` and `catalogue/`. The app must display attribution. Anyone redistributing a
modified catalogue must publish their changes. Documented in `LICENSE`, `LICENSE-DATA`
and `DATA_SOURCES.md`.

**Amended in F04, 2026-08-27.** The original decision named only ODbL. Reading the Open Food
Facts terms during the F04 licence review showed they publish under ODbL 1.0 for the database
**and DbCL 1.0 for the contents**, which are different things: ODbL governs the database as a
structure, DbCL governs the individual facts inside it. A share-alike source obliges us to pass
the same pair on, so the database is now ODbL 1.0 plus DbCL 1.0. `LICENSE-DATA-CONTENTS` added.

---

### ADR-0004 - Android first, both platforms built

**Decision.** Every project targets `net10.0-android` and `net10.0-ios` from F07 onward.
Only Android is distributed in Phase 1. iOS must compile in CI but is not released.

**Rationale.** iOS distribution needs a Mac and a paid Apple account. Building for iOS from
day one prevents platform-specific code from leaking into shared layers.

**Consequence.** No Android-only API may be called outside a platform abstraction.
CI runs an iOS compile check on a macOS runner.

---

### ADR-0005 - Distribution: GitHub Releases APK only

**Decision.** Phase 1 ships a signed APK attached to a GitHub Release. No Play Store,
no F-Droid in Phase 1.

**Consequence.** The app needs its own in-app update check for the APK itself, separate from
the catalogue update check. Users must enable install-from-unknown-sources. F-Droid remains
possible later because of ADR-0006.

---

### ADR-0006 - Barcode scanning: open source engine first

**Decision.** Phase 1 uses a fully open-source barcode decoder. Scanning sits behind an
`IBarcodeScanner` abstraction so a Google ML Kit implementation can be added later as a
separate build flavour without touching feature code.

**Rationale.** Keeps the whole app free software, keeps an F-Droid release possible, and
avoids a Google dependency in the core path.

**Consequence.** Decode accuracy on worn or curved packaging may be lower than ML Kit.
F09 must include a real-world scan test on physical Indian products and must measure the
failure rate before we accept it.

---

### ADR-0007 - Two separate SQLite databases

**Decision.** `catalogue.db` is read-only, fully replaceable and owned by sync.
`user.db` holds grocery list, pending requests, history and settings, and is never touched
by sync.

**Rationale.** Atomic catalogue replacement is only safe if no user data lives inside the
file being replaced.

**Consequence.** No foreign keys across the two files. References from `user.db` to catalogue
rows are stored as barcodes, not row ids, and must tolerate a product disappearing from a
future catalogue.

---

### ADR-0008 - Data access: Microsoft.Data.Sqlite

**Decision.** `Microsoft.Data.Sqlite` for both databases. FTS5 for catalogue text search.
Hand-written mapping in repository classes. No heavyweight ORM.

**Rationale.** Full control over connection lifetime, read-only open modes, `PRAGMA`
integrity checks, and attaching a downloaded file for verification before activation, all of
which F11 needs.

**Consequence.** More mapping code than `sqlite-net-pcl`. The bundled native SQLite must be
verified to include FTS5 on Android and iOS during F08.

---

### ADR-0009 - Pipeline tools in C#

**Decision.** Validator, catalogue builder and importer are .NET console apps in `tools/`.
Python is permitted only for the research agent (F14) if a task genuinely needs it.

**Rationale.** One language for contributors, shared model types between tools and app, and
simple CI.

**Consequence.** Tools and app share a `Prana.Core` model library so the schema cannot drift
between them.

---

### ADR-0010 - Tiered contribution intake

**Decision.** Contribution submission sits behind an `IContributionChannel` abstraction with
tiers, so a non-technical user is never forced through GitHub.

- Tier 1 (F13, required): prefilled GitHub Issue opened in the phone browser. Zero secrets in
  the app, submitted under the account of the person contributing.
- Tier 2 (F13, required): local pending-request queue with export and share, so someone with
  no GitHub account can send a request through any channel they already use.
- Tier 3 (evaluated in F13, may land later): an optional stateless relay that holds a
  fine-grained token and opens the Issue on behalf of the app, so submission is one tap and
  needs no GitHub account. It stores nothing and is optional. A fork can run its own or none.

**Rationale.** The plan forbids repository credentials in the app, which Tier 1 and Tier 2
honour absolutely. But requiring a GitHub account would exclude most real users, which kills
the demand-driven growth model. Tier 3 is the pragmatic answer and stays optional so the
no-server principle still holds for anyone who forks.

**Consequence.** F13 must define the Tier 3 abuse controls (rate limit, payload size cap, no
free-text passthrough into privileged context) before any relay is written. The app must work
fully with Tier 3 disabled.

---

### ADR-0011 - Signed catalogue releases from day one

**Decision.** Every catalogue release carries a SHA-256 and a detached signature. The signing
private key lives only in GitHub Actions secrets. The public key is compiled into the app.
An unsigned or badly signed package is never activated.

**Consequence.** Key generation, storage and rotation are part of F06 and must be written into
`SECURITY.md` before the first release. Losing the key means every installed app must be
updated, so a documented rotation path is mandatory, not optional.

---

### ADR-0012 - Deterministic research only

**Decision.** The research agent uses deterministic parsers and documented source APIs.
No LLM in the Phase 1 pipeline.

**Rationale.** Every published field must be traceable to evidence. Determinism also makes CI
reproducible and free.

**Consequence.** Coverage will be lower than an LLM-assisted extractor. Anything the
deterministic path cannot resolve becomes a review item, not a guess.

---

### ADR-0013 - Bulk data acquisition, not hand curation

**Decision.** The catalogue is built by bulk import from openly licensed datasets, with a
small hand-verified golden set kept alongside for regression testing.

**Rationale.** The goal is full coverage of Indian packaged food, which hand curation cannot
reach.

**Consequence.** F04 grows into a real feature: importer, licence verification per source,
deduplication, and a quality gate that keeps low-confidence records out of the verified
catalogue. Repo size and Git performance must be managed (see ADR-0014).

---

### ADR-0014 - One file per product, sharded by barcode

**Decision.** `data/products/<first-3-digits>/<barcode>.json`. No aggregate files.

**Rationale.** A contribution touches exactly one file, so PR review and merge conflicts stay
trivial at any scale.

**Consequence.** Tens of thousands of small files. The validator and builder must stream and
never load all records into memory. Git operation timings must be measured during F04.

**Measured in F04, 2026-08-27.** A tree of 10,001 product records, 43 MB of JSON on disk:

| Operation | Time |
|---|---|
| `git add -A` (cold, first time) | 43.0 s |
| `git commit` | 8.2 s |
| `git status` | 0.15 s |
| `git checkout -b` | 0.18 s |
| resulting `.git` | **2 MB** |

The headline is the last row. 43 MB of working tree compresses to 2 MB, because records share
almost all of their structure and git packs them well. Extrapolating to the roughly 22,700
Indian products in the source, the repository grows by single-digit megabytes and everyday
operations stay in the hundreds of milliseconds. The one-file-per-product decision is safe at
this scale, and the size fear behind it does not materialise. Bulk `add` and `commit` are slow,
but they happen once per import, in CI.

---

### ADR-0015 - Process

**Decision.** `main` is protected. One feature per branch (`feat/fNN-slug`), one PR, squash
merge. Conventional Commits. GitHub Milestones map to M0 through M6. A GitHub Project board
tracks all features. CI must pass to merge. See [WORKFLOW.md](WORKFLOW.md).

---

### ADR-0016 - Build order: data spine before app

**Decision.** F01 to F06 (schema, validator, data, builder, release) are completed before the
app is scaffolded in F07.

**Rationale.** The app is a client of the catalogue format. Building the client first would
mean guessing the format and reworking the data layer.

**Consequence.** No visible app until M2. Progress in M0 and M1 is proven by CI and by a real
downloadable catalogue release, not by screenshots.

---

### ADR-0017 - Canonical key is a zero-padded GTIN-14

**Decision.** Every product is keyed by its barcode padded to 14 digits. The digits as printed
are kept separately in `barcode_printed`. The check digit is verified before a barcode becomes
a key. Products with no barcode are out of scope for Phase 1.

**Rationale.** EAN-13, UPC-A, EAN-8 and GTIN-14 are one numbering scheme at different widths.
A UPC-A code is the EAN-13 of the same product with the leading zero dropped. Keying on the
printed digits would store one product under two keys, and no later deduplication would
reliably merge them.

**Consequence.** One implementation of this lives in `Prana.Core.Barcodes.Gtin` and is shared
by the scanner, validator, importer and builder. The directory shard is the first three
significant digits, taken after the padding, so records do not all pile into a `000` directory.

---

### ADR-0018 - Provenance by path coverage

**Decision.** Sources are declared once per record. A provenance map points paths in the record
at those sources, and a path covers everything beneath it. The validator rejects any published
value that no path covers.

**Rationale.** Per-field evidence objects cannot desync but roughly triple record size and make
contributions painful. Flat values with no link to evidence allow a value with no evidence at
all. Coverage gives the honest middle: one entry for `nutrition` genuinely reflects that the
whole panel came from one photograph, while a disputed field can still be pinned to its own
source by naming a more specific path.

**Consequence.** The coverage rule is only as good as its enforcement, so the F03 validator must
implement prefix matching before any bulk import runs. Without it this decision degrades into
the flat option it was chosen over.

---

### ADR-0019 - Unknown has three states, none of them null

**Decision.** A value is either declared, listed in `not_declared`, or absent. `null` is never
written and `0` is never used to mean unknown.

**Rationale.** "The packet does not state this" and "nobody has checked yet" are different
facts. Collapsing them makes the app show a blank where it should show Unknown, and makes the
research automation re-research the same gap forever.

**Consequence.** JSON serialisation omits null properties, which the round-trip tests enforce.
Every UI surface must render an absent value as Unknown rather than as empty or zero.

---

### ADR-0020 - Nutrition is an array of bases

**Decision.** `nutrition` is a list of blocks, each carrying its own `basis`. Blocks are never
merged and values are never converted between bases. A `per_serving` block requires a serving.

**Rationale.** Indian packets commonly print per 100 g and per serving side by side, and those
measure different things. A single value set would force a conversion that the label often does
not support.

**Consequence.** Every comparison feature, including the alternatives engine, must select a
comparable basis rather than assuming one, and must handle a product that only declares
per-serving values.

---

### ADR-0021 - Shared libraries live in src/

**Decision.** `Prana.Core` and any future shared library live in `src/`, alongside `app/` and
`tools/`.

**Rationale.** The model is shared by the app and the pipeline tools, so it belongs in neither.
The layout in the original project plan has no home for it.

**Consequence.** A small documented deviation from the plan layout. `README.md` records it.

---

### ADR-0022 - Imported records are never marked verified

**Decision.** Every record from a bulk source is written as `unverified` with `medium`
confidence, regardless of how complete it looks. Only evidence from packaging, a manufacturer,
or a regulator can support a higher status.

**Rationale.** Open Food Facts is community-entered. One community database agreeing with itself
is not corroboration. Marking imported data verified would make the word meaningless everywhere
else in the system, including in the automated merge policy that depends on it.

**Consequence.** Essentially the entire catalogue is unverified at launch, so the app has to
present unverified data as normal and useful rather than as a warning. Getting that tone right
is now a requirement on F10, not a nicety. Verified records will only ever come from people
checking real packets.

---

### ADR-0023 - The importer refuses to convert between nutrition bases

**Decision.** The importer reads what the label declared. Open Food Facts publishes normalised
per-100g columns for every product, including ones whose label only stated a per-serving panel;
those columns are not used unless the label basis was per 100 g. A per-serving panel with no
serving described produces no nutrition at all.

**Rationale.** Importing the convenient normalised column would launder exactly the silent
conversion `DATA_POLICY.md` forbids, and would do it across the whole catalogue at once, where
nobody would ever see it.

**Consequence.** Fewer products carry nutrition than the source appears to offer. That is the
correct trade: a per-serving figure presented as per 100 g is worse than no figure.

---

### ADR-0024 - Imports run in GitHub Actions, not on a maintainer machine

**Decision.** `import-openfoodfacts.yml` downloads the export, runs the importer, validates the
result and opens a pull request. Nothing merges automatically.

**Rationale.** The export is over a gigabyte. An import that depends on one person having
downloaded it is not reproducible by a fork, which contradicts the forkability principle. A
runner has fast access to the source and no state of its own.

**Consequence.** A pull request opened with `GITHUB_TOKEN` does not trigger other workflows, so
`validate-data.yml` will not run on it. The import workflow therefore validates the content
itself before opening the pull request, and says so in the body.

---

### ADR-0025 - The licence review is enforced in code

**Decision.** A source with no approved row in `DATA_SOURCES.md` has no adapter, and
`prana-import` rejects any `--source` it does not recognise. `ISourceAdapter` requires every
adapter to state its licence and attribution, which are then stamped onto every record it
produces.

**Rationale.** A policy that lives only in a document is a policy someone will route around
under deadline. Making the licence a required property of the adapter means an unreviewed source
cannot even be expressed in code.

**Consequence.** Adding a source is deliberately two steps: the review, then the adapter. That
is friction, and it is the point.

