# Product Schema

The canonical specification for Prana data records. The machine-readable schemas are in
[`schema/`](../schema); this document explains what the fields mean and why they are shaped
the way they are.

Worked examples live in [`schema/examples/valid`](../schema/examples/valid). Records that must
be rejected, one per mistake, live in [`schema/examples/invalid`](../schema/examples/invalid).

| Schema | Record | Stored at |
|---|---|---|
| `product.schema.json` | A packaged product | `data/products/{shard}/{gtin}.json` |
| `ingredient.schema.json` | A canonical ingredient | `data/ingredients/{id}.json` |
| `brand.schema.json` | A brand | `data/brands/{id}.json` |
| `category.schema.json` | A product category | `data/categories/{id}.json` |
| `country.schema.json` | Country labelling conventions | `data/countries/{code}.json` |
| `alternative.schema.json` | Curated substitutions | `data/alternatives/{gtin}.json` |
| `common.schema.json` | Shared definitions | referenced by all of the above |

---

## 1. File format

Records are read and written by tools, and also edited by people in pull requests. Both have to
produce the same bytes, or every automated touch would create a noisy diff and review would
become impossible. So the format is fixed:

- UTF-8, no byte order mark.
- LF line endings, including on Windows.
- Two-space indentation.
- Arrays and objects always expanded, one element per line, even short ones.
- One trailing newline at the end of the file.
- Non-ASCII characters written literally, not escaped, so Indian language text stays readable.
- Properties in the order given in this document.
- An unknown value is an **absent property**, never `null`.

`Prana.Core` writes exactly this. If your hand edit differs, the tooling will normalise it.

## 2. The canonical key

Every product is keyed by its barcode zero-padded to **14 digits**.

```
printed on the packet   8901234567890     (EAN-13)
canonical key           08901234567890    (GTIN-14)
stored at               data/products/890/08901234567890.json
```

EAN-13, UPC-A, EAN-8 and GTIN-14 are the same numbering scheme at different widths. A UPC-A code
is the EAN-13 of the same product with its leading zero dropped. Keying on the digits as printed
would store one product under two keys, and no later deduplication would reliably put it back
together.

The check digit is verified before a barcode becomes a key. A mistyped barcode that silently
becomes a key is a duplicate record waiting to happen. `Prana.Core.Barcodes.Gtin` is the single
implementation of all of this, shared by the scanner, the validator, the importer and the
builder.

The directory shard is the first three **significant** digits, taken after the padding zeros.
That keeps directories small enough for Git and for code review at tens of thousands of records.

Products with no barcode are out of scope for Phase 1.

## 3. Product fields

| Field | Required | Meaning |
|---|---|---|
| `schema_version` | yes | Always `1`. Changed only by a migration. |
| `gtin` | yes | Canonical key, 14 digits. |
| `barcode_printed` | yes | The digits as printed, so the app can show what the user is holding. |
| `barcode_format` | yes | `EAN-13`, `EAN-8`, `UPC-A`, `UPC-E`, `GTIN-14` or `ITF-14`. |
| `name` | yes | The name on the front of the packet, including the variant. Not shortened or tidied. |
| `names` | no | Translations keyed by language code. Empty until Phase 2. Never used for lookup. |
| `brand` | no | Slug referencing `data/brands`. |
| `category` | no | Slug referencing `data/categories`. |
| `countries` | yes | ISO 3166-1 alpha-2 codes where the product is sold. |
| `package` | no | Net quantity as declared, and a multipack count when the packet states one. |
| `nutrition` | no | One block per basis printed on the packet. See below. |
| `ingredients_raw` | no | The full ingredient statement copied verbatim. This is the evidence. |
| `ingredients` | no | The parsed tree, derived from `ingredients_raw`. |
| `sources` | yes | The evidence, declared once and referenced by id. |
| `provenance` | yes | Which source backs which part of the record. |
| `conflicts` | no | Recorded disagreements between sources. |
| `verification` | yes | Whether this may be shown as trusted, and when that was established. |

## 4. Nutrition

`nutrition` is an **array of blocks**, not a single set of numbers. Indian packets commonly print
per 100 g and per serving side by side, and those are different measurements of different things.

```json
"nutrition": [
  {
    "basis": "per_100g",
    "values": { "sugars_g": 18, "protein_g": 6 },
    "not_declared": ["added_sugars_g", "trans_fat_g"]
  },
  {
    "basis": "per_serving",
    "serving": { "description": "2 biscuits", "quantity": { "value": 25, "unit": "g" } },
    "values": { "sugars_g": 4.5 }
  }
]
```

Rules, without exception:

- `basis` is always present. It is never inferred and never defaulted.
- Blocks are never merged, and values are never converted between bases.
- `per_serving` requires `serving`. Without a serving mass, a per-serving value cannot be
  compared with anything, so we record what the packet says instead of guessing.
- The basis is shown to the user next to the numbers, always.

### Nutrient fields

The unit is part of the field name, so a value can never be stored without one. This is a closed
set. A nutrient outside it is a schema error, not an extra field, because a typo like
`sugar_g` would otherwise become invisible data loss.

| Field | Unit | Notes |
|---|---|---|
| `energy_kcal` | kcal | |
| `energy_kj` | kJ | |
| `protein_g` | g | |
| `carbohydrate_g` | g | Total carbohydrate. |
| `sugars_g` | g | Total sugars as declared. |
| `added_sugars_g` | g | Only when declared. Never derived from total sugars. |
| `fat_g` | g | Total fat. |
| `saturated_fat_g` | g | |
| `trans_fat_g` | g | |
| `fibre_g` | g | |
| `sodium_mg` | mg | Indian labels usually declare sodium rather than salt. |

