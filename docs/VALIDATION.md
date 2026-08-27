# Validation

Every data change passes through `prana-validate` before it can reach `main`. This document is
the reference for what it checks, what the codes mean, and what the exit codes promise.

The schema itself is documented in [PRODUCT_SCHEMA.md](PRODUCT_SCHEMA.md).

---

## Running it

```bash
# Check everything under data/
dotnet run --project tools/Prana.Tools.Validator -- validate

# Check one record, or a directory
dotnet run --project tools/Prana.Tools.Validator -- validate data/products/890/08901234567890.json

# Fix formatting rather than arguing with CI about indentation
dotnet run --project tools/Prana.Tools.Validator -- format data
```

| Option | Effect |
|---|---|
| `--strict` | Warnings count as errors. Used by the F04 import quality gate. |
| `--format human` | Default. For a person at a terminal. |
| `--format github` | Workflow commands, which become annotations on the pull request diff. |
| `--format json` | Machine readable, for workflows that act on findings. |
| `--root <dir>` | Repository root. Found automatically when omitted. |
| `--schema <dir>` | Schema directory. Defaults to `<root>/schema`. |
| `--check` | `format` only. Reports what would change, changes nothing, exits 1. |

## Exit codes

These are a contract. Workflows depend on them, so they do not change meaning.

| Code | Meaning |
|---|---|
| 0 | Nothing blocking. |
| 1 | Blocking findings: errors, or warnings under `--strict`, or files needing formatting under `--check`. |
| 2 | The tool could not run: bad arguments, missing schemas, a path that does not exist. |

Note that 1 and 2 are different on purpose. Exit 1 means the data is wrong. Exit 2 means the
check never happened, which must never be mistaken for a pass.

## Severity

| Level | In CI | When |
|---|---|---|
| **error** | Blocks the merge | The record contradicts itself or the schema. It is wrong, not merely odd. |
| **warning** | Annotated, does not block | Worth a human look, but a real packet could legitimately produce it. |
| **note** | Summary only, never annotated | Useful context. A gap to fill rather than a defect. |

Two levels would force a bad choice: either block a genuine contribution over label rounding, or
never mention it at all. Notes are deliberately kept out of pull request annotations, because a
diff covered in grey markers trains people to ignore all of them.

## Rules

### File and format

| Code | Severity | Rule |
|---|---|---|
| PRN0101 | error | The file is not valid JSON. |
| PRN0102 | warning | The file is not in the canonical format. Run the `format` command. |
| PRN0103 | error | The record is not at the path its key implies, so lookup will never find it. |
| PRN0104 | error | The file could not be read, or passed the schema but could not be loaded into the model. |

### Schema

| Code | Severity | Rule |
|---|---|---|
| PRN0201 | error | The record does not match its JSON Schema. |

Only the innermost failure is reported. A mistake deep in a record also fails every object
containing it, and those outer messages all say the same unhelpful thing.

### Identity

| Code | Severity | Rule |
|---|---|---|
| PRN0301 | error | The barcode fails its check digit, so it is not a real barcode. |
| PRN0302 | error | `gtin` is not `barcode_printed` padded to 14 digits. |
| PRN0303 | warning | `barcode_format` does not match how many digits were printed. |

PRN0302 is the rule that prevents duplicates. A product stored under a non-canonical key is
invisible to a scan of the same packet.

### Nutrition

| Code | Severity | Rule |
|---|---|---|
| PRN0401 | error | Saturated fat is higher than total fat. |
| PRN0402 | error | Sugars are higher than total carbohydrate. |
| PRN0403 | error | Added sugars are higher than total sugars. |
| PRN0404 | error | Trans fat is higher than total fat. |
| PRN0405 | error | Protein, carbohydrate and fat weigh more than the basis they are declared against. |
| PRN0406 | warning | Declared energy does not match what the macronutrients imply. |
| PRN0407 | warning | The kcal and kJ figures do not convert into each other. |
| PRN0408 | error | Two panels declare the same basis. |
| PRN0409 | warning | A per-serving panel has no serving mass, so nothing can be compared with it. |
| PRN0410 | error | A nutrient is both declared and listed as not declared. |
| PRN0411 | warning | More sodium than any food except salt itself could contain. |

**Tolerances.** Mass comparisons allow **0.1 g** of slack, because labels round to one decimal
place and a packet can legitimately print saturated fat as 10.5 g against total fat of 10.4 g.
The mass budget in PRN0405 allows 1 g, since three rounded values drift further than one.

PRN0411 fires above **10,000 mg of sodium per 100 g**, which is roughly 25 g of salt in every
100 g of food. The threshold sits far above anything a normal product reaches, because the rule
exists to catch a decimal point in the wrong place rather than to comment on salty food. It is a
warning, since bouillon and spice blends can legitimately approach it. Products in the `salt`
category are skipped entirely: table salt really is about 39 g of sodium per 100 g, and warning
about a bag of salt for containing salt is how people learn to ignore warnings. The first real import turned up a
cumin powder declaring 40,000 mg, which is more salt than cumin, and it passed only because it
sat exactly on the schema ceiling.

