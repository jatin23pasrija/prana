# Data Policy

How Prana treats product data, what it promises about accuracy, what it does with your data,
and how to get something corrected.

---

## 1. What this database is

A community-maintained record of what packaged food products declare on their labels, with the
source of each claim recorded alongside it.

It is not an official database. It is not maintained by manufacturers or by any regulator. It
will contain mistakes, and it will contain products whose formulation has changed since the
record was written. The design assumption is that it is imperfect, which is why provenance,
confidence and verification dates are stored with every record instead of being hidden.

**Always trust the packet in your hand over this app.**

## 2. Evidence rules

These are the rules that decide whether a value may be published.

| Rule | Meaning |
|---|---|
| No invention | If the label does not state a value, it is `unknown`. Never a guess, never zero. |
| No silent conversion | Per-serving is never converted to per-100 g without a known serving mass. Every conversion is explicit. |
| No mixing bases | A record never mixes per-100 g and per-serving values without labelling each. |
| Raw text preserved | The exact ingredient wording is kept. Normalisation is added alongside, never in place of it. |
| Source required | Every important field records its source type, location where permitted, retrieval date and confidence. |
| Conflicts lower confidence | Sources that disagree are not averaged. The disagreement is recorded and confidence drops. |
| Presence is not quantity | Knowing an ingredient is listed is not knowing how much. Palm-derived oil is reported as present, not detected, unknown, or confirmed with a quantity. |

## 3. Confidence levels

| Level | Meaning | Handling |
|---|---|---|
| High | Several trustworthy sources agree, or manufacturer and packaging evidence is strong | May be merged automatically if all validation passes |
| Medium | Useful evidence, but some uncertainty or disagreement | Goes to a pull request for review |
| Low | A single weak or indirect source | Not published as verified |
| Unknown | No data available | Stored as unknown |

## 4. Source hierarchy

1. Manufacturer information and the product packaging itself.
2. Official product databases where access and licensing permit.
3. Openly licensed structured datasets.
4. Permitted retailer and product listings.
5. Community-submitted evidence, such as a photo of the packet.

A search result snippet is never evidence on its own.

Every source used must have an approved entry in [DATA_SOURCES.md](DATA_SOURCES.md) recording
its licence, redistribution rights, attribution requirement and retrieval method. Publicly
visible is not the same as reusable.

## 5. Freshness

Formulations and package sizes change. Every record carries a verification date.

| Time since verification | Status shown |
|---|---|
| Under 6 months | Current |
| 6 to 12 months | Review recommended |
| Over 12 months | Possibly outdated |

These are operational prompts to re-check, not statements that a record is wrong.

The date moves only when a record's content changes. An import that re-reads a source and finds
the product unchanged leaves both the record and its date alone. This matters: if every import
refreshed the date, nothing would ever cross the six-month line, and a product last edited
upstream years ago would report itself current forever. See ADR-0032.

## 6. Corrections

Anyone can correct anything.

1. Open a **product correction** issue, or a pull request editing the record directly.
2. Include evidence: a photo of the packet is best, a manufacturer page is good.
3. Automated validation runs on every change.
4. A correction with strong evidence and no conflict can be merged automatically. Anything
   ambiguous is reviewed by a person.
5. The change reaches users on the next catalogue release.

Git history is the audit trail. Every value can be traced to the change that introduced it.

## 7. What the app will not tell you

- That a food is healthy or unhealthy.
- Anything framed as medical, diagnostic or dietary advice.
- A number that was not on a label or in a cited source.
- A precise quantity of an ingredient when only its presence is known.

The app shows comparative indicators, for example higher sugar per 100 g than most products in
the same category, and always explains which rule produced them.

## 8. Your data

- No account is required, and none is offered.
- The grocery list, scan history and preferences are stored on the device only.
- Nothing is uploaded unless you take an explicit action, such as submitting a product request.
- Photos are opt-in, per submission.
- There is no analytics or tracking in the app. If any is ever added, it will be optional,
  off by default, and documented here before it ships.
- A product request you submit is public, because it becomes a GitHub issue. Do not put
  anything private in it.

## 9. Removal requests

If you are a rights holder and believe content in this repository should not be here, open an
issue or email **jeremie23scott@gmail.com** with the specific records and the basis for the
request. Records will be removed while the question is being resolved rather than after.
