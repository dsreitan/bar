# Alternative license models

How do companies with flexible commercial packaging reliably give users the
content they paid for? This document surveys the patterns, maps them onto our
publication/tag model, and recommends a direction. It complements
ARCHITECTURE.md: that document makes the *current* model diagnosable; this one
asks whether a different model would stop producing the incidents at all.

## The root tension

Our current model evaluates the **sale-time abstraction at request time**:
a publication descriptor is a predicate over content metadata, and every page
view re-answers "what did this customer buy?" from raw provisioning data.
That couples two things that want different properties:

- **Selling** wants flexibility: tiny bits, bundles, arbitrary combinations,
  new packaging ideas every season.
- **Serving** wants certainty: a cheap, explainable, cacheable yes/no that
  cannot be wrong in new ways when marketing invents a new bundle.

Every company with a flexible business model hits this tension. The striking
thing is how uniformly they resolve it.

## The industry pattern: sell predicates, serve sets

Across very different domains, the convergent design is to **separate the
purchase model from the entitlement model, and materialize the former into
the latter at provisioning time** — so the runtime check is set membership,
not rule evaluation.

**Steam (Valve).** Users buy *packages* — arbitrary bundles, promo
combinations, tiny DLC bits. At purchase time a package resolves to an
explicit list of application ids attached to the account. The runtime
question is only "does this account hold app id X?" The bundle definition is
never re-evaluated at launch time; changing a package later doesn't silently
change what existing owners have.

**Microsoft 365.** What's sold is a SKU; what's entitled is the SKU's
*service plans*. Assigning a license materializes per-user assigned plans,
and — worth copying — provisioning failures are **visible states** on the
user object that admins can query, not silent drops. A user who "has the
license but not the service" shows up as exactly that.

**Academic and professional publishing (Elsevier, Springer, EBSCO, ProQuest).**
The closest domain to ours. Institutional subscriptions are flexible
(packages, backfiles, pick-and-choose), but what the institution's systems
consume is a **holdings list** — an explicit title-level enumeration of
accessible content (the KBART standard). The whole library-access ecosystem
runs on exchanged explicit lists precisely because "predicate over metadata,
evaluated by the other party" proved undebuggable across organizations.

**Streaming (Netflix and peers).** Content rights are contracts — predicates
over territories and time windows. Nobody evaluates contracts at play time:
rights are compiled ahead of time into per-region availability tables, and
playback consults the table. Rule changes become *diffs* in the compiled
output that can be reviewed and alerted on before users see them.

**Stripe Entitlements and the feature-entitlement vendors.** The product maps
features to products once; when a subscription changes, the platform
materializes the customer's *active entitlements*, and application code
checks that list — never re-deriving access from subscription arithmetic.

**Google Zanzibar (Drive, YouTube, Cloud IAM).** The generalized version:
authorization as explicit stored *relationship tuples* plus a small rewrite
algebra (groups within groups covers bundles-of-bundles natively), with
`check`, `expand` and `explain` as first-class API operations. Open-source
descendants: OpenFGA, SpiceDB, Ory Keto.

The common thread: **flexibility lives at write/provisioning time, where
mistakes are observable and fixable in bulk; the read path is a lookup.**
Our model instead carries the flexibility all the way to the read path,
which is why a provisioning mistake and a code bug look identical at the
moment a teacher can't open chapter one.

## What this looks like for Skolestudio: the entitlement ledger

No change to how licenses are sold or provisioned — publications and
descriptors remain the commercial language. The change is a compilation step:

```
                    (offline, on catalog or registry change)
publication registry ──┐
                       ├─► MATERIALIZER ─► entitlement ledger
content index ─────────┘                   pub id → set of content ids
                                           (versioned, diffable, auditable)

                    (request time)
user → pub ids → union of ledger sets → contentId ∈ set?   O(1), cacheable
```

The materializer's matching engine is **literally the pure core from this
POC** — same `Evaluate`, run in batch over the catalog instead of per
request. One implementation, two execution modes; no second divergent
matcher.

What this buys, in terms of our actual incidents:

- **"Code or data?" becomes a lookup.** Does row (540200, contentX) exist?
  If yes and the user still can't reach it, the bug is in the serving path
  (code). If no, the materialization log for pub 540200 says exactly why not
  (data). The archaeology session becomes a query.
- **Data defects become pipeline alerts instead of support tickets.** A
  descriptor that matches zero content, a retag that silently removes 132
  items from a publication, a license referencing an unknown publication id —
  all of these surface as materialization diffs/alerts *before* a user hits
  them. The linter's checks graduate from batch job to pipeline gate.
- **The trace gets even better.** "You have access via pub 540200,
  materialized 2026-07-06T03:00 from registry version N" is a statement
  support can act on and sales can verify.
- **Performance stops depending on descriptor complexity.** Membership is
  O(1) however creative the bundling gets; today every new key in a
  descriptor adds work to every request.

