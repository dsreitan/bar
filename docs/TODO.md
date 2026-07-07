# TODO

What we can and should do **now**, before leadership decides on any
commercial model — ordered so that everything in the first two sections is
useful under *every* possible decision (including "no change"). Sizes:
S = days, M = weeks, L = quarter.

## 1. No-regret: kill the current bug class (needs nobody's permission)

These pay for themselves on the next "correct license, no access" incident
and are prerequisites for everything else. Pattern code for all of it lives
in this repo.

- [ ] **(M) Port the pure decision core into the production BFF.**
      Restructure `TagLicenseService` so resolve → parse → transport → match
      is one pure, deterministic path returning a full `DecisionTrace`
      (per-publication, per-key comparisons + assembly losses). Semantics
      pinned by this repo's spec table — no behavior change.
- [ ] **(S) Surface the silent drops.** Dropped publication ids (license →
      registry misses), invalid-syntax fail-opens, and dropped label keys
      become trace entries and App Insights dimensions, not nothing.
      Alert on: any fail-open grant, any dropped publication id.
- [ ] **(S) Ship the `why` endpoint** (internal, audit-logged) in the
      production BFF. This is the support tool; it ends archaeology sessions.
- [ ] **(S) Run the linter's registry-only checks against production data**:
      invalid descriptor syntax (each one currently grants the whole catalog,
      silently), unknown descriptor keys, empty publications, and user
      licenses referencing unknown publication ids. Needs only data the BFF
      already receives. Triage findings with the license setup team.
- [ ] **(S) Wire the linter into CI/cron** so new data defects alert within
      a day instead of surfacing as support tickets.
- [ ] **(M) Start the scenario corpus in the production repo.** Seed from
      existing tests; decouple rule tests from virtual-publication snapshots
      (code tests vs. data tests, clearly separated). Adopt the rule: every
      incident becomes a scenario file before the ticket closes.
- [ ] **(S) Property tests vs. the oracle** in the production repo —
      port from this repo; they found a real spec subtlety here on day one.

## 2. No-regret: start collecting the data every future model needs

Whatever leadership picks — pay-per-use, logins, site licenses, credits, or
status quo — these are the inputs. Every month not collecting is a month of
evidence lost.

- [ ] **(S) Traffic-accumulated content-tag index.** Log the distinct
      (contentId → license-relevant tags) pairs the BFF already serves.
      Unlocks the linter's dead-grant check and license preview without any
      CMS dependency.
- [ ] **(M) Shadow usage metering.** Emit an idempotent daily-active-use
      event: (pseudonymized user, org, product/learning material, date) on
      first meaningful content open per day. Append-only store, school-level
      aggregation. This single stream back-tests Model A, A′, B renewal
      arguments, and royalty questions. **Involve the DPO before the first
      event is stored** — minors' data; design for aggregation + short raw
      retention from day one.
- [ ] **(S) Back-test the login model with existing data.** Last school
      year's login history × candidate price points vs. actual invoices per
      municipality; publish the spread internally. Free (the data exists),
      and it converts the A′ discussion from opinions to numbers before any
      pilot. The projector/portal distortions in
      GREENFIELD-LICENSE-SYSTEM.md § Model A′ frame what to look for.
- [ ] **(S) Ship the preview endpoint to the license setup team.** Dry-run a
      descriptor before provisioning: what does it unlock, is it dead, is it
      fail-open. Uses the traffic-accumulated index as its catalog.

## 3. Needs other teams — start the conversations now (calendar time, not dev time)

- [ ] **(talk) CMS team:** a tags-only export or search endpoint (the
      KBART-style ask — small), and content-changed webhooks if they exist.
      Not blocking (traffic accumulation covers us) but upgrades preview and
      enables the full materializer.
- [ ] **(talk) License system owners:** visible provisioning-failure states
      (the Microsoft 365 pattern) — a license referencing an unknown
      publication should be *their* alert too, not just our trace entry.
      Share linter findings as the opener.
- [ ] **(talk) Catalog/editorial:** the "products are explicit versioned
      lists, not tag queries" principle. This is the spine of every
      greenfield model; the earlier editorial owns product definitions, the
      cheaper every future becomes.
- [ ] **(talk) DPO/legal:** usage metering of minors — aggregation level,
      pseudonymization, retention window, and what may ever appear on an
      invoice. Needed before section 2's metering leaves shadow mode.
- [ ] **(talk) Finance/royalties:** if any usage-based model advances,
      royalty contracts follow usage data. Long lead time; open it early.

## 4. Decision-gated (do NOT start until leadership picks a direction)

- [ ] Entitlement ledger in shadow mode + parity logging against live
      decisions (the ALTERNATIVE-LICENSE-MODELS.md migration, step 2).
- [ ] Rating engine + price book as pure versioned functions; ghost invoices
      for a full school year (Model A/A′ pilot).
- [ ] Budget guard: caps, alerts, degrade behavior (required for any
      pay-per-use variant sold to municipalities).
- [ ] Agreement/product data model in production (greenfield spine),
      migration of publications → products via the materializer.
- [ ] School-admin usage dashboard (transparency feature; also the DPA story).

## Standing rules (adopt immediately, cost ≈ 0)

1. Every access incident gets a `why` trace attached to the ticket and a
   scenario file before it closes.
2. Every new publication is previewed before provisioning; invalid syntax
   never ships.
3. No entitlement logic outside the pure core — the matcher exists exactly
   once.
4. Meter first, decide later: no commercial discussion without the usage
   data to test it against.
