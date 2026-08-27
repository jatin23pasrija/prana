# Catalogue Format

The catalogue is a plain SQLite database, Brotli compressed, published as a release artefact
alongside a manifest.

This document is written so you can build a client without using our app, and without asking us
anything. That is the point of publishing it: the data is ODbL, and a format only we understand
would make that licence a formality.

Built by [`tools/Prana.Tools.CatalogueBuilder`](../tools/Prana.Tools.CatalogueBuilder).

---

## What you get

| File | What it is |
|---|---|
| `catalogue.db.br` | Every product. Downloaded after install. |
| `catalogue-starter.db.br` | The most complete products only, small enough to bundle in an app. |
| `manifest.json` | Versions, sizes, checksums, licence and attribution. |

Measured on the 2026-08-27 build of 26,453 Indian products:

```
full     15.9 MB SQLite  ->  2.5 MB compressed   (26,453 products)
starter   2.4 MB SQLite  ->  0.4 MB compressed   ( 2,000 products)
```

Decompress with any Brotli decoder. The result is an ordinary SQLite file: `sqlite3 catalogue.db`
works, and so does every SQLite binding in every language.

Brotli rather than zstd, which the original plan named, because Brotli is built into .NET and
into browsers, so no client needs a native dependency to read a catalogue.

## Identifying the file

`application_id` is `0x50524E41` ("PRNA") and `user_version` holds the schema version. Both are
readable without parsing anything:

```sql
PRAGMA application_id;   -- 1347240001
PRAGMA user_version;     -- 1
```

## The manifest

```json
{
  "catalogueVersion": 1,
  "schemaVersion": 1,
  "minimumAppVersion": "1.0.0",
  "builtOn": "2026-08-27",
  "licence": "ODbL-1.0 (database), DbCL-1.0 (contents)",
  "attribution": "Contains data from Open Food Facts ...",
  "full":    { "file": "catalogue.db.br", "products": 26453, "incomplete": 15414,
               "sizeBytes": 2600000, "uncompressedBytes": 16700000, "sha256": "..." },
  "starter": { "file": "catalogue-starter.db.br", "products": 2000, "incomplete": 0, "...": "..." },
  "signature": null
}
```

`catalogueVersion` increases with every release. `schemaVersion` changes only when the tables
below change; a client that does not understand it must refuse the file rather than guess.
`minimumAppVersion` is the contract between the two release trains: an app older than this must
refuse the catalogue and tell the user to update. `signature` is filled from F06 onwards.

## Tables

### `meta`

Key and value strings. Always present: `catalogue_version`, `schema_version`, `built_on`,
`minimum_app_version`, `kind` (`full` or `starter`), `licence`, `attribution`.

**If you redistribute this data, `licence` and `attribution` come with it.** ODbL is share-alike.

### `product`

| Column | Notes |
|---|---|
| `gtin` | Primary key. The barcode zero-padded to 14 digits. |
| `barcode_printed` | The digits as printed, for display. |
| `barcode_format` | `EAN-13`, `EAN-8`, `UPC-A`, `UPC-E`, `GTIN-14`, `ITF-14`. |
| `name` | As printed on the front of the packet. |
| `brand_id`, `category_id` | References, may be null. |
| `package_value`, `package_unit`, `multipack_count` | Net quantity as declared. |
| `ingredients_raw` | The ingredient statement verbatim. This is the evidence. |
| `verification_status` | `verified`, `unverified` or `disputed`. |
| `last_verified` | `YYYY-MM-DD`. |
| `is_complete` | `0` when the record has neither nutrition nor ingredients. |

Look products up by `gtin`. It is the primary key, so the query plan is
`SEARCH product USING PRIMARY KEY`. A thousand lookups take about 28 ms.

**`is_complete = 0` matters more than it looks.** 58% of the current catalogue is a barcode and a
name and nothing else. Those records exist so that a scan can say "this is Parle-G, and we know
nothing else" rather than "not found", which is both more useful and more honest.

A client that treats them as complete answers will silently stop offering to look the product up
online, which is how the catalogue improves. If you build a client, check this column.

