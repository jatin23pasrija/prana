# Phase 1 Feature Backlog

Seventeen features. One feature per branch, one PR, one test round.
Each feature becomes one GitHub Issue in the Project board, titled `F01 - Repository foundation`
and so on. A feature is not done until its Definition of Done is fully ticked and the test
round has passed on the target hardware.

Legend: **Deps** are the features that must be merged first.

---

## M0 - Foundation

### F01 - Repository foundation and governance
**Branch** `feat/f01-repo-foundation` · **Deps** none

Set up the repository so that every later feature has a home and a process.

**In scope**
- Directory skeleton: `app/`, `data/`, `rules/`, `requests/`, `sources/`, `schema/`, `tools/`, `catalogue/`, `docs/`, `.github/`.
- `LICENSE` (Apache-2.0), `LICENSE-DATA` (ODbL 1.0), `NOTICE`.
- `README.md`: what it is, why offline, how it works, how to install, how to contribute, how to fork for another country, licence split, status badges.
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `GOVERNANCE.md`, `SECURITY.md`, `DATA_POLICY.md`, `DATA_SOURCES.md` (skeleton, filled in F04).
- `.gitignore`, `.gitattributes`, `.editorconfig`, `Directory.Build.props`, `global.json`, `Prana.sln`.
- Issue templates: bug, feature, **product request**, product correction, data source proposal. Plus `config.yml`.
- `PULL_REQUEST_TEMPLATE.md` with the DoD checklist.
- `.github/workflows/ci.yml`: build and test the solution. Skeleton only, nothing to build yet.
- GitHub Milestones M0 to M6, labels, and the Project board with all 17 feature issues.

**Out of scope** Any code. Any product data.

**Definition of done**
- [ ] Repo is public on GitHub, `main` protected, CI required.
- [ ] A fresh clone plus `dotnet build` succeeds (empty solution is acceptable).
- [ ] Both licences present and the split is stated in README.
- [ ] All five issue templates render correctly when opening a new issue.
- [ ] 17 issues exist on the Project board, assigned to milestones.

**Test round** Open each issue template in the browser. Open a throwaway PR and confirm the
template and required checks appear.

---

### F02 - Product data schema and provenance model
**Branch** `feat/f02-product-schema` · **Deps** F01

Define the canonical record. Everything downstream is generated from this.

**In scope**
- `schema/product.schema.json` (JSON Schema): identity, package, nutrition with explicit
  `basis`, ingredients with `raw` plus `canonical`, sources, verification, confidence.
- `schema/ingredient.schema.json`, `brand.schema.json`, `category.schema.json`,
  `alternative.schema.json`, `country.schema.json`.
- Per-field provenance: `source_type`, `source_url`, `retrieved_at`, `confidence`.
- Confidence ladder: high, medium, low, unknown, with the handling rule for each.
- `unknown` is a first-class value. Never invent a number.
- `docs/PRODUCT_SCHEMA.md` explaining every field in plain English with examples.
- `Prana.Core` model library with the C# types and System.Text.Json contracts.
- Round-trip tests: JSON to model to JSON is byte-stable.

**Out of scope** Validation rules beyond schema shape (that is F03). Any real data.

**Definition of done**
- [ ] Schema validates the example record from the project plan.
- [ ] Schema rejects: missing basis, negative nutrition, invalid barcode shape, mixed units.
- [ ] `PRODUCT_SCHEMA.md` documents every field, its unit and its unknown handling.
- [ ] Round-trip tests pass.

**Test round** Hand-write three records (good, ambiguous, broken) and check each is
accepted or rejected as expected.

---

### F03 - Validator CLI and data CI
**Branch** `feat/f03-validator` · **Deps** F02

The gate that keeps bad data out of `main`.

**In scope**
- `tools/Prana.Tools.Validator`: `validate <path>` with human and JSON output, non-zero exit on failure.
- Schema validation plus semantic rules: valid GTIN check digit, saturated fat not above total
  fat, sugars not above carbohydrate, no silent serving/100g mixing, explicit unit conversion,
  raw ingredient text preserved, duplicate barcode detection, conflicting sources lower
  confidence rather than being averaged.
- Streaming over the sharded tree, so memory stays flat.
- `.github/workflows/validate-data.yml` running on any `data/**` change, annotating the PR
  at the exact file and line.
