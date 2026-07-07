# A license system from scratch

If we could redesign how we sell and grant access today — no publications, no
descriptors, no legacy — what would we build? This document proposes an
architecture for the model under real consideration (**pay per use**), lays
out four other packaging models, and argues for one design decision that
matters more than the choice of model itself.

Context that shapes everything: we sell to **schools and municipalities**
(Norwegian public sector). That means procurement rules, annual budget cycles,
a hard requirement for **predictable costs**, Feide as the identity fabric
(user → school → municipality is given, reliably), and GDPR with minors —
per-student usage data is radioactive and must be minimized by design.

## The one decision that outlives every pricing model

Pricing models come and go — bundles this year, pay-per-use pilot next year,
some hybrid after the next procurement round. The architecture mistake in the
current system is not the descriptor syntax; it is that **the commercial
model is baked into the access decision**. Change how we sell and you change
how the doorman thinks.

The greenfield rule: build five stable primitives, and make the pricing model
a *policy* configured on top of them:

```
1. IDENTITY     Feide: user → school → municipality (we don't build this, we consume it)
2. CATALOG      stable content ids + curated product ids (a product = an explicit,
                versioned set of content ids — never a metadata predicate)
3. AGREEMENT    what an organization bought: product refs + commercial terms
                (model, period, caps, seats) — the only place pricing lives
4. ENTITLEMENT  compiled from agreements: explicit (org|user, product) rows,
                versioned, with provenance — the only thing the read path sees
5. USAGE        append-only event ledger of content opens — metered ALWAYS,
                billed only when the agreement's model says so
```

Every model below is a different way of filling in AGREEMENT and a different
consumer of USAGE. None of them changes the read path: *"is there an
entitlement row covering this user and this content's product?"* — O(1),
explainable, and boring, exactly like the ledger recommendation in
ALTERNATIVE-LICENSE-MODELS.md. The current system's core defect (predicates
evaluated per request) never gets rebuilt.

Two catalog rules worth writing in stone:

- **Products are explicit lists, not queries.** "Refleks naturfag 8–10" is a
  versioned set of content ids maintained editorially. Adding content to a
  product is a reviewable diff, not a side effect of tagging.
- **Content ids are forever.** Retagging, restructuring, new editions — none
  of it may silently change what an existing agreement covers; a new edition
  is a new product version with an explicit upgrade policy on the agreement.

## Model A — pay per use (the candidate)

**The pitch:** schools stop guessing in April what they need in September.
Everything is open to every Feide user in a customer organization; the
municipality pays for what its schools actually used. Sales friction drops to
zero ("just try it — you only pay if teachers actually use it"), and the
catalog's long tail earns its keep.

**The profound shift:** access control becomes almost trivial — *"is this
user's organization a customer at all?"* — and the hard correctness problem
moves to **metering and billing**. Today's bug class ("correct license, no
access") largely disappears; the new bug class is "we invoiced wrongly",
which is worse politically and must be engineered against from day one.

### Architecture

```
                                  ┌──────────────────────────────────────────┐
 React app ──► BFF ──► allow if org has an agreement (cheap, cached, boring)  │
                │                                                             │
                └─► USAGE EVENT (at "first meaningful use")                   │
                     { eventId, userId(pseud.), orgId, contentId, ts }        │
                          │  idempotent append                                │
                          ▼                                                   │
                 ┌────────────────┐    nightly/continuous    ┌─────────────┐  │
                 │  USAGE LEDGER  │ ───────────────────────► │   RATING    │  │
                 │  (append-only, │   pure function:         │  events ×   │  │
                 │   immutable)   │   (events, price book,   │  price book │  │
                 └────────────────┘    period) → charges     └──────┬──────┘  │
                          │                                        ▼          │
                          │                              ┌──────────────────┐ │
                          │                              │ BUDGET GUARD     │◄┘
                          │                              │ caps, alerts,    │
                          │                              │ degrade-to-free  │
                          ▼                              └────────┬─────────┘
                 ┌────────────────┐                               ▼
                 │ ADMIN PORTAL   │                     ┌──────────────────┐
                 │ live usage &   │                     │ INVOICING        │
                 │ cost per school│                     │ per municipality │
                 └────────────────┘                     │ per period       │
                                                        └──────────────────┘
```

**The billable unit is the critical product decision, not a detail.** Charge
per click and you punish curiosity, invite disputes over prefetching and
bots, and teach teachers to hoard PDFs. The robust unit for this domain:

> **unique (user, product) per period** — "a student used Multi in March" —
> capped per user per period, so cost has a hard ceiling by construction.

Properties: idempotent (reloads and flaky wifi cost nothing extra), fraud-
resistant (a bot loop costs the same as one open), explainable on an invoice,
and privacy-friendly (it aggregates naturally). Fire the event on *first
meaningful use* (opened and interacted, not merely listed in search results).

**Engineering rules, carried over from this repo's philosophy:**

- The usage ledger is **append-only and immutable**; corrections are
  compensating events, never edits. Every eventId is idempotent — the same
  open reported twice lands once.
- **Rating is a pure, versioned function**: `(events, price book, period) →
  charges`. Same inputs, same invoice, forever. A billing dispute is resolved
  by *replay*, exactly like an access dispute is resolved by the why
  endpoint today. Price books are versioned data, never code.
- **Meter from day one, bill later.** Run the whole pipeline in shadow mode
  producing invoices nobody pays, next to the existing model, until the
  numbers survive a full school year's weirdness (exam season spikes,
  Christmas, municipality mergers).

