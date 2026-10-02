# ADR-0030: Szablon nowego serwisu (`dotnet new superapp-service`)

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §13; ADR-0002, ADR-0004, ADR-0021, ADR-0025

## Kontekst

Każdy serwis ma tę samą strukturę projektów, konfigurację EF (schemat, historia migracji),
messaging, sondy i OpenAPI. Ręczne kopiowanie prowadzi do rozbieżności.

## Decyzja

- Szablon `dotnet new` w `src/Tools/SuperApp.Cli/Templates/superapp-service` (nazwa krótka `superapp-service`), parametr `-n`
  = nazwa serwisu w PascalCase; schemat bazy, scope i nazwy wdrożeń w małych literach.
- Szablon generuje 6 projektów (`Domain`, `Application`, `Infrastructure`, `Api`, `Worker`,
  `Contracts`) i 3 projekty testów, gotowe do kompilacji, bez kodu domenowego.
- Po wygenerowaniu: dodanie projektów do `SuperApp.slnx`, rejestracja kontekstu w `SuperApp.Migrator`,
  pierwsza migracja, wpis użytkownika bazy w skrypcie bootstrap, trasy w konfiguracji bram,
  wartości chartu Helm.

## Konsekwencje

- Zmiany wspólne dla wszystkich serwisów trafiają do `SuperApp.Framework`, nie do szablonu;
  szablon zawiera tylko kod specyficzny dla serwisu.
- Szablon jest aktualizowany razem ze zmianami konwencji; serwisy już wygenerowane nie są
  aktualizowane automatycznie.
