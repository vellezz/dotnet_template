# 18. Checklista przed pull requestem

Przejdź listę przy każdej zmianie. Przy punkcie, którego nie rozumiesz, kliknij odnośnik: każdy prowadzi do wyjaśnienia.
Ta sama lista jest podstawą polecenia Copilota `/review-against-rules`.

## Granice i model

- [ ] Funkcja jest we właściwym kontekście; brak referencji do projektów innych serwisów i odwołań do cudzych schematów
      ([04 Wybór kontekstu](04-wybor-kontekstu.md)).
- [ ] Logika biznesowa w agregacie (reguły, przejścia stanów, limity); handler tylko orkiestruje
      ([05 Model domeny](05-model-domeny.md)).
- [ ] Jedna komenda zmienia jeden agregat; reakcje innych agregatów przez zdarzenia.
- [ ] Nowe silne ID i value objects tworzone przez `Create`/`New`; brak `default`/`new()`; `FromTrusted` tylko w infrastrukturze i testach.
- [ ] Błędy biznesowe jako `Result` z błędem o stałym kodzie `{serwis}.{pojęcie}.{problem}` i właściwym `ErrorType`.
- [ ] Wartości z `Result<T>` odczytywane przez `TryGetValue`, błąd po sprawdzeniu `IsFailure`; w testach `ResultAssert` (ADR-0047).
- [ ] Czas przekazywany do metod agregatu (`now`), nie czytany w domenie.

## Experience, BFF i brama

