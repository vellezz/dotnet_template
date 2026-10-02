# ADR-0001: Rejestrowanie decyzji architektonicznych

- **Status:** Zaakceptowany (zmieniony przez ADR-0043: dopiski o doprecyzowaniu w nagłówkach)
- **Data:** 2026-09-29
- **Decydenci:** zespół architektury (zapis przyjętych zasad architektury)
- **Powiązane:** zasady architektury (preambuła, §14, §15)

## Kontekst

Dokument zasad architektury jest wiążącym opisem architektury, ale nie przechowuje uzasadnień ani historii zmian.
Odejście od zasad ma być proponowane jako ADR, a decyzje otwarte (§14) wymagają miejsca na
udokumentowanie faktów i rozstrzygnięć.

## Decyzja

- Decyzje architektoniczne zapisujemy w `docs/adr/` w formacie z `0000-szablon.md`.
- Numeracja czterocyfrowa, rosnąca, nazwy plików w kebab-case (`NNNN-tytul.md`).
- Statusy: **Proponowany**, **Zaakceptowany**, **Odrzucony**, **Zastąpiony przez ADR-XXXX**.
- Zaakceptowanego ADR nie edytujemy merytorycznie; zmiana decyzji = nowy ADR, który zastępuje stary.
- Każdy ADR z faktami zewnętrznymi (licencje, wsparcie wersji) podaje datę weryfikacji i źródła.
- Indeks ADR utrzymujemy w `docs/adr/README.md`.
- ADR mogą być pisane po polsku.

## Konsekwencje

- Przed dodaniem pakietu objętego decyzją otwartą należy sprawdzić `docs/adr/` (zasady architektury §14).
- ADR-y o statusie „Proponowany” nie mogą być traktowane jako rozstrzygnięcie.
