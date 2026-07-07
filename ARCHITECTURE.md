# BFF Authorization POC — Architecture & Direction

## The problem this POC exists to solve

The BFF sits between a React frontend and platform microservices. Its hardest
responsibility is entitlement: deciding whether a user's license scope grants
access to a piece of content, where

- licenses are extremely flexible — users buy tiny bits, bundles, and arbitrary
  combinations of both, and
- content is tagged with metadata that licenses reference.

The recurring, expensive bug class: **a user with a correct license can't reach
their content, and we can't tell whether the bug is in the matching code or in
the license setup (data).** Every incident starts with an archaeology session.

The goal of this POC is therefore *not* "write an authz layer." It is:

> Find an architecture where the code-vs-data question is answerable in
> minutes, and where the matching logic is testable exhaustively enough that
> "it's probably the code" stops being a plausible default.

## Core decision 1: a pure, deterministic decision core

All matching logic lives in one place, as a **pure function** with no I/O:

```
Decision Evaluate(LicenseSnapshot license, ContentDescriptor content, EvaluationContext ctx)
```

- No HTTP calls, no database, no `DateTime.Now` (time comes in via `ctx`),
  no DI container, no config lookups. Inputs in, decision out.
- The BFF's API layer and HTTP clients are *adapters*: they fetch the license
  snapshot and content metadata, call the core, and shape the response. They
  contain zero matching logic.
- The core is versioned. Every decision records which rule version produced it.

Why this is the load-bearing decision:

1. **Testability.** A pure function over two data structures can be tested
   with tables, generated inputs, and oracles — no mocks, no test containers,
   millions of cases per second.
2. **Reproducibility.** Any production incident can be replayed exactly:
   capture the two inputs, re-run the function, get the same answer. If the
   inputs were right and the answer is wrong → code bug. If the inputs were
   wrong → data bug. The ambiguity that plagues us today comes from decisions
   being smeared across HTTP calls, caches, and clock reads.
3. **A cheap seam.** The frontend API, the microservice clients, caching,
   and the license data model can all evolve without touching the logic that
   actually causes incidents.

### The model: normalize both sides into a shared vocabulary

Most of the "mess" is vocabulary mismatch: licenses reference content in one
set of terms, content is tagged in another, and the mapping lives implicitly
in code. The POC should make the vocabulary explicit:

- Define the **access dimensions** (e.g. product family, subject, level,
  format, edition — whatever the real axes are).
- A **content descriptor** is a point (or set of tags) in that dimension space.
- A **license** normalizes to a set of **grants**, each grant being a predicate
  over the dimension space (a bundle is just a named set of grants — expand it
  at normalization time, keep the provenance).
- **Access = ∃ grant in the normalized license that covers the content
  descriptor.** Set membership, nothing cleverer.

Two pure steps, separately testable:

```
Normalize:  RawLicense → Grant[]        (bundle expansion, defaults, ranges)
Match:      Grant[] × ContentDescriptor → Decision
```

Splitting these matters because in practice bugs cluster in normalization
(bundle X was supposed to include Y) rather than matching. Separate functions
mean separate test suites and a decision trace that says *which step* went
wrong.

## Core decision 2: explanations are the output, allow/deny is a projection

The core never returns a bare boolean. It returns a **decision trace**:

- final verdict (allow / deny),
- for allow: which grant matched, which license line/bundle that grant came
  from (provenance through normalization), which dimensions it matched on,
- for deny: for *each* grant, which dimension failed and with what values
  ("grant from bundle B covers subject=math but content is subject=physics"),
- rule version and the exact input snapshots (or hashes of them).

This is the direct answer to "code or license setup?":

- Trace says "no grant references tag X, content requires tag X" and a human
  reading the license agrees the customer *should* have X → **license setup
  bug** (or sales/provisioning bug). Ticket goes to the right team with
  evidence attached.
