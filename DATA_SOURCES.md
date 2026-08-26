# Data Sources

Every source of product data, its licence, and our decision about using it.

**The rule: no record from any source enters `data/` until that source has an approved row in
the register below.** Publicly visible does not mean reusable. This file is the gate.

Status values: `approved`, `under review`, `rejected`, `not evaluated`.

---

## Register

| Source | Licence | Status | Approved in | Scope |
|---|---|---|---|---|
| _(none yet)_ | | | | |

The register is filled during **F04 - Data acquisition and importer**. Until then the catalogue
contains no imported data.

## Candidate sources to evaluate in F04

These are candidates only. None of them is approved, and no licence below is assumed to be
correct until it has been read and recorded in the register.

| Candidate | Why we want it | What must be verified |
|---|---|---|
| Open Food Facts, India subset | The largest openly licensed packaged food dataset, with Indian coverage and barcode keys | Exact licence version, attribution wording, share-alike obligations on our built catalogue, bulk export terms |
| Wikidata | Brand, manufacturer and parent-company relationships | Licence, and whether the entity coverage of Indian FMCG brands is worth the mapping effort |
| USDA FoodData Central | Generic and raw food composition | Licence and redistribution terms, and whether US generic foods are relevant to an India-first catalogue |
| Indian regulatory publications | Authoritative labelling requirements and product notifications | Copyright status of government publications in India, redistribution rights, machine readability |
| Indian food composition tables | Raw and regional Indian foods, for Phase 3 | Copyright holder, redistribution rights, whether any licence exists at all |
| Manufacturer product pages | The most authoritative source for a specific product | Terms of use per manufacturer, robots policy, whether facts extracted from a page carry any restriction |
| Community photo submissions | Direct evidence from the packet | Consent, contributor licensing, storage and moderation policy |

## What each register row must record

Adding a source means filling all of this, in a pull request that a maintainer approves before
any import runs.

```
Source name
  Homepage:          <url>
  Data location:     <url of the dump, API or endpoint actually used>
  Licence:           <name and version, with a link to the licence text>
  Redistribution:    <may we ship this data inside our catalogue: yes / no / conditional>
  Share-alike:       <does it force a licence on our derived database>
  Attribution text:  <the exact wording the licence requires>
  Retrieval method:  <bulk download, documented API, or per-record fetch>
  Rate limits:       <what we must respect>
  Restrictions:      <anything forbidden, for example commercial use or images>
  Field mapping:     <link to the adapter that maps it into our schema>
  Approved by:       <maintainer, date, pull request link>
  Last reviewed:     <date>
```

## Standing restrictions

1. **Images are not a dependency.** No feature may require a product image. Images are stored
   or cached only where licensing clearly permits it.
2. **Scraping is a last resort.** Documented APIs and bulk exports are preferred. Where a page
   must be read, it is read politely, cached, and never at high frequency.
3. **Attribution ships with the app.** Every approved source appears in the in-app attribution
   screen and in [NOTICE](NOTICE), with the exact wording its licence requires.
4. **Share-alike is respected in full.** Our database is ODbL (see
   [DECISIONS.md](docs/planning/DECISIONS.md), ADR-0003), which keeps us compatible with
   share-alike sources. A source whose terms conflict with ODbL redistribution cannot be
   imported, no matter how useful it is.
5. **A source can be withdrawn.** If a licence changes or a rights holder objects, the source
   is marked rejected and its records are removed from `data/` in the next catalogue build.

## Reviews

Every approved source is re-reviewed at least once a year, and immediately if its terms change.
The scheduled `data-health.yml` workflow reports sources whose last review date has aged out.