- Unit tests per rule, with fixtures.

**Out of scope** Auto-fixing. Research. Building the catalogue.

**Definition of done**
- [ ] Every rule has a passing and a failing test.
- [ ] Validator runs the whole tree in a documented time budget.
- [ ] A PR with a deliberately broken record fails CI with a readable message.
- [ ] Exit codes documented for use by later workflows.

**Test round** Push a branch with one broken record. CI must fail and point at the field.

---

## M1 - Data spine

### F04 - Data acquisition and importer
**Branch** `feat/f04-data-import` · **Deps** F03

Fill the catalogue at real scale. This is the biggest data feature.

**In scope**
- `DATA_SOURCES.md` completed: for each candidate source, record licence, redistribution
  rights, attribution text, retrieval method and any restriction. **No source is imported
  until its row is filled and approved.**
- Candidate sources to evaluate (licence status to be verified per source, not assumed):
  Open Food Facts India subset, Wikidata for brand and company metadata, USDA FoodData
  Central for generic foods, Indian regulatory publications, manufacturer sites per-source.
- `tools/Prana.Tools.Importer`: source adapter interface, one adapter per approved source,
  mapping into the canonical schema, provenance stamped on every field.
- Deduplication and conflict handling across sources.
- Quality gate: only records meeting confidence rules enter the verified set.
- A hand-verified golden set of about 50 Indian products kept for regression testing.
- Repo scale management: measure clone, status and checkout time at full volume.

**Out of scope** Live scraping from the app. Image redistribution.

**Definition of done**
- [ ] Every imported source has an approved `DATA_SOURCES.md` row and attribution.
- [ ] Import is repeatable and idempotent. Re-running changes nothing.
- [ ] Whole tree passes the F03 validator.
- [ ] Golden set is byte-stable across imports.
- [ ] Git operation timings recorded in the PR.

**Test round** Full import from scratch, validator clean, spot-check 20 records against
their real packaging.

---

### F05 - Catalogue builder
**Branch** `feat/f05-catalogue-builder` · **Deps** F04

Turn the Git tree into the mobile database.

**In scope**
- `tools/Prana.Tools.CatalogueBuilder`: sharded JSON in, one SQLite file out.
- Schema tuned for the phone: lookup by barcode, FTS5 for name and brand, category and
  nutrition indexes for the alternatives engine.
- Compression, `catalogueVersion`, `schemaVersion`, `minimumAppVersion`.
- `manifest.json` with version, size, SHA-256 and signature slot.
- Reproducible output: same input produces the same bytes.
- `docs/CATALOGUE_FORMAT.md` so third parties can consume it without the app.

**Out of scope** Delta updates (explicitly deferred by the project plan).

**Definition of done**
- [ ] Build from the full tree completes inside a documented time and size budget.
- [ ] Byte-identical output on repeat runs.
- [ ] Barcode lookup query plan uses an index, verified with `EXPLAIN QUERY PLAN`.
- [ ] `CATALOGUE_FORMAT.md` is complete enough to write a third-party client from.

**Test round** Build, open the result in a SQLite browser, run lookups by hand.

---

### F06 - Signed release pipeline
**Branch** `feat/f06-release-pipeline` · **Deps** F05 · **DEFERRED**

> **Deferred on 2026-08-31, waiting on key generation.** Nothing about the design changed; the
> signing keypair has to be created and stored by a person, and that has not happened yet.
> See ADR-0031 for what this blocks and what it does not.

Publish the catalogue so a phone can fetch it.

**In scope**
- Key generation procedure, private key in Actions secrets, public key committed for the app.
- `.github/workflows/build-catalogue.yml` and `release-catalogue.yml`: build, checksum, sign,
  create a versioned GitHub Release with the package, manifest and signature attached.
- A stable `latest` manifest URL the app can poll.
- Key rotation procedure written into `SECURITY.md`.

**Out of scope** The app side of sync (F11).

**Definition of done**
- [ ] A real release exists and is downloadable over plain HTTPS.
- [ ] Signature verifies with the public key using a documented command.
- [ ] A tampered package fails verification in a recorded test.
- [ ] Rotation procedure documented and dry-run once.

**Test round** Download on a laptop, verify checksum and signature manually, then corrupt
one byte and confirm verification fails.

