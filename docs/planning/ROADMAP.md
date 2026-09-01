# Prana Roadmap

Prana is an open-source, offline-first food intelligence app for India.
GitHub is the source of truth. There is no application server and no hosted database.

This document defines the phases and milestones. The per-feature detail lives in
[FEATURES.md](FEATURES.md). The process rules live in [WORKFLOW.md](WORKFLOW.md).
The locked technical decisions live in [DECISIONS.md](DECISIONS.md).

---

## Phase 1 - Core deliverable

Phase 1 is the whole product requirement. Everything after it is optional.

**Phase 1 success criterion (from the project plan):**
A user scans an unknown Indian product, the app discovers it online, the user confirms it,
a community contribution is generated, automation validates and researches it, the catalogue
is rebuilt and released, and a later app sync makes the product available offline,
with no application server and no hosted database anywhere in the loop.

### Milestones

| Milestone | Name | Features | Exit condition |
|---|---|---|---|
| M0 | Foundation | F01, F02, F03 | Repo, licences, schema and validator exist. CI rejects invalid data. |
| M1 | Data spine | F04, F05, ~~F06~~ | Catalogue builds reproducibly. F06 deferred, waiting on key generation (ADR-0031). |
| M2 | Offline app | F07, F08, F09, F10 | Scan a barcode on a real phone in aeroplane mode and see the product. |
| M3 | Sync | F11 | App upgrades its catalogue in the background and survives every failure drill. |
| M4 | Discovery and contribution | F12, F13, F14 | Unknown product goes from scan to merged PR without a maintainer typing anything. |
| M5 | Everyday utility | F15, F16 | Alternatives and grocery list work fully offline. |
| M6 | Public release | F17 | Signed APK on GitHub Releases, failure matrix passed, docs complete. |

### Milestone dependency flow

```
M0  Foundation
      |
      v
M1  Data spine ------------------+
      |                          |
      v                          v
M2  Offline app             M4  Discovery + contribution
      |                          |
      v                          |
M3  Sync <---------------------- +
      |
      v
M5  Everyday utility
      |
      v
M6  Public release
```

M4 needs M1 (a catalogue format to contribute into) and M2 (a screen to trigger it from).
M3 needs M1 (a release to download) and M2 (a data layer to swap).

---

## Phase 2 - Strong community utility

Not started until Phase 1 is released and stable.

- Allergens: contains / may contain / facility warnings, allergen filtering, local household profile.
- Dietary attributes: vegetarian, vegan, egg, dairy, gluten, country-specific rules.
- OCR label scanner: photograph nutrition and ingredient panels, contribute from extracted data.
- Better alternatives: use-case aware ranking, not score ranking.
- Product comparison: two or more products side by side.
- Multilingual: Hindi, Punjabi, then community translations. Canonical data stays language neutral.
- Accessibility: screen reader, large text, high contrast, voice summaries, simple-language explanations.

## Phase 3 - Broader food intelligence

- Pantry mode, on device only.
- Recipe engine with approximate nutrition.
- Indian regional food database (dal, chole, rajma, sattu, millets, poha, idli, dosa) with common serving sizes.
- Serving-size intelligence.
- Price and unit-value comparison where legally permitted.
- Product formulation history and shrinkflation detection.

## Phase 4 - Community ecosystem

- Published open data specification for third-party clients.
- Country-specific catalogues and rule packs.
- Community tooling and data missions.
- Automated quality and stale-source monitoring.
- Official recall datasets, with strict source requirements.

---

## Out of scope, permanently

No commercial marketplace. No affiliate requirement. No subscription. No user accounts.
No always-on product API. No hosted database. No medical diagnosis or medical nutrition advice.
No absolute claim that any product is healthy or unhealthy.
