---
applyTo: "docs/**/*.md,README.md"
---

# Documentation under docs/

- Documents in `docs/` (architecture, ADRs, developer guide) and `README.md` are written in Polish; code, XML docs and
  code comments are in English.
- A new architectural decision is a new ADR in `docs/adr/` (next number, status, date, context, decision, consequences) plus a row
  in `docs/adr/README.md`; a superseded ADR gets the status "Zastąpiony przez ADR-NNNN"; an ADR refined by a newer one gets the note
  "(doprecyzowany przez ADR-NNNN: zakres)" in its status line and index row (ADR-0043), with no other change to its content. Update `docs/architektura.md` and the
  guide in `docs/przewodnik/` when a decision changes how the system works.
- Do not mention technologies that were considered and rejected, and do not mention AI assistants.
- Use the experience vocabulary (ADR-0038): super app, shell, experience, edge gateway (shared, out of scope; locally `SuperApp.Gateway`),
  BFF = BFF experience (never the `bff-web` gateway profile), domain service. Describe the current code: the BFF of the example
  experience (`src/Bff/Example.Bff`), the `superapp-bff` template and chart, and the token-forwarding handler (`AddUserTokenForwarding`)
  exist. Calls through the gateway use `/api/example/v1/knowledge/...` and `/api/example/v1/sleepdiary/...`; direct calls to a service
  (ports 5101/5102) are for debugging only and are labeled so. Open items (shared edge gateway, NetworkPolicy procedure, token exchange,
  CI check of breaking contract changes) are labeled as open. Locally docker compose does not enforce NetworkPolicy.
- Rules for synchronous calls (ADR-0040): the user's token is forwarded unchanged in the user's context; client credentials only for
  system calls; token exchange is an open door. Network isolation per experience (ADR-0041) is a requirement towards the
  infrastructure department with an open procedure; charts carry the labels `app.kubernetes.io/part-of` and
  `superapp.example/experience-role`.
- Product analytics and feature flags (ADR-0036) are described in `docs/przewodnik/21-analityka-i-feature-flags.md`; a new
  product event, event property or feature flag convention updates that chapter, and a new log EventId updates
  `docs/logowanie-eventid.md` (400–499 framework feature flags, 9000–9999 forwarder). Describe PostHog concretely and the CIAM
  generically. Web and mobile client code is not in the repository yet: Angular, Kotlin and Swift snippets are patterns for building
  the clients and must be labeled as such.
- After changing documentation run `dotnet superapp doctor --rule doc-links doc-paths`: relative links, `#anchors` and repository
  paths in backticks must resolve. A path to a file the reader is told to create goes into `.config/superapp-doctor.json`, never a
  broken path to an existing file. The tool itself is described in `docs/przewodnik/22-narzedzie-superapp.md`.
