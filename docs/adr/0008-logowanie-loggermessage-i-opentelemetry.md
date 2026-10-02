# ADR-0008: Logowanie przez `[LoggerMessage]` i telemetria OpenTelemetry (OTLP)

- **Status:** Zaakceptowany
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury §11

## Kontekst

System rozproszony wymaga korelacji logów, śladów i metryk; logowanie musi być wydajne
i nie może ujawniać danych osobowych ani tokenów.

## Decyzja

- Logi wyłącznie przez source generator `[LoggerMessage]` ze stałymi `EventId`
  i zakresami numeracji per serwis. Klasy `Log` domyślnie `internal`.
- Zakaz interpolacji stringów w logach oraz logowania danych osobowych i tokenów.
- OpenTelemetry (tracing, metryki, logi) → OTLP → OTel Collector → Tempo / Loki / Prometheus.
- Instrumentacja: ASP.NET Core, HttpClient (obejmuje YARP i Refit), SqlClient (zapytania EF Core), MassTransit, runtime.
- Propagacja `traceparent` od bramy przez serwisy do konsumentów.

## Konsekwencje

- Pakiet instrumentacji EF Core dla OpenTelemetry był długo publikowany jako prerelease;
  przed dodaniem do CPM sprawdzić status wersji. Alternatywa: instrumentacja SqlClient.
- MassTransit emituje własne `ActivitySource`; wymaga `AddSource("MassTransit")`.
- Rejestr zakresów `EventId` per serwis prowadzony w repo, żeby uniknąć kolizji.