- Trace contradicts a human reading of the same data ("this grant obviously
  covers this content, the evaluator says no") → **code bug**, and the two
  input snapshots *are* the failing regression test. Paste them into the test
  corpus, fix, done.

Expose this via an internal diagnostic endpoint on the BFF:

```
GET /internal/entitlements/why?userId=...&contentId=...
```

which fetches live inputs, runs the core, and returns the full trace. This
endpoint is the support tool that kills the archaeology sessions. (Internal
only, audit-logged — it reveals license structure.)

## Core decision 3: the testing strategy (the actual scope of this POC)

The pyramid is deliberately bottom-heavy. Nearly all value is in tests of the
pure core; the API layer gets a thin smoke layer.

### 1. Scenario corpus — executable specification (highest value)

A folder of human-readable scenario files (YAML/JSON), each one:

```
name: "single-title buyer can read that title but not the bundle sibling"
license:  { ...raw license as the license service would return it... }
content:  { ...content descriptor... }
expect:   deny
because:  "grant covers only edition=2023"
```

One data-driven test runner executes the whole folder. Properties of this
corpus:

- **Readable by non-developers.** The people who *configure* licenses can read,
  review, and contribute cases. This is where the tacit knowledge about how
  licensing is *supposed* to work becomes executable instead of tribal.
- **Every production incident becomes a scenario file.** Capture the real
  license snapshot + content descriptor from the `why` endpoint, anonymize,
  commit. The corpus grows into a regression shield shaped exactly like the
  bugs we actually get.
- The `because` field is asserted against the decision trace, not just the
  verdict — so explanations are regression-tested too.

### 2. Property-based tests — the flexibility mess, tamed

Example-based tests can't cover "users combine everything." Properties can.
Use FsCheck or CsCheck with generators for random licenses and content, then
assert invariants that must hold for *any* combination:

- **Monotonicity:** adding a grant/bundle to a license never revokes access
  to anything previously accessible. (This is the classic source of "I bought
  more and now less works" bugs.)
- **Bundle equivalence:** a license containing bundle B grants access to
  exactly the union of what B's parts grant individually.
- **Empty license grants nothing; irrelevant grants change nothing.**
- **Order independence:** grant order in the license never affects outcomes.
- **Trace soundness:** if the verdict is allow, the grant named in the trace,
  evaluated alone, also allows. If deny, no grant evaluated alone allows.

The last property is gold: it mechanically ties explanations to reality.

### 3. Oracle (model-based) testing

Keep a second implementation of `Match` that is brutally naive — nested loops,
no optimization, small enough to verify by eye. Property tests assert the real
implementation always agrees with the oracle over generated inputs. When the
real core later grows caching, precomputed indexes, or clever short-circuits
(it will), this is what keeps it honest.

### 4. License linter — testing the *data* half

The same dimension vocabulary lets us validate license setups themselves,
independent of any user request:

- grants referencing metadata tags/values that no content in the catalog has,
- empty or self-contradictory grants (predicates satisfiable by nothing),
- bundles that expand to nothing, dangling bundle references,
- (warning-level) fully overlapping grants — usually a provisioning smell.

Run it as a batch job over real license data and in CI over fixtures. This is
the other half of the code-vs-data split: the linter catches data bugs
*before* a user does, the core tests catch code bugs before deploy.

### 5. Contract tests on the HTTP clients

Half of "the data was wrong" incidents are really "the data didn't mean what
we assumed." For each upstream (license service, content metadata service),
keep verified stub responses (snapshot real responses, or Pact if the owning
teams will play along) and test that the anti-corruption mapping from their
DTOs into `LicenseSnapshot`/`ContentDescriptor` is lossless for every field
the core reads. The core's tests then trust its inputs; the contract tests
own the boundary.

### 6. Thin API integration tests

`WebApplicationFactory` + stubbed HTTP clients: auth token in → correct
upstream calls → core invoked with correctly mapped inputs → 200/403 with the
right shape out. A handful of tests. They verify wiring, never rules — any
test asserting a licensing rule at this layer is in the wrong layer.

## What the POC repo should contain

```
src/
  Bff.Entitlements/          # THE pure core: model, Normalize, Match, DecisionTrace
  Bff.Entitlements.Oracle/   # naive reference implementation
  Bff.Api/                   # minimal API: /content endpoints + /internal/entitlements/why
  Bff.Clients/               # typed HTTP clients + DTO→domain mapping (stub upstreams for now)
tests/
  Bff.Entitlements.Tests/    # scenario-corpus runner + property tests vs oracle
  scenarios/                 # the YAML/JSON corpus — the crown jewel
  Bff.Api.Tests/             # thin WebApplicationFactory smoke tests
tools/
  LicenseLinter/             # console app over license fixtures
```

Suggested build order (each step is demoable):

1. Dimension vocabulary + `LicenseSnapshot`/`ContentDescriptor` model, with
   3–4 realistic sample licenses of increasing nastiness (single item, bundle,
   bundle + single-item overlap, the messiest real combination we've seen).
2. `Normalize` + `Match` + `DecisionTrace`, driven by a starter scenario
   corpus (~20 cases including known historical bugs).
3. Property tests + oracle.
4. `why` endpoint with stubbed clients — the support-tool demo.
5. License linter over the sample licenses (plant a broken one).

## Success criteria

The POC succeeds if, for a re-enacted historical incident ("user U can't open
content C despite valid license"):

1. One call to `/internal/entitlements/why` yields a verdict and a
   human-readable reason in under a minute.
2. The trace makes it unambiguous whether it's code or data.
3. If code: the captured inputs drop into `tests/scenarios/` as a failing test
   with no additional harness work.
4. If data: the license linter (or the trace) states the defect in
   provisioning terms someone outside the dev team can act on.

## Trade-offs considered

- **External policy engine (OPA/Rego, Cerbos, Casbin)** — rejected for the
  core. These shine for *role/permission* rules shared across services. Our
  problem is data-driven entitlement matching over a domain-specific dimension
  space; the rules are stable, the *data* is wild. A pure C# core gives
  type-safe modeling, better explanations, and property testing in the same
  language as the team. Revisit only if many services need to evaluate the
  same rules and can't call the BFF/an entitlement service.
- **Testing in the license service instead of the BFF** — the matching logic
  should ultimately live wherever entitlement decisions are made for all
  clients (possibly a dedicated service). The POC deliberately doesn't decide
  that; the pure core is extractable by construction. What must *not* happen
  is a second, divergent implementation of matching in another service — the
  scenario corpus should be treated as the portable spec if the core moves.
- **Caching decisions** — out of scope, but note: cache *inputs* (license
  snapshots, content metadata), never verdicts, or replayability and the `why`
  endpoint silently lie. If verdict caching is ever needed for latency, key it
  on input hashes + rule version.
