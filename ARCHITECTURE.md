# BFF Authorization POC — Architecture & Direction

Grounded in the real thing: `TagLicenseService` ("Lisensmottor'n") and its
tests, copied into `reference/` for study. Terms used below are the real ones:
**publications** (what a user's license resolves to, each with a *descriptor*
like `LearningMaterial=refleks;Subject=naturfag`) and **content tags** (a
`TagSet`: `TagType` → list of string values, e.g. `LearningMaterial=multi`).

## The problem this POC exists to solve

The recurring, expensive bug class: **a user with a correct license can't
reach their content, and we can't tell whether the bug is in the matching code
or in the license/publication setup.** Every incident starts with an
archaeology session.

The goal is *not* "rewrite the authz layer." It is:

> Find an architecture where the code-vs-data question is answerable in
> minutes, and where the matching logic is testable exhaustively enough that
> "it's probably the code" stops being a plausible default.

## Step 0: the current semantics, written down as a spec

Half the debugging pain is that the semantics live implicitly in loop
structure. Extracted from `TagLicenseService`, the actual rules are:

**Open-content gates (checked first, in order):**

| # | Rule | Result |
|---|------|--------|
| 1 | Feature flag `LicenseCheckNone` on | open |
| 2 | Content has no tag values at all | open |
| 3 | Content carries a free tag (e.g. `Pricing=free`, `LearningMaterial=frittstaaende`) | open |
| 4 | Content has none of `LearningMaterial`, `LearningComponent`, `Isbn` | open |

