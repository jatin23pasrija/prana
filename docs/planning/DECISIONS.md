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
