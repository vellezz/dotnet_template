# ADR-0038: Experience: moduł, BFF i serwisy domenowe

- **Status:** Zaakceptowany
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §1, §3, §6; ADR-0002, ADR-0006, ADR-0007, ADR-0012, ADR-0014, ADR-0030, ADR-0037, ADR-0039,
  ADR-0040, ADR-0041; raport `docs/analizy/2026-10-01-weryfikacja-wzgledem-notatki-ze-spotkania.md`

## Kontekst

Architektura organizacji (notatka ze spotkania) opiera się na trzech pojęciach:

- **Super app:** całe rozwiązanie. Natywny shell innego zespołu dostarcza tożsamość, feature flags, push i nawigację, a moduły
  dostarczają różne zespoły.
- **Experience:** jedna funkcjonalność, czyli moduł aplikacyjny. Składa się z:
  - modułu klienta,
  - **BFF** pod wspólną bramą brzegową, który orkiestruje wywołania domen,
  - jednego lub kilku **serwisów domenowych**.
- **BFF-y** nie komunikują się ze sobą bezpośrednio. Wyjątkiem są jawne API wewnętrzne (ADR-0039).

Nasz zespół buduje **jedną experience**, ale rozwiązanie nie może zakładać, że experience jest tylko jedna. W repozytorium dziś nie
ma BFF w tym znaczeniu: `bff-web` to brama brzegowa (token handler, ADR-0037), a klient woła serwisy domenowe przez bramę.

## Decyzja

- **Słownik** (obowiązuje w dokumentach, kodzie i rozmowach):

  | Pojęcie | Znaczenie |
  |---|---|
  | super app | całe rozwiązanie organizacji |
  | shell | natywna aplikacja-host innego zespołu (tożsamość, flagi, push, nawigacja) |
  | experience | jedna funkcjonalność: moduł + BFF + 1..n serwisów domenowych; jednostka własności zespołu |
  | brama brzegowa | wspólna brama super appki (ADR-0037); lokalnie `SuperApp.Gateway`, profile `bff-web` i `gateway-mobile` (nazwy w kodzie zostają) |
  | BFF | BFF experience, a **nie** brama brzegowa |
  | serwis domenowy | bounded context (ADR-0002), bez zmian |

- **Experience w repozytorium** to jeden projekt BFF i 1..n serwisów domenowych.
  - Szablony i reguły nie zakładają liczby experience.
  - Kolejna experience (nasza albo innego zespołu) powstaje z tych samych szablonów.
- **BFF experience:**
  - **Hosting:** host ASP.NET Core MVC, bezstanowy. Bez własnej bazy, modelu domeny i migracji. Cache odczytów (`HybridCache`,
    ADR-0020) jest dopuszczalny.
  - **Orkiestracja:** w obrębie experience. Woła nasze serwisy domenowe (klienci Refit + Refitter, ADR-0014) i może czytać inne
    domeny przez ich API wewnętrzne lub ACL. Odpowiada za kształt danych dla modułu. Wywołania równoległe mają timeout per wywołanie
    i obsługę częściowej niedostępności.
  - **Logika biznesowa:** zostaje w serwisach domenowych. BFF nie zmienia stanu kilku domen w jednym żądaniu; procesy między domenami
    realizują zdarzenia i sagi (§9).
  - **Zero trust:** waliduje JWT jak każdy serwis (ADR-0007).
  - **Autoryzacja:** sprawdza uprawnienia wcześniej (UX, oszczędność wywołań), ale **serwis domenowy sprawdza zawsze** (scope, reguły
    zasobu). BFF nie jest jedyną linią obrony.
  - **API:** wystawia API publiczne dla modułu i API wewnętrzne dla innych experience (ADR-0039).
  - **Inne BFF-y:** nie woła ich poza API wewnętrznym innych experience (ADR-0039).
- **Serwisy domenowe** nie są wystawiane na brzegu. Przyjmują wywołania tylko od BFF swojej experience i od serwisów tej experience
  (ADR-0040, ADR-0041).
- **Moduł klienta** działa w shellu super appki. Kontrakt z shellem (tożsamość, flagi, zgoda analityczna) opisze osobny ADR. Do tego
  czasu ADR-0031 i ADR-0036 w części klienta mobilnego są wzorcem dla modułu działającego samodzielnie.

## Konsekwencje

- **Do zrobienia** (pakiety raportu):
  - szablon `src/Tools/SuperApp.Cli/Templates/superapp-bff` i projekt BFF (A2);
  - wsparcie orkiestracji i przekazywania tokenu w `SuperApp.Framework` (A3, ADR-0040);
  - testy architektury dla BFF (A4): BFF zależy tylko od wygenerowanych klientów i `Contracts`, nie od warstw serwisów ani innych
    BFF, nie ma `DbContext`; serwisy nie zależą od BFF;
  - przeniesienie przykładów Knowledge i SleepDiary za BFF przykładowej experience (A6).
- **Stan przejściowy:** do czasu powstania BFF lokalna brama kieruje ruch bezpośrednio do serwisów (`/api/{serwis}/**`). To wyjątek
  odnotowany w ADR-0037 i ADR-0039, nie wzorzec.
- **Koszt:** dodatkowy przeskok sieciowy i nowy rodzaj komponentu do utrzymania. Część logiki prezentacji może się powtarzać między
  experience; akceptujemy to tak jak notatka.
