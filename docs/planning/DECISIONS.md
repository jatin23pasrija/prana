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

---

### ADR-0026 - Incomplete records are kept, and must never suppress discovery

**Decision.** A product with a name but no nutrition and no ingredients is imported. The import
writes every such barcode to a gap queue. In return, F10 must present such a record as
incomplete, and F12 must still offer online discovery for it exactly as if the lookup had
missed.

**Rationale.** Coverage is worth having: telling someone the packet in their hand is Parle-G and
that we know nothing else beats telling them it does not exist. The first import dropped 15,415
Indian products on this rule alone.

The danger is subtle and would have been easy to miss. A bare record makes the local lookup
**succeed**, so without this decision the app would stop offering discovery and contribution for
exactly the products that need them most. Coverage bought that way would quietly strangle the
mechanism the catalogue grows by.

**Consequence.** F10 and F12 carry a hard requirement, not a preference. The condition is
computable with no schema change: no `nutrition` and no `ingredients_raw` means incomplete. The
gap queue gives F14 a work list of tens of thousands of real targets on day one instead of
waiting for a user to scan one.

---

### ADR-0027 - Enrichment during import is not possible, and is not faked

**Decision.** The importer does not attempt online lookup for records it cannot complete. It
records the gap and moves on.

**Rationale.** This was requested and investigated rather than dismissed. It cannot work today,
for reasons that are worth writing down so it is not proposed again without them changing:

- The only approved product source is Open Food Facts, and the export *is* Open Food Facts.
  Re-querying their API for a product just read from their own export returns the same
  emptiness.
- Doing so would mean tens of thousands of requests against an API that already returns 503 on
  the fifth page of a search. That is the source asking to be left alone.
- Any other source needs an approved row in `DATA_SOURCES.md` and an adapter, which ADR-0025
  enforces in code. There is none.

Recovering more from the export itself was measured and also does not help: language variant
fields hold a name or ingredients in 1 record out of 864, `quantity` text never appears without
the numeric field, and no record has a name only in `generic_name`.

**Consequence.** Enrichment happens where it can actually work, which is F12 and F14, against
sources that have been through licence review. The gap queue is the bridge between this import
and that automation.

---

### ADR-0028 - Brotli, not zstd

**Decision.** Catalogue packages are Brotli compressed.

**Rationale.** The project plan named zstd. Brotli is built into .NET and into every browser, at
a comparable ratio, so no client needs a native dependency to read a catalogue. On a mobile app
that has to run on low-end Android devices, and for third-party clients we will never meet, that
is worth more than the last few percent of compression.

**Consequence.** Measured on 26,453 products: 15.9 MB of SQLite compresses to 2.5 MB, 85%
smaller. `docs/CATALOGUE_FORMAT.md` states the codec so a third party is never left guessing.

---

### ADR-0029 - Completeness is stored, not inferred

**Decision.** `product.is_complete` is a column in the catalogue, set when a record has neither
nutrition nor ingredients.

**Rationale.** ADR-0026 requires the app to treat incomplete records differently and still offer
discovery for them. A rule every caller has to remember to repeat is a rule that will eventually
be forgotten in one screen, and the symptom would be silent: discovery quietly stops being
offered for the products that most need it.

**Consequence.** One indexed question instead of a null check in every query, and third-party
clients get the same signal. `CATALOGUE_FORMAT.md` calls it out explicitly for that reason.

---

### ADR-0030 - Three app build flavours

**Decision.** The app ships in three flavours: full catalogue bundled, starter catalogue
bundled, and no catalogue at all. The catalogue builder produces the two artefacts they consume.

**Rationale.** Requested during the F05 round. It lets someone on expensive mobile data install
an app that already works, and someone on wifi install a small one.

**Consequence.** Three artefacts to build, sign, test and publish per release, so F17 grows.
The install-time behaviour differs per flavour, so F08 must handle a bundled catalogue that is
absent, small, or already complete, and F11 must not re-download a catalogue the flavour already
carries. Recorded here because the cost lands on features that have not started yet.

---

### ADR-0031 - F06 deferred, and what that must not be allowed to mean

**Decision.** The signed release pipeline is postponed. The signing keypair has to be generated
and stored by a person, and that has not happened. Work continues with F07.

**What this does not block.** F07 through F10 need nothing from F06. The original dependency was
ordering, not necessity, so it has been rewired: F07 now depends on F05.

**What this does block, hard.** F11 installs downloaded catalogues, and ADR-0011 says an
unsigned or badly signed package is never activated. F11 therefore cannot be completed while F06
is deferred, and its Definition of Done now says so explicitly.

The failure mode worth naming: F11 arrives, the key still does not exist, and verification gets
stubbed out to let the feature land, with a comment promising to enable it later. That ships an
app that installs any file it is handed. If F06 is still deferred when F11 begins, F11 waits.

**Consequence.** F07 must embed a public key placeholder and build the verification path from
the start, so that turning it on is supplying a real key rather than writing new code under
deadline. The keypair should be generated before M3 begins.


### ADR-0032 - A re-import that finds nothing new writes nothing

**Context.** The importer stamped the retrieval date on every record it mapped, then wrote every
record it mapped. Those two facts together meant no record could ever compare equal to itself on
a later run. The first monthly re-import found 127 genuinely new products and opened a pull
request of 31,749 files, 64,086 additions and 58,646 deletions. The repository's `.git` grew from
15 MB to 35 MB in one commit.

The diff was the visible symptom. The damage was `last_verified`. DATA_POLICY.md uses it to
decide when a record is stale and needs re-checking, and the importer was resetting it on every
run, for every record, whether or not anything about the product had changed. A product last
edited upstream in 2019 would have reported itself freshly verified every month, forever. The
staleness thresholds could never have fired.

**Decision.** Writing is conditional on the record's substance changing. Before writing, the
importer reads what is already on disk and compares the two with every retrieval and verification
date blanked. If they match, the file is left exactly as it is, keeping its original dates. The
same rule applies to brand records.

A record that genuinely changed is written and does take the new date, because for that record
the date is true.

**Consequence.** `last_verified` now means what the policy says it means: when this record's
content was last confirmed against a source. It no longer means when a bot last looked at the
file. A monthly import produces a diff a person can read, and the repository grows with the data
rather than with the number of times it has been imported.

**Enforcement.** Comparing the counts is the check. The importer reports how many records it
wrote, and the workflow fails if far more files changed than that, because the gap can only mean
records are being rewritten that should have been left alone. Five tests in
`ImportIdempotencyTests` cover the property directly, including that a real change still is
written and still is re-dated; four of the five fail against the old behaviour.

**What this does not do.** Records already carrying the bumped date keep it. Correcting them
would mean another commit touching every record, which is the exact harm this decision exists to
prevent, to fix a five-day skew that changes no threshold.