---

## M2 - Offline app

### F07 - MAUI application skeleton
**Branch** `feat/f07-app-skeleton` · **Deps** F05

**In scope**
- `app/Prana.Mobile` targeting Android and iOS, `com.prana.app`.
- Dependency injection, MVVM, Shell navigation, light and dark theme, app icon and splash.
- Home screen from the plan UX sketch: Scan Product, My Grocery, Search Catalogue, Recent.
- Startup must not block on network.
- CI builds Android and iOS.

**Definition of done**
- [ ] Debug APK installs and runs on the physical Android device.
- [ ] iOS target compiles in CI.
- [ ] Cold start time recorded on the real device.
- [ ] Navigation between all placeholder screens works.

**Test round** Install on the phone, navigate every route, force-close and relaunch.

---

### F08 - Local catalogue data layer
**Branch** `feat/f08-data-layer` · **Deps** F07

**In scope**
- `Prana.Data`: `catalogue.db` opened read-only, `user.db` created with migrations (ADR-0007).
- Repository interfaces: `IProductRepository`, `IIngredientRepository`, `ISearchRepository`.
- A starter catalogue bundled in the app so first launch is useful with no network.
- Handle all three build flavours from ADR-0030: bundled full catalogue, bundled starter, and
  none at all. An absent bundle must not be an error.
- FTS5 availability verified on Android and iOS.
- Lookup performance measured on the real low-end device.

**Definition of done**
- [ ] Barcode lookup returns from the bundled catalogue with the network fully off.
- [ ] Lookup latency measured and recorded on the physical device.
- [ ] `user.db` migrations run on first launch and are idempotent.
- [ ] Missing or corrupt catalogue file degrades gracefully instead of crashing.

**Test round** Aeroplane mode, look up ten known barcodes, delete the catalogue file and
confirm graceful behaviour.

---

### F09 - Barcode scanner
**Branch** `feat/f09-scanner` · **Deps** F08

**In scope**
- `IBarcodeScanner` abstraction plus the open-source implementation (ADR-0006).
- Camera permission request, denial and recovery path, manual barcode entry fallback.
- EAN-13, EAN-8, UPC-A, UPC-E, GTIN-14. Check-digit validation and normalisation.
- Torch, continuous scan, duplicate-scan debounce.

**Definition of done**
- [ ] Scans at least 20 real Indian products, with the success rate recorded in the PR.
- [ ] Permission denial shows a recovery path, not a dead end.
- [ ] Manual entry works when the camera is unavailable.
- [ ] Invalid check digits are rejected with a clear message.

**Test round** Physical shopping bag test. Record hit rate, retry count and time to decode.

---

### F10 - Product details and analysis
**Branch** `feat/f10-product-details` · **Deps** F09

**In scope**
- Product screen from the UX sketch: identity, nutrition table with basis shown, ingredients,
  detected attributes, sources, verification status and date.
- Transparent indicators (Higher, Moderate, Lower) driven by versioned rules in `rules/`,
  not a single opaque score.
- Palm-derived ingredient states: Present, Not detected in available ingredients, Unknown,
  Confirmed quantity. Never an invented amount.
- Unknown values render as Unknown, never as zero or blank.
- Stale-data notice using the verification date thresholds.

**Definition of done**
- [ ] Every indicator explains itself when tapped, naming the rule and its version.
- [ ] A record with mostly unknown fields renders honestly and without empty gaps.
- [ ] A record with no nutrition and no ingredients is shown as incomplete and offers to search
      online, rather than looking like a complete answer. Required by ADR-0026.
- [ ] No medical or absolute health claim appears anywhere in the UI copy.
- [ ] Renders correctly in light and dark theme and at large font sizes.

**Test round** Scan five real products and compare every rendered number against the packet.

---

## M3 - Sync

### F11 - Catalogue sync and atomic install
**Branch** `feat/f11-sync` · **Deps** F10 **and F06**

> F06 is a hard dependency, not an ordering one. ADR-0011 says an unsigned or badly signed
> package is never activated, so this feature cannot be finished while F06 is deferred.

The highest-risk feature in Phase 1. Failure here can destroy a working catalogue.