**Per-publication match (first success wins across the user's publications):**

| # | Rule | Result |
|---|------|--------|
| 5 | Descriptor syntax invalid | **access granted** (fail-open, logged) |
| 6 | Descriptor parses to zero tags | no match |
| 7 | Let **K = keys(publication) ∩ keys(content)**. If K = ∅ | no match |
| 8 | Access iff for **every** key in K, some content value is hierarchically covered by some publication value | match / no match |

**Value matching (rule 8 details):**

- Hierarchical with `/` as boundary: publication `veien-til-toppidrett`
  covers content `veien-til-toppidrett/oevelser`, but `salaby` does **not**
  cover `salaby-skole`. Child publication never covers parent content.
- Null content values → match (fail-open). Null publication values → no match.
- Descriptor values are normalized on parse (lowercased, whitespace and
  invalid characters stripped).

Rules 7+8 compress to one sentence — *match on the key intersection; require
the intersection to be non-empty; every intersected key must value-match* —
and that sentence is the whole matcher. Two consequences worth stating out
loud, because they are policy decisions hiding in code:

- Publication keys the content doesn't carry are ignored, and content keys
  the publication doesn't constrain are ignored. Constraints only bind on the
  intersection.
- **Any single-key overlap suffices.** A publication `Subject=naturfag`
  grants access to every piece of content tagged `Subject=naturfag`,
  regardless of which learning material it belongs to. Broad low-specificity
  descriptors are a very large lever.

This spec — as a document *and* as the oracle implementation (below) — is a
first-class deliverable of the POC. It's also the artifact to review with the
people who configure licenses: every row is a decision someone should own.

## Step 1: the suspect list — where "correct license, no access" actually hides

Reading the service, the matcher itself (`CheckUserPublicationAccess`,
`IsMatchingTags`) is already **static and pure** — the easy part is done. The
bugs live in the impure assembly around it, and almost all of them are
*silent*:

1. **Unknown publication IDs are silently dropped.**
   `user.Publications.Where(skolestudioPublications.ContainsKey)` — a user
   whose license references a publication the registry doesn't know behaves
   exactly like a user with no license. No log, no trace entry, no error.
   This is the top suspect for the historical bug class and the single
   highest-value thing to make visible.
2. **Normalization is smeared across parsing paths.** Descriptor parsing
   lowercases and strips values; the content-tag side and `IsMatchingTags`
   (ordinal compare) do not. The existing tests hint at an inconsistency:
   `GetTagsByKey` lowercases (`TEST=UPPER,CASE` → `upper, case`), yet a
   descriptor value `Salaby` is expected to match content value `Salaby`
   through `GetDescriptorTags().ToTagSet()`. Whether or not that's a live
   bug, two parse paths with different normalization is exactly the kind of
   divergence that produces "the values look identical but don't match."
3. **Invalid descriptor syntax fails open** (rule 5): a malformed descriptor
   grants access to *everything*. Meanwhile a well-formed-but-wrong
   descriptor fails closed. So the failure gradient is inverted: the worse
   the data, the more access. At minimum this belongs in the linter; whether
   it should stay fail-open is a policy question to surface.
4. **Key-vocabulary coupling.** A descriptor key with no `TagType`
   counterpart silently vanishes on parse (`product=pilot` → empty
   publication), and `ToLicenseTagSet` silently drops content label keys with
   no `TagType` counterpart (`EducationalRole`, `SeasonalEvent`). A typo'd or
   newly-introduced key on either side disappears without a sound.
5. **Rule 4 makes under-tagged content free for everyone.** Content that
   loses its `LearningMaterial` tag in an editorial mishap becomes open —
   the inverse bug (over-grant), equally silent.
6. **Reasons are too coarse to diagnose.** `FAIL_PublicationsNoMatchingValues`
   doesn't say which publication, which key, or which values were compared.
   The `publicationChecks` list is the right skeleton; it needs per-key flesh.

The architecture below is designed so every item on this list becomes either
a trace entry (visible per-request) or a linter finding (visible before any
user hits it).

## Core decision 1: one pure decision path, assembly included

Today the *matcher* is pure but the *decision* isn't: user resolution,
publication registry lookup, descriptor parsing, content-label transport,
and feature flags all happen inline. The POC restructures the whole decision
as pure stages with explicit inputs:

```
Resolve:    userPublicationIds × PublicationRegistry → ResolvedPublications   (records DROPPED ids)
Parse:      descriptor string → Grant (normalized key→values)                 (records invalid/empty/unknown-key)
Transport:  raw content labels → TagSet                                       (records dropped label keys)
Match:      Grant[] × TagSet × flags → Decision                               (rules 1–8, per-key trace)
```

Composed: `Evaluate(inputs) → DecisionTrace`, deterministic, no I/O, no
clock, no DI. The BFF's API layer and HTTP clients only *fetch the inputs*
and call it. Two payoffs:

- **Reproducibility.** Any incident replays exactly from captured inputs.
  Inputs right + answer wrong → code bug. Inputs wrong → data bug. That's
  the code-vs-data split, mechanized.
- **The silent drops become data.** "Publication 551909 on your license is
  not in the registry" stops being invisible and becomes line one of the
  trace — the sentence that, today, never gets said.

Normalization becomes **one shared pure function** applied identically to
descriptor values and content values, killing suspect #2 structurally.

## Core decision 2: the trace is the output, allow/deny is a projection

`TagLicenseAccessResult` + `publicationChecks` is the right idea at the
wrong granularity. The POC's decision trace contains:

- verdict + which open-content gate or which publication decided it,
- **assembly losses**: dropped publication IDs, invalid descriptors, dropped
  label keys (suspects 1, 3, 4),
- per publication: the key intersection K, and per key in K the exact value
  pairs compared and whether hierarchy matched — so a deny reads
  "pub 540200 `refleks`: K={LearningMaterial, Subject}; LearningMaterial
  matched (refleks); Subject failed (pub: samfunnsfag; content: naturfag)",
- rule version + input snapshot (or hash).

Exposed as an internal diagnostic endpoint on the BFF:

```
GET /internal/entitlements/why?userId=...&contentId=...
```

fetch live inputs → run the pure core → return the trace. This endpoint is
the support tool that kills the archaeology sessions. (Internal only,
audit-logged.) The existing App Insights reason metric keeps working — the
reason enum is now just a projection of the trace, and it can finally carry
*which key* failed as a dimension.

## Core decision 3: the testing strategy

Bottom-heavy: nearly all value is in tests of the pure core. The existing
`TestDataSinglePublications` table is already the right shape — descriptor
string × TagSet × expected reason. The POC promotes that shape to the center
of the strategy and fixes what's around it.

### 1. Scenario corpus — executable specification (highest value)

Move the table style of `TestDataSinglePublications` into data files
(YAML/JSON), one scenario per case:

```yaml
name: refleks naturfag licence does not open samfunnsfag content
publications: ["LearningMaterial=refleks;LearningComponent=fagrom,bokstoette;Subject=naturfag"]
content:      { LearningMaterial: [refleks], Subject: [samfunnsfag] }
expect:       deny
because:      "Subject failed: pub=naturfag content=samfunnsfag"
```

- Readable and reviewable by the people who *configure* publications — this
  is where the tribal knowledge about how licensing is supposed to work
  becomes executable.
- `because` is asserted against the trace, not just the verdict, so
  explanations are regression-tested too.
- **Every production incident becomes a scenario file**, captured straight
  from the `why` endpoint. The corpus grows into a regression shield shaped
  exactly like the bugs we actually get.

Crucially: **decouple the spec corpus from production data snapshots.** The
current `TagLicenseServiceTests` bind to `PublicationFetcherJob`'s virtual
publications and `MockUser` templates — so a test failure can mean "code
broke" *or* "someone edited a virtual publication," which is the disease
this POC exists to cure, reproduced inside the test suite. Inline-descriptor
scenarios test the code. A separate, clearly-labeled, small suite pins the
real publication-registry snapshot and is understood as *data* regression.

### 2. Property-based tests — the combination mess, tamed

FsCheck/CsCheck generators for random descriptors and TagSets, asserting
invariants that must hold for *any* combination:

- **Monotonic in publications:** adding a publication to a user never
  revokes access to anything (holds because the model is purely additive —
  first-success-wins, no negation; this is the "bought more, now less
  works" bug class).
- **Publication keys restrict:** adding a key to a descriptor never grants
  access to more content.
- **Hierarchy:** a publication value `v` covers content `v/x…` for any
  suffix; `v` never covers `vx` (boundary); a child value never covers
  parent content.
- **Normalization:** idempotent, and identical for descriptor values and
  content values (this property is a *probe* — it may well fail against
  current semantics, which is a finding, not a broken test).
- **Order independence:** publication order and value order never change
  the verdict.
- **Trace soundness:** if allow, the publication named in the trace also
  allows when evaluated alone; if deny, no publication evaluated alone
  allows. Mechanically ties explanations to reality.

Note what is *deliberately not* a property: adding a tag key to content is
not monotone in either direction (it can add a binding constraint under
rule 8, or lift content out of the rule-4 open gate). That asymmetry is
real semantics — document it in the spec, cover it with scenarios.

### 3. Oracle (model-based) testing

The spec table above, implemented as a brutally naive second `Match` —
nested loops straight off the rule table, small enough to verify by eye.
Property tests assert the real implementation always agrees with it over
generated inputs. When the real core grows indexes or short-circuits, this
keeps it honest.

### 4. License linter — testing the *data* half

Runs over the real publication registry (batch job + CI over fixtures):

- descriptor syntax invalid — **today this grants everything** (rule 5);
  the linter makes it a data incident instead of a security hole,
- descriptor keys with no `TagType` counterpart (the `product=pilot` case),
- descriptors that parse to zero grants,
- values referencing e.g. `LearningMaterial` names that no content in the
  catalog carries (needs a content-vocabulary feed; even a periodic dump
  works),
- and against real user licenses: **publication IDs not present in the
  registry** — suspect #1, caught proactively instead of via support ticket.

The linter and the core tests split the bug space: linter catches data bugs
before a user does, core tests catch code bugs before deploy.

### 5. Contract tests on the HTTP clients

For each upstream (user/license service, publication registry, content
metadata), verified stub responses + tests that the DTO→domain mapping is
lossless for every field the core reads. Half of "the data was wrong"
incidents are really "the data didn't mean what we assumed" — that
assumption lives at this boundary, so pin it here.

### 6. Thin API integration tests

`WebApplicationFactory` + stubbed clients: token in → correct upstream
calls → core invoked with correctly mapped inputs → 200/403/trace out. A
handful. Any test asserting a licensing *rule* at this layer is in the
wrong layer.

## POC repo layout & build order

```
src/
  Bff.Entitlements/          # pure core: Resolve/Parse/Transport/Match + DecisionTrace
  Bff.Entitlements.Oracle/   # naive rule-table implementation
  Bff.Api/                   # minimal API: content endpoint + /internal/entitlements/why
  Bff.Clients/               # typed HTTP clients + DTO→domain mapping (stubbed upstreams)
tests/
  Bff.Entitlements.Tests/    # corpus runner + property tests vs oracle
  scenarios/                 # the YAML corpus — the crown jewel
  Bff.Api.Tests/             # thin smoke tests
tools/
  LicenseLinter/             # console app over publication fixtures
reference/                   # the real TagLicenseService + tests this design is derived from
```

Build order (each step demoable):

1. Port the model (`TagSet`, `TagType`, descriptor parsing with **one**
   shared normalizer) and the rule table as the spec + oracle.
2. Pure `Evaluate` with full `DecisionTrace`, driven by a starter corpus
   (~25 scenarios: seed from `TestDataSinglePublications`, the custom-mix
   cases, hierarchy cases, plus one scenario per suspect on the list).
3. Property tests vs oracle — including the normalization-consistency probe.
4. `why` endpoint with stubbed clients — the support-tool demo: replay a
   re-enacted historical incident end to end.
5. Linter over sample publication fixtures with planted defects (invalid
   syntax, unknown key, unknown publication ID on a user).

## Success criteria

For a re-enacted historical incident ("user U can't open content C despite
valid license"):

1. One `why` call yields a verdict and a human-readable reason in under a
   minute.
2. The trace makes code-vs-data unambiguous — including the currently
   invisible cases (dropped publication ID, invalid descriptor, dropped
   label key).
3. If code: the captured inputs drop into `tests/scenarios/` as a failing
   test with no harness work.
4. If data: the linter or trace states the defect in provisioning terms
   someone outside the dev team can act on.

## Trade-offs considered

- **External policy engine (OPA/Cerbos/Casbin)** — rejected for the core.
  The rules (the table above) are few and stable; the *data* is wild. A
  typed C# core gives better explanations and property testing in the
  team's language, and the matcher is a dozen lines once formalized.
- **Where the core ultimately lives** — the pure `Evaluate` is extractable
  to a dedicated entitlement service by construction; the scenario corpus
  is the portable spec if it moves. What must not happen is a second,
  divergent implementation of matching elsewhere.
- **Fail-open decisions (rules 4 and 5, null content values)** — kept as-is
  in the POC so the spec matches production, but flagged in the spec as
  owned policy decisions. Changing them is a product conversation the trace
  and linter will finally make evidence-based.
- **Caching** — out of scope; if added, cache *inputs* (publication
  registry, license snapshots), never verdicts, or the `why` endpoint lies.
  Verdict caching, if ever needed, keys on input hashes + rule version.