## 5. The unknown model

There are three distinct states, and collapsing any two of them loses real information.

| State | How it is written | Meaning |
|---|---|---|
| Declared | the property is present with a number | The packet states this value. |
| Not declared | listed in `not_declared` | We looked at this panel and the packet does not state it. |
| Unresearched | absent from both | Nobody has checked yet. |

This is why a value is never written as `null` and never written as `0` to mean unknown.

The distinction is load-bearing in two places. The app shows "Unknown" rather than a blank or a
zero, which are both read as "none". And the research automation knows not to keep re-researching
a gap that has already been checked and found genuinely absent.

## 6. Provenance

Every published value must be traceable to evidence. The design problem is doing that without
tripling the size of every record and making contributions painful to write.

The answer is **path coverage**. Sources are declared once. The provenance map points paths in
the record at those sources, and **a path covers everything beneath it**.

```json
"sources": [
  { "id": "s1", "type": "packaging", "retrieved_at": "2026-08-20" },
  { "id": "s2", "type": "manufacturer", "url": "https://...", "retrieved_at": "2026-08-21" }
],
"provenance": {
  "nutrition": { "source": "s1", "confidence": "high" },
  "category":  { "source": "s2", "confidence": "medium" }
}
```

One entry for `nutrition` backs the whole panel, which is honest: the entire panel really did
come from one photograph. When one field needs its own evidence, name it specifically and the
more precise path wins:

```json
"nutrition[0].values.sugars_g": { "source": "s1", "confidence": "low" }
```

The validator rejects any published value that no path covers. That rule is what keeps the map
honest while records stay small enough that people will actually write them.

### Source types

Ranked from most to least authoritative. Ranking decides which source wins when they disagree.
It never causes the weaker source to be discarded silently.

| Type | Meaning |
|---|---|
| `packaging` | A photograph of the actual packet. The strongest evidence there is. |
| `manufacturer` | The company's own published product information. |
| `regulator` | An official regulatory publication. |
| `open_database` | An openly licensed dataset, recorded in `DATA_SOURCES.md`. |
| `retailer` | A retailer product listing. |
| `community` | A contributor typed it in with no attachable artefact. |

A search result snippet is not evidence and has no source type.

### Confidence

| Level | Meaning | Handling |
|---|---|---|
| `high` | Several trustworthy sources agree, or packaging evidence is strong | May be merged automatically if all validation passes |
| `medium` | Useful evidence with some uncertainty | Goes to a pull request for review |
| `low` | A single weak or indirect source | Not published as verified |

There is deliberately no `unknown` confidence, because an unknown value is not stored at all and
so can never carry one.

## 7. Conflicts

When sources disagree, the disagreement is recorded. It is never averaged away, and the higher
value is never quietly preferred.

```json
"conflicts": [
  {
    "path": "nutrition[0].values.sugars_g",
    "values": [
      { "source": "s1", "value": 13.1, "note": "Read from the packet photograph." },
      { "source": "s2", "value": 11.8, "note": "Possibly an older formulation." }
    ],
    "resolution": "unresolved"
  }
]
```

An unresolved conflict means the record cannot be `verified`, and the app shows the disagreement
to the user rather than hiding it behind one number.

## 8. Verification and freshness

| Status | Meaning |
|---|---|
| `verified` | Evidence and validation both passed. Shown as trusted catalogue data. |
| `unverified` | In the repository, but must not be presented as trusted. |
| `disputed` | An unresolved conflict exists. |

`last_verified` drives the freshness prompts in [DATA_POLICY.md](../DATA_POLICY.md): current under
six months, review recommended from six to twelve, possibly outdated beyond that. These are
prompts to re-check, not statements that a record is wrong.

## 9. Ingredients

`ingredients_raw` is the evidence: the complete statement copied from the packet, brackets and
percentages included. `ingredients` is the parsed tree derived from it, and can be rebuilt at any
time without loss.

Nesting is preserved, because a label saying chocolate contains sugar is not the same statement
as a flat list containing both.

```json
"ingredients": [
  { "raw": "Refined wheat flour", "canonical": "refined-wheat-flour", "percentage": 45 },
  {
    "raw": "Chocolate",
    "canonical": "chocolate",
    "children": [
      { "raw": "Sugar", "canonical": "sugar" },
      { "raw": "Cocoa butter", "canonical": "cocoa-butter" }
    ]
  }
]
```

- `raw` is never normalised or corrected. It is what the packet says.
- `canonical` may be absent. An unmatched ingredient is a normal state, not an error.
- `percentage` appears only when the packet declares one. It is never derived or estimated.
- Declaration order is preserved, because order carries meaning on a label.

Canonical ingredients carry the flags the app reasons about, such as `palm_derived`. Note what
this does and does not tell you: an ingredient statement declares order, not quantity. Prana can
say a palm-derived oil is listed. It cannot say how much, and it does not pretend to.

## 10. Changing the schema

`schema_version` is bumped only by a migration that rewrites every affected record in one pull
request. Records of mixed versions never coexist.

A change to any schema needs, in the same pull request:

1. The schema file itself.
2. The matching `Prana.Core` model, keeping property declaration order aligned with the file.
3. This document.
4. A new example under `schema/examples/valid` or `schema/examples/invalid`, which the tests
   pick up automatically by file name.

Adding a case is dropping in a file. If the tests need editing to accept a new record shape,
that is a signal the change is bigger than it looks.
