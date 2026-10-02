# ADR-0033: Dokumentacja w kodzie (XML) po angielsku, dla osoby nowej w zespole

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §10, §15; ADR-0019, ADR-0030, ADR-0032

## Kontekst

`SuperApp.Framework.*` jest wewnętrznym frameworkiem używanym przez wszystkie serwisy, a `Contracts`
i kontrolery API tworzą kontrakty dla innych zespołów. Bez opisów kontraktu (znaczenie parametrów,
zwracane błędy `Result`, wymagane scope) użytkownik typu musi czytać implementację.

## Decyzja

- **Język:** dokumentacja XML i komentarze w kodzie wyłącznie **po angielsku**. ADR i dokumenty w `docs/` po polsku.
  Literały tekstowe (komunikaty błędów, wyjątków, szablony logów) nie są dokumentacją i nie podlegają tej regule.
- **Odbiorca:** programista dołączający do zespołu, który nie zna frameworku ani kodu. Z dokumentacji typu ma się dowiedzieć,
  do czego służy, gdzie leży w architekturze, jak go użyć, co gwarantuje, co może pójść źle i czego nie robić.
  Opis powtarzający nazwę lub sygnaturę („Komenda bez wartości zwrotnej”) jest niedopuszczalny.
- **Typ publiczny:** `<summary>` (czym jest, rola), `<remarks>` (jak działa, reguły i niezmienniki, cykl życia, powiązane typy,
  pułapki), `<example>` z kodem z repozytorium, gdy użycie nie jest oczywiste, `<seealso>` dla typów powiązanych.
- **Składowa publiczna:** kontrakt w `<summary>`; `<param>` dla **każdego** parametru, także parametrów rekordu pozycyjnego
  i konstruktora podstawowego (znaczenie, format, jednostki, zakres, znaczenie `null`); `<typeparam>`; `<returns>` dla metod zwracających
  wartość, a dla `Result` / `Result<T>` wartość sukcesu i każdy możliwy błąd z kodem; `<exception>` dla wyjątków rzucanych celowo.
  Metody agregatu opisują warunki wstępne, zmianę stanu i zgłaszane zdarzenia domenowe.
- Implementacje interfejsów i nadpisania, które nic nie dodają: `<inheritdoc />`.
- Typy wewnętrzne (handlery, walidatory, repozytoria, konfiguracje EF, konsumenci) też dostają opis; nie jest wymuszany przez kompilator.
- Kontrolery: dokumentacja trafia do kontraktu OpenAPI (generator komentarzy XML `Microsoft.AspNetCore.OpenApi`; `AddOpenApi`
  wywoływane w projekcie Api), więc jest pisana dla konsumentów API, z `<response code>` dla każdego statusu (ADR-0019).
- **Wzorzec:** `SuperApp.Framework.*`.
- **Wymuszenie (błąd kompilacji):** `GenerateDocumentationFile` we wszystkich projektach; brak dokumentacji: **CS1591**;
  brak `param` / `typeparam` / `returns`: **APP006**; w `src/Framework` APP006 wymaga także `<remarks>` na typach publicznych
  (opcja `app_documentation_require_remarks` w `.editorconfig`). Jakość treści sprawdza code review.
- Projekty testowe są wyłączone z wymogu kompletności (CS1591 i APP006 w `NoWarn`), ale komentarze w nich też są po angielsku.

## Konsekwencje

- Zmiana sygnatury wymaga aktualizacji dokumentacji, inaczej build się nie powiedzie.
- Opisy w kontraktach OpenAPI zmieniają commitowane pliki kontraktów (zmiana niełamiąca).
- Zasada `internal` domyślnie (§15) ogranicza zakres dokumentacji do rzeczywistego API.