**In scope**
- Background manifest check, version and compatibility comparison against `minimumAppVersion`.
- Do not re-download a catalogue the installed flavour already carries (ADR-0030).
- Resumable background download, metered-connection and Wi-Fi-only preference.
- Verify size, SHA-256 and signature before anything is opened.
- Open in a temporary location, run `PRAGMA integrity_check` and schema checks.
- Atomic activation, previous catalogue retained until the new one is proven, then cleanup.
- Full rollback on any failure at any step.

**Definition of done**
- [ ] Every drill in the failure matrix passes: interrupted download, power loss mid-install,
      Wi-Fi to mobile switch, no connectivity, GitHub unavailable, corrupted package,
      bad signature, low storage, app older than `minimumAppVersion`.
- [ ] A failed update never leaves the app without a working catalogue. Proven by test.
- [ ] No temporary files survive a failure.
- [ ] Sync never blocks the UI.
- [ ] Signature verification is enforced, not stubbed. This feature may not be marked done with
      verification disabled or bypassed, however convenient that would be (ADR-0011, ADR-0031).

**Test round** Run the full failure matrix on the physical device, one drill at a time,
recorded in the PR.

---

## M4 - Discovery and contribution

### F12 - Online product discovery
**Branch** `feat/f12-discovery` · **Deps** F11

**In scope**
- `IDiscoverySource` adapters. Approved sources only, from `DATA_SOURCES.md`.
- Search ladder from the plan: exact barcode, then barcode plus nutrition, then barcode plus
  ingredients, then name plus brand, preferring primary and manufacturer sources.
- Structured APIs preferred over HTML parsing. All external content treated as untrusted.
- Temporary result screen, visually distinct from trusted catalogue data, showing sources,
  confidence and any conflict between sources.
- Only triggered when the local lookup misses.

**Definition of done**
- [ ] A trusted record and a discovered record are impossible to confuse in the UI.
- [ ] Source list, retrieval time and conflicts are all shown.
- [ ] Timeouts, offline and empty results all handled without a crash.
- [ ] Discovered data never enters `catalogue.db`.

**Test round** Scan products known to be missing. Confirm the temporary labelling is
unmistakable to someone who has not read the docs.

---

### F13 - Community contribution flow
**Branch** `feat/f13-contribution` · **Deps** F12

**In scope**
- Tier 1: prefilled GitHub Issue opened in the browser, matching the product request template.
- Tier 2: local pending-request queue, retried when online, with export and share for people
  with no GitHub account.
- Tier 3: design and decision on the optional stateless relay, including abuse controls
  (ADR-0010). Implementation may be split into its own follow-up feature.
- User confirm and edit step before submission. Opt-in only for any photo.
- Absolutely no GitHub write token in the app. Verified by inspecting the built APK.

**Definition of done**
- [ ] A real issue is created end to end from the phone.
- [ ] Offline requests queue and submit later without duplicating.
- [ ] APK inspection shows no repository credential of any kind.
- [ ] Submission takes a first-time user under one minute, timed with a real person.

**Test round** Hand the phone to someone who has never used GitHub and watch them submit.

---

### F14 - Research automation and auto-PR
**Branch** `feat/f14-research-automation` · **Deps** F13

**In scope**
- `.github/workflows/research-product.yml` triggered by a product request issue.
- Seed the queue from `import-gaps.json`, so the automation starts with tens of thousands of
  known targets rather than waiting for a user to scan one.
- `tools/Prana.Tools.Researcher`: identity discovery, source search, ranking, extraction,
  normalisation, cross-source validation, schema validation, confidence calculation.
  Deterministic only (ADR-0012).
- High confidence plus all rules passing produces a PR eligible for auto-merge.
- Anything ambiguous stays a visible PR or Issue for community review.
- Human review triggers from the plan: source conflict, uncertain identity, duplicate barcode,
  incomplete evidence, large unexplained change, safety or recall signal, licensing doubt.
- Least-privilege workflow permissions. External content never trusted.
- `docs/RESEARCH_PIPELINE.md`.
- Scheduled workflows: data health check, request prioritisation by demand.

**Definition of done**
- [ ] A test issue produces a valid PR with full provenance.
- [ ] A deliberately ambiguous case is routed to review, not merged.
- [ ] Auto-merge is impossible when any validation rule fails.
- [ ] Workflow permissions are minimal and documented.