**Public-sector reality — the two hard constraints:**

1. **Unpredictable cost is procurement poison.** Pure pay-per-use fails many
   tender processes outright. The sellable variant is **capped pay-per-use**:
   a municipality commits to a floor, the budget guard enforces a ceiling,
   and between them usage decides. The cap turns "scary variable cost" into
   "we'll never pay more than X" — and the floor protects our revenue
   planning. (This is how cloud vendors sell to the same buyers: committed
   spend + metered usage.)
2. **GDPR with minors.** Bill at *school* aggregation; pseudonymize user ids
   in the ledger with rotating keys; keep raw events only as long as the
   dispute window requires; give schools the usage dashboard (transparency
   is a feature, and it is also the DPA story). Never let invoice line items
   identify a student.

**What gets harder, honestly:** revenue recognition and forecasting; author/
royalty models (usage-based royalties follow naturally, but that's a
contract renegotiation); editorial incentives (usage-visible content will
shape what gets made — decide whether that's a feature); and sales
compensation. None of these are engineering problems, all of them will land
on the roadmap.

## Model B — site license ("Spotify for schools")

Whole catalog, per school or municipality, price by enrollment size (Feide
gives us truthful headcounts). The simplest possible read path: *is the org a
customer?* No entitlement granularity at all.

- Best buyer experience and the strongest tender story: one line item,
  everything included, zero mid-year surprises.
- Internally, **usage metering still runs** — not for billing, but for
  royalty distribution, editorial insight, and renewal negotiation ("your
  schools used 71% of the catalog"). This is Spotify's actual shape: flat
  price outside, usage-metered distribution inside.
- Risk: leaves money on the table with heavy users, prices out small/poor
  municipalities unless tiered. Mitigate with size bands rather than pure
  headcount multiplication.

## Model C — seat subscriptions per product

The classic: an agreement grants product P for N students at school S; seats
auto-assign on first Feide login (never make teachers manage seat lists —
that UX kills adoption). Predictable for buyers, granular for us,
straightforward migration from today's publications (a publication ≈ a
product, a license ≈ seats).

The known failure mode is **seat-count friction**: class sizes change in
August, and nothing sours a renewal like a compliance email about 31 students
on a 30-seat license. Soften with truthful auto-counts from Feide and
generous tolerance bands (bill on the September count, ignore ±10%).

## Model D — prepaid credits (budget-safe pay-per-use)

The municipality buys a credit pool in the spring budget round; through the
year, schools (or teachers) spend credits to unlock products — each unlock
materializes an entitlement for the rest of the school year. Unspent credits
carry over one year.

- Fits public-sector budgeting perfectly: the cost is fixed at purchase
  time, but the *allocation* stays flexible all year — the delegation
  question ("which content do we need?") moves from procurement officers to
  the teachers who actually know.
- Architecturally it is pay-per-use with the budget guard moved to the front:
  same catalog, same entitlement ledger, no billing pipeline risk at all
  (money changed hands up front).
- Watch out for: credits hoarded until March then dumped; pricing the credit
  cost of products (that's a full price-book problem in disguise).

## Model E — curated packs (the modernized status quo)

Sell explicit, versioned product bundles — "Barnetrinn complete", "Realfag
8–10" — mapped to content-id sets in the catalog. This is today's commercial
model with the predicate layer amputated: no descriptors, no tag matching,
just products whose contents are reviewable lists. Cheapest to migrate to,
least strategic change; think of it as the floor every other model stands on,
because *every* model needs the catalog and products anyway.

## Comparison

| | A. Pay per use (capped) | B. Site license | C. Seats | D. Credits | E. Packs |
|---|---|---|---|---|---|
| Cost predictability for buyer | medium (cap helps) | **best** | good | **best** | good |
| Matches value delivered | **best** | poor | medium | good | medium |
| Tender/procurement fit | hard without caps | **easiest** | easy | **easy + flexible** | easy |
| Read-path complexity | trivial | **trivial** | low | low | low |
| New engineering risk | **billing pipeline** | none | seat counts | credit ledger | none |
| Where incidents land | invoices | renewals | seat disputes | unlock UX | catalog curation |
| Sales friction | **lowest** | low | medium | low | medium |
| Migration from today | big | medium | **small** | medium | **smallest** |

## Recommendation

Don't pick one model — **pick the primitives, then run two models on them.**

1. **Build the spine regardless**: catalog with explicit versioned products,
   agreements, compiled entitlement ledger, and the usage ledger with
   metering on from day one. Every model above is configuration on this
   spine, and the metering data is what lets us *negotiate* future models
   from knowledge instead of guessing. (The pure evaluator in this repo is
   the compiler for migrating existing publications onto it.)
2. **Lead commercially with B or D** — site licenses for large
   municipalities, credits for smaller ones. Both are procurement-friendly
   *today* and both generate the usage data pay-per-use needs.
3. **Pilot A (capped pay-per-use) in shadow mode**: rate real usage, produce
   ghost invoices, compare against what the same customers pay now. If the
   ghost invoices look sellable after a full school year, we have the
   pipeline, the caps, and the evidence — and the pilot cost us a batch job,
   not a re-platform.

The one-sentence version: **meter everything, gate almost nothing, and let
pricing be a policy on top of a boring, explicit entitlement ledger — so the
next commercial experiment is a config change, not a rewrite.**