PRN0406 allows **20%**, and is only ever a warning. Indian labels differ in whether fibre is
counted inside carbohydrate, and sugar alcohols and organic acids are not declared at all. It is
a prompt to look, never a statement that the packet is wrong.

These arithmetic rules live in `Prana.Core.Rules.NutritionConsistency` rather than in the
validator, because the app shows the same findings to the user. Nothing is stored in the record:
findings are recomputed from the declared values, so they cannot go stale and a correction takes
effect without a rebuild.

### Provenance

This is where [ADR-0018](planning/DECISIONS.md) is enforced. Without these rules the provenance
map would be documentation rather than a guarantee.

| Code | Severity | Rule |
|---|---|---|
| PRN0501 | error | A published value that no provenance path covers. |
| PRN0502 | error | Provenance points at a source id that is not declared. |
| PRN0503 | warning | A provenance path that does not exist in the record, so it backs nothing. |
| PRN0504 | note | A declared source that nothing references. |
| PRN0505 | error | A verified record resting on low-confidence evidence. |
| PRN0506 | error | A verified record with an unresolved conflict. |

**What needs coverage.** Everything the record claims about the product: `name`, `names`,
`brand`, `category`, `package`, each `nutrition` block, `ingredients_raw`, `ingredients`.

**What does not.** `schema_version`, `gtin`, `barcode_printed`, `barcode_format`, `countries`,
`verification`, `conflicts`, `sources`, `provenance`. These are structural or derived rather than
claims about what is in the packet. When one of them looks wrong, the app asks the user to raise
a correction, which is a better answer than inventing a source for a field nobody sourced.

**How coverage works.** A path covers itself and everything beneath it. One entry for
`nutrition` backs every value in every panel, which is honest, because the whole panel really did
come from one photograph. A field needing its own evidence is named specifically, and the more
precise path wins.

### Ingredients

| Code | Severity | Rule |
|---|---|---|
| PRN0601 | error | A parsed ingredient list with no `ingredients_raw`, which has thrown away its own evidence. |
| PRN0602 | note | No canonical ingredient record exists for a slug yet. |
| PRN0603 | warning | Declared percentages at one level add up to more than 100%. |

PRN0602 is only a note because most raw ingredient text has no canonical record yet. That is a
gap to fill, not a defect to block on.

### References

| Code | Severity | Rule |
|---|---|---|
| PRN0701 | error | The same barcode appears in two files. |
| PRN0702 | warning | No brand record for the referenced brand. |
| PRN0703 | warning | No category record for the referenced category. |
| PRN0704 | warning | No country record for a listed country. |

These are warnings in Phase 1 on purpose. Making them errors would mean every product
contribution also had to create a brand record, turning a two minute contribution into a chore.
They become errors once the reference data is populated in F04.

### Verification

| Code | Severity | Rule |
|---|---|---|
| PRN0801 | error | `last_verified` is in the future. |
| PRN0802 | note | Verified over a year ago, so it is worth re-checking against a current packet. |

## Performance

Measured on a generated tree of 10,000 product records, Release build:

```
10,001 files checked in 10.7s
```

Roughly 900 records per second, so a hundred thousand records takes under two minutes. Pull
requests do not pay that cost: CI validates only the records the pull request changed, and runs
the whole tree on merge and weekly, which is where cross-record rules like duplicate barcode
detection actually matter.

Memory stays flat regardless of tree size. Files are streamed and disposed one at a time, and
only the small facts needed for cross-record rules are retained.

## In CI

[`validate-data.yml`](../.github/workflows/validate-data.yml) runs two jobs.

**Validate records** checks the changed records on a pull request, and the whole tree on merge
and on a weekly schedule. Findings appear as annotations on the exact line of the diff.

The reference records in `data/brands`, `data/categories`, `data/countries` and
`data/ingredients` are always included, even when unchanged. A product validated on its own
cannot resolve its own brand or category, so without them every pull request would carry
warnings that say nothing about the change. They are small, so this costs almost nothing.

Note that `schema/examples/invalid` is never given to this job. Those records exist to be
rejected, so validating them would fail the build on purpose-built bad data. Proving they are
still caught is the second job's work.

**The validator catches what it is meant to** runs every record in `schema/examples/invalid`
individually and fails if any of them is accepted, then checks that everything in
`schema/examples/valid` passes and that all committed records are in the canonical format. A gate
nobody tests is a gate that quietly stops working.

## Adding a rule

1. Add the code to `Rules` in `tools/Prana.Tools.Validator/Diagnostics.cs`. Codes are stable and
   are never reused for a different meaning.
2. Implement it. Arithmetic that the app should also show belongs in `Prana.Core.Rules`.
   Everything else belongs in `tools/Prana.Tools.Validator/Checks`.
3. Add a failing test and a passing test in `tests/Prana.Tools.Validator.Tests/RuleTests.cs`.
   The passing test matters as much: a rule with no case proving when it stays quiet is a rule
   that will eventually fire on good data.
4. Document it in this file.
