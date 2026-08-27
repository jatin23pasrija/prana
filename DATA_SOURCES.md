# Data Sources

Every source of product data, its licence, and our decision about using it.

**The rule: no record from any source enters `data/` until that source has an approved row in
the register below.** Publicly visible does not mean reusable. This file is the gate, and the
importer enforces it: a source with no approved row has no adapter, so there is no code path
that can import it.

Status values: `approved`, `under review`, `rejected`, `not evaluated`.

---

## Register

| Source | Licence | Status | Scope |
|---|---|---|---|
| [Open Food Facts](#open-food-facts) | ODbL 1.0 + DbCL 1.0 | **approved** | Indian packaged products: identity, nutrition, ingredients |
| [Wikidata](#wikidata) | CC0 1.0 | **approved** | Brand and parent-company metadata. Not yet imported |
| [USDA FoodData Central](#usda-fooddata-central) | Public domain / CC0 1.0 | **approved, deferred** | Generic food composition. Little Indian relevance |
| [GS1 India](#gs1-india) | Commercial | **rejected** | Barcode registry |

---

### Open Food Facts

```
Homepage:          https://world.openfoodfacts.org
Data location:     https://static.openfoodfacts.org/data/openfoodfacts-products.jsonl.gz
Licence:           ODbL 1.0 for the database, DbCL 1.0 for the contents,
                   CC-BY-SA for images
Redistribution:    Yes, under ODbL share-alike
Share-alike:       Yes. Our derived database must also be ODbL
Attribution text:  Data from Open Food Facts (https://world.openfoodfacts.org), made available
                   under the Open Database License (ODbL) v1.0.
Retrieval method:  JSONL bulk export, streamed by .github/workflows/import-openfoodfacts.yml
Rate limits:       The search API rate-limits bulk paging. It must not be used for import
Restrictions:      Images are not imported at all
Field mapping:     tools/Prana.Tools.Importer/Sources/OpenFoodFacts/OffMapper.cs
Approved:          2026-08-27, F04
Last reviewed:     2026-08-27
```

**Why the export and not the API.** Paging the search API returns 503 after a handful of
requests. That is Open Food Facts asking to be left alone, and it is also the documented
guidance. The export is the sanctioned path for this much data. The API is still used for
single-product lookups in F12, which is what it is for.

**Why the JSONL export and not the smaller CSV one.** This one cost us a rewrite, so it is
worth recording. Open Food Facts publishes a tab separated export of about 1.2 GB and a JSONL
export of about 12 GB. The small one is the obvious choice until you read its header: it has
211 columns, **none** of which is `nutrition_data_per`, and it carries no per-serving columns at
all. Every nutrient appears only as a normalised `*_100g` value, including for products whose
label declared a per-serving panel and whose figures were therefore derived by dividing through
by the serving size.

Importing that would have labelled the entire catalogue as per 100 g, silently, with no way for
anyone downstream to tell which figures were declared and which were computed. That is precisely
the conversion [DATA_POLICY.md](DATA_POLICY.md) forbids, committed once across every product.

The JSONL export carries the full product document, declared basis and serving included. It is
ten times the size, so it is never stored: the workflow pipes it from the network through
decompression into the importer, and nothing larger than one product exists at a time.

The tab separated export is not used, and there is deliberately no adapter for it, so nobody
reaches for it later on a slow afternoon.

**What we take.** Products either tagged as sold in India or carrying a GS1 India barcode
prefix. The country tag is contributor-entered and missing on many records, so taking both
signals finds products the tag alone would lose. A record needs a name, and nothing more.

Records with a name but no nutrition and no ingredients are kept deliberately. Telling someone
the packet in their hand is Parle-G and that we know nothing else beats telling them the product
does not exist, and it is honest about which of those is true. The import writes those barcodes
to a gap queue, which is the seed work list for the research automation. This is only safe
because the app treats such a record as incomplete and still offers to search online and
contribute, which ADR-0026 makes a requirement on F10 and F12 rather than a hope.

**What we change.** Three things, all documented in the mapper:

The importer reads `nutrition_data_per` and takes the figures matching the basis the packet
declared, rather than the normalised per-100g ones sitting beside them. When the source does not
say what the numbers are measured against, the record gets no nutrition at all. Guessing would
be the same silent conversion in a different disguise.

Sodium is stored in grams upstream and in milligrams here. The conversion happens before
rounding, because rounding first turns 0.296 g into 300 mg instead of 296 mg, a quiet 1.4% error
on every sodium figure in the catalogue.

Brand names become slugs, and accented Latin characters are folded to ASCII explicitly rather
than through Unicode normalisation, which is a no-op under `InvariantGlobalization`.

**Confidence.** Every imported record is `unverified` with `medium` confidence. Open Food Facts
is community-entered, and one community database agreeing with itself is not corroboration.
Marking it verified would make our own verification vocabulary meaningless.

---

### Wikidata

```
Homepage:          https://www.wikidata.org
Licence:           CC0 1.0 for structured data in the main, property and lexeme namespaces
Redistribution:    Yes, unrestricted
Share-alike:       No
Attribution text:  Not required. We credit it anyway in NOTICE
Retrieval method:  SPARQL endpoint or bulk dump
Restrictions:      Text outside the structured namespaces is CC-BY-SA, and is not used
Field mapping:     None yet
Approved:          2026-08-27, F04
Last reviewed:     2026-08-27
```

Approved but not yet imported. Wikidata is good for brand and parent-company relationships,
which is a transparency feature rather than a coverage one: it tells a user which company owns a
brand. It holds almost no barcode-level Indian product data, so it does not help coverage.

---

### USDA FoodData Central

```
Homepage:          https://fdc.nal.usda.gov
Licence:           Public domain, published under CC0 1.0
Redistribution:    Yes, unrestricted. Attribution requested, not required
Share-alike:       No
Attribution text:  Data from USDA FoodData Central
Retrieval method:  Bulk download
Restrictions:      None
Field mapping:     None yet
Approved:          2026-08-27, F04
Last reviewed:     2026-08-27
```

Approved and deliberately deferred. The licence is the most permissive of any source here, but
the content is US generic foods and US branded products. It is worth revisiting in Phase 3 for
generic food composition, where a lentil is a lentil regardless of country. It does nothing for
Indian packaged food coverage today.

---

### GS1 India

```
Homepage:          https://www.gs1india.org
Licence:           Commercial, subscription
Redistribution:    No
Status:            rejected
Last reviewed:     2026-08-27
```

The authoritative registry of Indian barcodes, and unusable. It is commercial, redistribution is
not permitted, and nothing about it fits a project that must remain forkable. Recorded here so
nobody spends another afternoon reaching the same conclusion.

---

## On coverage

There is no second bulk source for Indian packaged food. Open Food Facts is it. Everything else
openly licensed is either the wrong country, the wrong granularity, or not redistributable.

That is not a gap in the plan, it is the reason for the plan. Coverage past the initial import
comes from people scanning products we do not have, which triggers discovery (F12), a
contribution (F13), and automated research (F14). The catalogue is meant to grow from demand
rather than from finding one more dataset to copy.

If you know a source we have missed, open a
[data source proposal](https://github.com/jatin23pasrija/prana/issues/new/choose). The licence
review is genuinely the hard part, so the form asks about it first.

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

1. **Images are not a dependency.** No feature may require a product image. Images are stored or
   cached only where licensing clearly permits it. Open Food Facts images are CC-BY-SA and are
   not imported.
2. **Scraping is a last resort.** Documented APIs and bulk exports are preferred. Where a page
   must be read, it is read politely, cached, and never at high frequency. Where a source
   rate-limits us, that is an answer, not an obstacle to route around.
3. **Attribution ships with the app.** Every approved source appears in the in-app attribution
   screen and in [NOTICE](NOTICE), with the exact wording its licence requires.
4. **Share-alike is respected in full.** Our database is ODbL, which keeps us compatible with
   share-alike sources. A source whose terms conflict with ODbL redistribution cannot be
   imported, no matter how useful it is.
5. **A source can be withdrawn.** If a licence changes or a rights holder objects, the source is
   marked rejected and its records are removed from `data/` in the next catalogue build.

## Reviews

Every approved source is re-reviewed at least once a year, and immediately if its terms change.
The scheduled `data-health.yml` workflow reports sources whose last review date has aged out.