**Test round** The full loop: submit from the phone, watch the PR appear, merge, rebuild
catalogue, release, sync, find the product offline. This is the Phase 1 success criterion.

---

## M5 - Everyday utility

### F15 - Alternatives engine
**Branch** `feat/f15-alternatives` · **Deps** F14

**In scope**
- Candidate constraints: same or compatible category, similar use case, available in country,
  comparable package or serving, better on the relevant dimensions.
- Ranking computed locally from the installed catalogue. Works offline.
- Every suggestion carries a specific reason (lower sugar, higher fibre, no palm-derived oil).
  Never a bare claim that something is healthy.
- Versioned, explainable rules in `rules/`.

**Definition of done**
- [ ] Suggestions are plausible substitutes, reviewed against 20 real products.
- [ ] Every suggestion shows a concrete comparative reason.
- [ ] Runs fully offline within a measured latency budget on the physical device.
- [ ] Returns nothing rather than something irrelevant when no good candidate exists.

**Test round** Manual review of the suggestions for 20 common Indian products.

---

### F16 - Grocery list and basket summary
**Branch** `feat/f16-grocery` · **Deps** F15

**In scope**
- Local-only list in `user.db`. No account, no upload.
- Add by scan or by catalogue search, quantity, optional serving estimate.
- Basket summary that is informative and not a medical assessment.
- A small number of meaningful swaps, not dozens.
- Survives catalogue replacement, including a product that disappears (ADR-0007).

**Definition of done**
- [ ] List survives app restart and a full catalogue swap.
- [ ] Summary avoids medical framing, checked line by line against the copy rules.
- [ ] Works entirely offline.
- [ ] Nothing leaves the device. Verified by network capture.

**Test round** Build a 12-item basket, replace the catalogue, confirm the list is intact.

---

## M6 - Public release

### F18 - Ingredient dictionary
**Branch** `feat/f18-ingredient-dictionary` · **Deps** F10

F10 seeded `data/ingredients/` with the palm vocabulary it needed: 16 records, 67 aliases. This
feature owns growing it, and nothing else currently does.

The corpus says what is worth having. The ingredient text across the catalogue holds 13,915
distinct terms; 273 of them cover 53 per cent of all mentions, 595 cover 63 per cent, and 1,188
cover 70 per cent. Coverage past that is a long tail of one-off wordings.

**In scope**
- Grow the dictionary to the terms appearing 10 or more times, roughly 595 entries.
- Aliases for OCR corruptions seen on real packets. "Iodised salt" is printed or transcribed as
  "lodised salt" on 349 products and "lodized salt" on 313, and neither matches today.
- INS additive numbers, which Indian labels use in place of names.
- Flags beyond palm: added sugar, artificial colour, preservative, common allergen, and the
  may_be_animal_derived case that some additives genuinely need.

**Definition of done**
- [ ] Every alias was observed in real label text, with the product count recorded in review.
- [ ] No entry carries a health claim. The explanation says what a thing is and why it is on a
      label, never whether it is good for you.
- [ ] Ambiguity is recorded as ambiguity: where a label does not say enough, the flag says so
      rather than guessing either way.
- [ ] Measured coverage of ingredient mentions reported before and after.

**Test round** Take 20 real ingredient statements and check every term the dictionary claims to
recognise, and every term it does not.

---

### F17 - Hardening and first public release
**Branch** `feat/f17-release` · **Deps** F16

**In scope**
- Full production readiness matrix from the project plan: mobile, catalogue, network failure,
  research automation.
- Low-end device performance, low storage, battery and background behaviour.
- Release signing for the APK, `.github/workflows/release-app.yml`.
- Three flavours built, signed, tested and published per release (ADR-0030).
- In-app APK update check (ADR-0005).
- Accessibility pass: screen reader labels, contrast, large text.
- Final documentation sweep and a first-run onboarding that states the data is community
  maintained and may be incomplete.

**Definition of done**
- [ ] Every row of the readiness matrix is executed and recorded.
- [ ] Signed APK attached to a GitHub Release and installs cleanly on a fresh device.
- [ ] All 21 items of the Phase 1 Definition of Done in the project plan are ticked.
- [ ] README quick-start works for someone who has never seen the repo.

**Test round** Fresh device, fresh install, no prior state, full user journey end to end.