Costs, honestly:

- **Staleness and invalidation.** New or retagged content must trigger
  recompute (event-driven, or nightly plus an on-demand hook). There is a
  window where the ledger lags the catalog. Mitigation: check the ledger
  first and fall back to live evaluation for content newer than the last
  materialization — same engine, so the answers agree by construction.
- **Ledger size.** Rows ≈ publications × average matched content. With
  grants targeting content groups / learning components rather than atoms,
  this is small; even at content-item granularity, 10⁴ publications ×
  10³ items is a modest table by any database's standard.
- **Operational surface.** A new batch job with monitoring. This is real but
  it is *observable* work replacing invisible per-request risk.

## Migration path: shadow mode, zero user risk

1. **Build the materializer** from the POC core; run it nightly against the
   real registry and content index. Ship the defect alerts first — they have
   value before anything serves from the ledger.
2. **Parity check.** Keep serving from the live evaluator, but also consult
   the ledger and log every disagreement. Each mismatch is a staleness case
   or a bug — found without user impact. (This doubles as the largest-scale
   test the matching code has ever had: the whole catalog × real licenses.)
3. **Flip reads** to the ledger once parity holds for a few weeks. Keep live
   evaluation for the why endpoint and the freshness fallback.
4. The reasons/telemetry keep working — the ledger stores provenance
   (which publication, which rule version), so traces stay explainable.

## Constraint: we don't own the license system or the CMS

The BFF team controls neither the system issuing licenses nor the CMS holding
content — so any design requiring bulk nightly dumps of "everything" is
suspect. Three things keep this constraint from blocking the direction:

- **The request-time POC needs nothing new.** Its three inputs — the user's
  publication ids, the publication registry, and one content item's tags —
  are exactly what the BFF already fetches per request today.
- **The scale is smaller than it looks.** Materialization is per
  *publication* (the registry: small, mostly static, already cached), never
  per user license — thousands of licenses are irrelevant because a license
  is just a list of publication ids resolved at request time. And the
  content side needs only each item's license-relevant tags: a few short
  strings per item, tens of megabytes for hundreds of thousands of items.
  Compute is a non-issue — the pure core evaluates millions of checks per
  second, so publications × catalog is minutes on one machine.
- **The content-tag index can be built without CMS cooperation.** In order
  of increasing upstream involvement: (1) accumulate it from live traffic —
  every request the BFF serves teaches it one content item's tags, so the
  index converges on all content anyone actually uses; (2) ask the CMS team
  for a tags-only export or search endpoint (the KBART-holdings ask — small
  and standard); (3) content-changed webhooks if they exist. Option 1 also
  powers the linter's dead-grant check with zero dependencies: the observed
  tag vocabulary is the content vocabulary.

Note the linter's highest-value checks — invalid descriptor syntax
(fail-open!), unknown keys, empty publications, license references to
unknown publication ids — need **only** the publication registry and license
data the BFF already receives. They can run today.

## When ReBAC (Zanzibar-style) is the better answer

If the roadmap grows *relations* — teacher shares a resource with a class,
school-level licenses inherited by staff, trial periods, per-student
differentiated access — then entitlements stop being "user × catalog" and
become a graph. That is the moment to adopt OpenFGA or SpiceDB rather than
extending the ledger: relationship tuples model
`student —memberOf→ class —taughtBy→ teacher —owns→ bundle —contains→ content`
natively, with check/expand/explain built in.

For pure catalog entitlements — which is what we have — ReBAC is more
machinery than the problem needs; a materialized table gives the same
debuggability with far less infrastructure.

## Comparison

| | Current: predicates at request time | Entitlement ledger (recommended) | ReBAC (OpenFGA/SpiceDB) |
|---|---|---|---|
| Runtime check | rule evaluation per request | set membership | graph check (ms, external service) |
| "Code or data?" | indistinguishable at runtime (this POC adds the trace to cope) | a lookup + materialization log | explain/expand APIs |
| Data defect discovery | when a user complains | pipeline alert on materialize | on tuple write |
| Selling flexibility | unchanged | unchanged (compile step absorbs it) | re-modeled as relations |
| New infra | none | one batch job + one table | authz service + tuple sync |
| Fit | today | mid-term target | if sharing/relations arrive |

## Recommendation

- **Now:** land this POC's direction on the current model — pure core, trace,
  scenario corpus, linter. It pays for itself on the next incident and it is
  the prerequisite for everything below (the core *is* the materializer).
- **Mid-term:** build the materializer and run shadow-mode parity against
  production decisions; promote the linter to a pipeline gate; then serve
  from the ledger.
- **Long-term:** adopt ReBAC only when relationship features (sharing,
  school hierarchies, per-student rules) justify it — not for catalog
  entitlements alone.

The one-sentence version: keep selling the mess, stop serving it —
**compile the mess into sets before the user ever asks.**