### `nutrition`

One row per declared panel, keyed by `(gtin, block_index)`.

**Read `basis` before you read any number.** It is `per_100g`, `per_100ml`, `per_serving` or
`per_package`. Panels are never merged and values are never converted between bases, because
most labels do not carry the information a conversion would need. A per-serving figure shown as
per 100 g is a wrong number presented confidently.

When `basis` is `per_serving`, `serving_description` is what the packet printed and
`serving_value` with `serving_unit` give the mass when it was stated. Without a mass, the panel
cannot be compared with anything.

Nutrient columns: `energy_kcal`, `energy_kj`, `protein_g`, `carbohydrate_g`, `sugars_g`,
`added_sugars_g`, `fat_g`, `saturated_fat_g`, `trans_fat_g`, `fibre_g`, `sodium_mg`. The unit is
in the name. `NULL` means unknown, never zero.

### `nutrition_not_declared`

`(gtin, block_index, field)`. Nutrients checked against that panel and confirmed absent from the
packet.

This is the difference between "the packet does not state it" and "nobody has looked". A row
here is the first. No row and no value is the second. Show them differently.

### `ingredient_item`

The parsed ingredient tree: `(gtin, ordinal)`, with `parent_ordinal` for sub-ingredients
declared inside brackets, plus `raw`, `canonical_id` and `percentage`.

**Empty today.** No record has a parsed tree yet; parsing Indian ingredient statements is its own
piece of work. The table exists so that filling it is an insert rather than a schema change.
Until then use `product.ingredients_raw`.

### `ingredient`, `ingredient_alias`, `ingredient_flag`

The canonical ingredient dictionary, its label wordings, and attributes such as `palm_derived`.
Also empty in the current build.

### `brand`, `category`, `country`

Reference data. `category` carries `typical_basis`, and `category_substitute` plus
`category_nutrient` say which categories are reasonable substitutes for each other and which
nutrients are worth comparing within one. That is what keeps a suggested alternative sensible: a
biscuit is not a substitute for cooking oil however good its numbers look.

### `source` and `provenance`

`source` is the evidence per product: type, url, retrieval date, licence. `provenance` maps a
path in the original record, such as `nutrition` or `name`, to a source and a confidence.

A path covers everything beneath it, so one `nutrition` entry backs a whole panel. That is how
the repository stores it, and it is why a value with no evidence cannot exist. If you show a
number, you can show where it came from.

### `product_search`

FTS5 over `name` and `brand`, with `gtin` unindexed.

```sql
SELECT gtin FROM product_search WHERE product_search MATCH 'parle';
```

Ingredients are deliberately not indexed. It would roughly double the index to answer a question
almost nobody asks while standing in a shop.

## Reproducibility

The same repository state produces a byte-identical catalogue. Verified on every build:

```
prana-catalogue build --built-on 2026-08-27 --verify-reproducible
  identical: 890b5bef963026eb5f1cf98a2216d5b550404789fc95a8eb66521dadb6a77392
```

This needs a fixed page size, journalling off, a deterministic write order, and `VACUUM` at the
end. `--built-on` must be passed explicitly, because the build date is stamped into the file and
a build straddling midnight would otherwise differ from itself.

It matters because without it nobody downstream can tell whether a new catalogue actually
contains anything new.

## Building it yourself

```bash
dotnet run --project tools/Prana.Tools.CatalogueBuilder -- build \
  --built-on 2026-08-27 --verify-reproducible
```

| Option | Meaning |
|---|---|
| `--version <n>` | Catalogue version stamped into the build |
| `--starter-size <n>` | Products in the starter catalogue, `0` to skip |
| `--minimum-app <v>` | App version required to read it |
| `--built-on <date>` | Build date. Pass it for a reproducible build |
| `--verify-reproducible` | Build twice and compare hashes |

Exit codes: `0` built, `1` built but not reproducible, `2` could not run.

## Changing the schema

Any change to the tables above is a change to a published contract that other people's software
depends on. It needs, in one pull request: the change, a `CatalogueSchema.Version` bump, this
document, and a test showing what a client would now see.