- [ ] Brak logiki experience w `SuperApp.Gateway`: zmiana lokalnej bramy tylko odwzorowuje wspólną bramę brzegową albo wymaganie wobec niej
      ([02 Architektura w praktyce](02-architektura-w-praktyce.md#experience-w-repozytorium), ADR-0037).
- [ ] Brak tras brzegu do serwisów domenowych ani do `/internal`; jedyna trasa experience to `/api/{experience}/v{n}/**` do jej BFF
      (ADR-0039).
- [ ] Nowa lub zmieniona operacja serwisu, z której korzysta moduł, jest wystawiona w BFF: klient zregenerowany
      (`dotnet refitter --settings-file src/Bff/{Experience}.Bff/Clients/{Serwis}/{serwis}.refitter`), wygenerowany kod commitowany
      bez ręcznych zmian, akcja przekazująca `this.ToActionResult(await client.XAsync(...))`
      ([przepis 01](przepisy/01-endpoint-komendy.md), [przepis 02](przepisy/02-endpoint-zapytania.md)).
- [ ] BFF bez logiki biznesowej i bez walidacji treści żądań (walidacja i kody błędów należą do serwisu); nie zmienia stanu kilku
      domen w jednym żądaniu; endpoint komponowany ładuje części równolegle z limitem czasu i statusem części odpowiedzi (`PartialResponseFetcher`).
- [ ] BFF nie referuje projektów serwisów ani innych BFF i nie ma `DbContext` (reguły 12–14 testów architektury).
- [ ] Nie wołasz serwisu domenowego innej experience; dane innej experience tylko przez jej API wewnętrzne BFF albo zdarzenia (ADR-0041).
- [ ] Wywołanie synchroniczne w kontekście użytkownika przekazuje jego token; odbiorca sprawdza scope i reguły zasobu, nie ufa
      identyfikatorowi użytkownika z payloadu; client credentials tylko dla jawnie oznaczonych wywołań systemowych z audytem
      ([9.8](09-bezpieczenstwo.md), ADR-0040).
- [ ] Nowy zasób w chartach Helm ma etykiety `app.kubernetes.io/part-of: {experience}` i `superapp.example/experience-role` (ADR-0041).

## Przypadki użycia

- [ ] Komenda/zapytanie z `[RequiresScope]` (stała z `{Serwis}Scopes`) albo świadomie `[AllowAnonymousRequest]`
      ([06 Warstwa aplikacji](06-warstwa-aplikacji.md)).
- [ ] Walidator z limitami ze stałych domeny i tym samym przycinaniem co agregat.
- [ ] Handler komendy nie wywołuje zapisu, nie otwiera transakcji, nie publikuje zdarzeń integracyjnych.
- [ ] Handler zapytania w Infrastructure, `ReadDbContext`, projekcja na DTO z prymitywami, deterministyczne sortowanie, stronicowanie z limitem.
- [ ] Reguły dostępu do zasobu (właściciel, szkic) w handlerze lub agregacie ([09 Bezpieczeństwo](09-bezpieczenstwo.md)).

## Dane

- [ ] Każda zmiana modelu zapisu ma nową migrację; żadna zastosowana migracja nie jest edytowana ([07 Dane i EF Core](07-dane-i-ef-core.md)).
- [ ] Plik migracji bez daty w nazwie, dokumentacja klasy z fazą expand/contract, brak operacji destrukcyjnych w fazie expand.
- [ ] `dotnet ef migrations has-pending-model-changes` bez zmian.
- [ ] Konfiguracje EF w `Persistence/Write/Configurations` i `Persistence/Read/Configurations`; limity kolumn ze stałych domeny.
- [ ] Unikalne indeksy, które użytkownik może naruszyć równolegle, zamapowane w `UniqueConstraintErrors`.
- [ ] Repozytorium: odczyty asynchroniczne, `Add`/`Remove` synchroniczne.

## Zdarzenia i cache

- [ ] Zdarzenia domenowe w `{Agregat}/Events`, w czasie przeszłym, z czasem zmiany z agregatu ([10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md)).
- [ ] Zdarzenia integracyjne tylko przez translator i outbox; zmiana `Contracts` wstecznie zgodna albo nowy typ `V2`.
- [ ] Konsument cienki (wiadomość → komenda), błąd biznesowy logowany i potwierdzany, komenda bezpieczna przy ponowieniu.
- [ ] Cache tylko po stronie odczytu, klucz z wersją, unieważnianie przez `IUnitOfWork.OnCommitted` ([11 Cache](11-cache.md)).

## Analityka i feature flags

- [ ] Do PostHog trafiają tylko nazwy, identyfikatory katalogu i kategorie; brak `sub`, e-maili, treści użytkownika i danych
      z dziennika snu; użytkownik tylko jako pseudonim `analyticsId` ([21 Analityka i feature flags](21-analityka-i-feature-flags.md)).
- [ ] Nowa właściwość lub zdarzenie analityczne opisane w PR do przeglądu prywatności; nazwa w `ProductEventNames`, istniejące
      nazwy bez zmian; test z dokładną listą właściwości; kolejka w `keda.queues` ([przepis 10](przepisy/10-zdarzenie-analityczne-i-feature-flag.md)).
- [ ] Serwis nie wysyła zdarzeń do PostHog i nie referencjonuje pakietu `PostHog`; zdarzenia backendowe tylko przez forwarder.
- [ ] Flaga zadeklarowana w `{Serwis}FeatureFlags` (prefiks serwisu, bezpieczna wartość domyślna), czytana przez `IFeatureFlags`,
      sprawdzana w backendzie; nie zastępuje scope; testy obu stanów; flaga utworzona w PostHog każdego środowiska.
- [ ] Klient: PostHog dopiero po zgodzie, `identify(analyticsId)`, `reset()` przy wylogowaniu, replay z maskowaniem i bez ekranów
      z danymi o zdrowiu.

## API

- [ ] Kontroler cienki, `ToActionResult`, `[ProducesResponseType]` dla każdego statusu, brak `[Produces]` ([08 API i kontrakty](08-api-i-kontrakty.md)).
- [ ] Dokumentacja akcji dla konsumentów API: scope, limity, `<response code>` z kodami błędów.
- [ ] Diff `openapi/*.json` przejrzany: statusy, schematy, opisy; zmiana wstecznie zgodna albo nowa wersja API; `dotnet superapp contracts diff`
      bez zmian `Breaking` ([22.5](22-narzedzie-superapp.md#225-praca-codzienna-add-usecase-migration-contracts)).
- [ ] Kontrakty BFF (`openapi/{Experience}.Bff_public.json`, `openapi/{Experience}.Bff_internal.json`) zbudowane i commitowane;
      zmiana w kontrakcie wewnętrznym wyłącznie wstecznie zgodna (konsumentami są inne zespoły), zmiana łamiąca = nowe `/internal/v{n+1}`.
- [ ] Nowa operacja API wewnętrznego BFF ma politykę scope `{experience}.internal.*` (`[Authorize(Policy = ...)]`,
      `RequireScope`), a scope jest zgłoszony do CIAM (lokalnie w realmie).
- [ ] Operacja trafia do właściwego API: publiczne BFF (`/v{n}`, moduł), wewnętrzne BFF (`/internal/v{n}`, inne experience, tylko
      zmiany wstecznie zgodne, scope `{experience}.internal.*`) albo wewnętrzne API serwisu domenowego
      ([8.0](08-api-i-kontrakty.md#80-kto-konsumuje-które-api-model-experience)).

## Kod i dokumentacja

- [ ] Jeden typ na plik, nazwa pliku = typ, katalog według odpowiedzialności, przestrzeń nazw = katalog.
- [ ] `sealed`/`internal` domyślnie, file-scoped namespaces, `CancellationToken` w metodach asynchronicznych.
- [ ] Dokumentacja XML po angielsku, dla osoby nowej: rola, reguły, błędy z kodami, przykłady ([15 Dokumentacja w kodzie](15-dokumentacja-w-kodzie.md)).
- [ ] Logi przez `[LoggerMessage]`, `EventId` z zakresu komponentu, bez danych osobowych i tokenów ([14 Logowanie](14-logowanie-i-obserwowalnosc.md)).
- [ ] Brak nowych pakietów bez uzasadnienia, brak sekretów w plikach.

## Testy i weryfikacja

- [ ] Testy na właściwych poziomach: domena bez mocków, Application z fake'ami, integracyjne z Testcontainers, BFF jednostkowo
      (endpoint komponowany, API wewnętrzne) ([12 Testy](12-testy.md)).
- [ ] `dotnet build SuperApp.slnx`: 0 błędów, 0 ostrzeżeń.
- [ ] `dotnet test --solution SuperApp.slnx`: wszystko zielone, w tym testy architektury.
- [ ] `dotnet superapp doctor`: 0 błędów (solucje, rejestracje serwisów i BFF-ów, porty, EventId, linki i ścieżki w dokumentacji;
      [22 Narzędzie `dotnet superapp`](22-narzedzie-superapp.md)).
- [ ] Zmiany przekrojowe (zdarzenia, brama, BFF, uprawnienia): `dotnet superapp env up --build` i `dotnet superapp e2e` bez błędów;
      dodatkowo ręcznie w lokalnym środowisku docker compose, w tym przez bramę
      (`https://localhost:5001/api/example/v1/...` albo `https://localhost:5002/api/example/v1/...`).

## Dokumentacja projektu

- [ ] Nowa decyzja architektoniczna = nowy ADR; doprecyzowanie starszego ADR = dopisek w jego nagłówku i w indeksie (ADR-0043).
- [ ] Zmiana zasad (ADR) = aktualizacja instrukcji Copilota (`.github/`), podręcznika i `docs/architektura.md` w tej samej zmianie.
- [ ] Nowy zakres `EventId` wpisany w `docs/logowanie-eventid.md`.
