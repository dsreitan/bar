# BFF entitlement POC

A .NET BFF proof-of-concept for one problem: **testing and diagnosing the
authorization rules that match a user's license (publications with tag
descriptors) against content (tag metadata)** — so that "user has a correct
license but can't reach content" incidents are answerable as *code bug or
license setup?* in minutes.

- **ARCHITECTURE.md** — the design: the current matching semantics written
  down as a spec, the silent-failure suspect list, and the testing strategy.
- **docs/ALTERNATIVE-LICENSE-MODELS.md** — how other companies with flexible
  business models solve this (materialized entitlements, ReBAC), and a
  recommended migration path.
- **reference/** — the production `TagLicenseService` and tests this POC is
  derived from.

## Layout

| Path | What |
|---|---|
| `src/Bff.Entitlements` | The pure decision core: descriptor parsing, tag transport, matching, decision trace. No I/O. |
| `src/Bff.Entitlements.Oracle` | Naive reference implementation of the spec table; the evaluator must always agree with it. |
| `src/Bff.Clients` | Typed clients for the platform services (in-memory stubs with a planted incident). |
| `src/Bff.Api` | Minimal API: content endpoint + `/internal/entitlements/why` support tool. |
| `tests/scenarios/*.yaml` | The scenario corpus — the executable specification. Production incidents get captured here. |
| `tests/Bff.Entitlements.Tests` | Corpus runner, ported production tests, property-based tests (CsCheck) vs the oracle. |
| `tests/Bff.Api.Tests` | Thin wiring smoke tests. |
| `tools/LicenseLinter` | Validates publication *data*: invalid syntax (fail-open!), unknown keys, dead grants, unknown publication ids on licenses. |

## Run it

```bash
dotnet test                       # 107 tests: corpus ×2, semantics, 7 properties × 2000 cases

dotnet run --project tools/LicenseLinter          # data defects in sample-data/ (5 planted)

dotnet run --project src/Bff.Api --urls http://localhost:5199
```

Then re-enact the classic incident — user `bob` has a valid license whose
publication id 551909 the registry doesn't know:

```bash
curl "http://localhost:5199/content/multi-tavle?userId=bob"          # 403
curl "http://localhost:5199/internal/entitlements/why?userId=bob&contentId=multi-tavle"
```

The `why` response opens with the sentence production never says:

> publication 551909 on the user's license is NOT in the publication registry
> — it grants nothing (license/provisioning defect)

Other stub users: `alice` (Multi + Refleks naturfag), `carol` (publication
with invalid descriptor — fails open by design, marked in the trace),
`dave` (no license).

## Capturing an incident as a regression test

Take the license + content tags from a `why` response, add a YAML entry under
`tests/scenarios/`, state the expected verdict and the `because` substring of
the explanation. It runs against both the evaluator and the oracle from then on.
