# 19. FAQ

Pytania, które zadaje każdy w pierwszych tygodniach. Odpowiedzi odsyłają do rozdziałów z pełnym wyjaśnieniem.

## Zapis, wyniki, transakcje

**Gdzie jest zapis zmian? Handler tylko dodaje agregat do repozytorium.**
Zapis robi ostatni pipeline behavior, `TransactionBehavior`. Po udanym wyniku handlera wywołuje `IUnitOfWork.SaveChangesAsync`:
dispatch zdarzeń domenowych, `INSERT`/`UPDATE` agregatów i wiadomości outboxa w jednej operacji, potem commit i akcje po
commicie. Przy błędzie nic nie zostaje zapisane. Dzięki temu każda komenda jest atomowa i nikt nie zapomni o zapisie.
[02 Architektura w praktyce](02-architektura-w-praktyce.md), [06 Warstwa aplikacji](06-warstwa-aplikacji.md).

**Dlaczego `Add` w repozytorium jest synchroniczne, a `GetAsync` asynchroniczne?**
`DbContext.Add` tylko rejestruje encję w change trackerze, bez I/O. `AddAsync` w EF służy wyłącznie generatorom kluczy
pobierającym wartości z bazy (HiLo); nasze ID generuje kod (`Guid.CreateVersion7()`). Asynchroniczne są metody z zapytaniem
do bazy. [07 Dane i EF Core](07-dane-i-ef-core.md).

**Dlaczego `Result`, a nie wyjątki?**
Oczekiwane porażki są częścią kontraktu: mają stały kod, który klient dostaje w `ProblemDetails`. `Result` czyni je widocznymi
w sygnaturze, a pipeline nie zatwierdza transakcji przy porażce. Wyjątki są dla błędów technicznych. [05 Model domeny](05-model-domeny.md).

**Jak czytać wynik? `Result<T>` nie ma `Value`.**
`TryGetValue`: `if (!Category.Create(...).TryGetValue(out var category, out var error)) return error;`, potem `category.Id.Value`.
W testach `ResultAssert.Success(...)` i `ResultAssert.Failure(...)` (ADR-0047).

**Co się stanie, gdy dwa żądania jednocześnie utworzą kategorię o tym samym slugu?**
Oba przejdą sprawdzenie `SlugExistsAsync`, ale drugi zapis naruszy unikalny indeks. `WriteDbContextBase` zamieni to na
`Result` z błędem zamapowanym w `UniqueConstraintErrors` (`knowledge.category.slug_taken`, 409), a nie na 500.
[07 Dane i EF Core](07-dane-i-ef-core.md).

**Czy mogę zmienić dwa agregaty w jednej komendzie?**
Nie. Reakcja drugiego agregatu idzie przez zdarzenie (domenowe → integracyjne → komenda). Jeśli to częste, granice agregatów
mogą być źle wyznaczone. [04 Wybór kontekstu](04-wybor-kontekstu.md).

## API i HTTP

**Dlaczego przy niepoprawnym ID w ścieżce dostaję 404, a nie 400?**
ID, które nie przechodzi `XxxId.Create` (np. `Guid.Empty`), oznacza zasób, którego nie ma: tak odpowiadają zapytania, które nie
mają walidatora. Gdy ID trafia do komendy z walidatorem `NotEmpty()` (z ciała albo z trasy, np. `PublishMaterialValidator`),
puste ID odrzuca wcześniej walidator z 400 (`validation.failed`). Ścieżka, w której ID nie jest GUID-em, w ogóle nie pasuje
do trasy (`{id:guid}`) i daje 404 z ogólnym kodem `http.not_found`.

**Dlaczego dostaję 403 `auth.unauthenticated`, skoro brak tokenu to 401?**
Brak lub zły token odrzuca uwierzytelnianie API z 401, zanim żądanie dotrze do pipeline'u. `auth.unauthenticated` zwraca
pipeline, gdy żądanie dotarło bez tożsamości użytkownika (np. zapytanie „moje ulubione” wysłane przez Workera, który ma
tożsamość systemową bez `sub`). [09 Bezpieczeństwo](09-bezpieczenstwo.md).

**Czy mogę wołać serwis domenowy innej experience?**
Nie. Ruch do domeny experience idzie wyłącznie przez jej BFF (ADR-0038, ADR-0039). Dane innej experience bierzesz z API
wewnętrznego jej BFF (`/internal/v{n}`, z przekazanym tokenem użytkownika i scope `{experience}.internal.*`) albo z jej zdarzeń
integracyjnych. Na klastrze takie połączenie zablokuje NetworkPolicy (ADR-0041); lokalnie compose tego nie egzekwuje, więc
wywołanie „zadziała”, ale i tak jest niedozwolone. Odwrotnie: inne experience nie wołają naszych serwisów, tylko API wewnętrzne
naszego BFF. [04 Wybór kontekstu](04-wybor-kontekstu.md), [08 API i kontrakty](08-api-i-kontrakty.md).

**Jakim tokenem woła się inny serwis lub API wewnętrzne?**
W kontekście użytkownika: tokenem użytkownika przekazanym bez zmian (ADR-0040). Odbiorca waliduje JWT, sprawdza scope i reguły
zasobu (właściciel z `sub`), a identyfikatorowi użytkownika z payloadu nie ufa. Client credentials tylko dla wywołań systemowych
(dane ogólne, procesy w tle), jawnie oznaczonych i audytowanych. W kodzie: klient rejestrowany przez
`AddDownstreamApi<I{Serwis}Api>(configuration, "{Serwis}")` i `.AddUserTokenForwarding()` (handler z
`SuperApp.Framework.Infrastructure/Http/UserContext`); tak BFF `Example.Bff` woła Knowledge i SleepDiary. [9.8](09-bezpieczenstwo.md),
[przepis 08](przepisy/08-wywolanie-innego-serwisu.md).

**Gdzie jest BFF experience? Czym różni się od `bff-web`?**
BFF przykładowej experience to `src/Bff/Example.Bff` (szablon `dotnet new superapp-bff`, ADR-0038): fasada serwisów Knowledge
i SleepDiary pod `/v1/knowledge/...` i `/v1/sleepdiary/...`, endpoint komponowany `GET /v1/me/summary` i API wewnętrzne
`/internal/v1/...`. Lokalnie działa jako kontener `example-bff` (port 5120). `bff-web` to profil `SuperApp.Gateway`, lokalnego
zamiennika wspólnej bramy brzegowej (ADR-0037); nazwa jest historyczna. Brama kieruje `/api/example/v{n}/**` wyłącznie do BFF.
[02 Architektura w praktyce](02-architektura-w-praktyce.md#experience-w-repozytorium).

**Dodałem endpoint w serwisie, a przez bramę dostaję 404.**
Brama zna tylko BFF, a BFF nie wystawia operacji automatycznie. Zregeneruj klienta (`dotnet refitter --settings-file
src/Bff/Example.Bff/Clients/Knowledge/knowledge.refitter`), dodaj akcję w kontrolerze BFF wzorem istniejących
(`this.ToActionResult(await knowledge.XAsync(...))`) i zbuduj BFF, żeby zaktualizować `openapi/Example.Bff_public.json`.
[Przepis 01](przepisy/01-endpoint-komendy.md), [przepis 02](przepisy/02-endpoint-zapytania.md).

**Dlaczego BFF nie waliduje żądań?**
Walidacja i kody błędów należą do serwisu domenowego, który i tak musi sprawdzić każde żądanie (zero trust). BFF przekazuje ciało
dalej i zwraca `400` serwisu bez zmian (`ModelValidatorProviders.Clear()`; wejście niemożliwe do powiązania BFF odrzuca sam: `400`). Własna walidacja BFF odrzucałaby poprawne dane na
podstawie atrybutów wygenerowanych z kontraktu i odpowiadała ogólnym `request.malformed` zamiast kodów serwisu. [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md).

**Kiedy w BFF pisać coś więcej niż akcję przekazującą?**
Gdy ekran modułu potrzebuje danych z kilku serwisów albo w innym kształcie: endpoint komponowany, jak `ExperienceSummaryController`
(równoległe wywołania, limit 2 s na część odpowiedzi, status każdej części odpowiedzi). Reguły biznesowe nadal zostają w serwisach.
[Przepis 11](przepisy/11-nowa-experience-i-bff.md).

**Dlaczego przez bramę `bff-web` dostaję 401 mimo zalogowania?**
Brakuje nagłówka `X-CSRF: 1` na `/api/*`, albo sesja wygasła/została unieważniona (wylogowanie w CIAM). [09 Bezpieczeństwo](09-bezpieczenstwo.md).

**Komunikaty błędów są po polsku, a dokumentacja po angielsku. To błąd?**
Nie. Dokumentacja i komentarze w kodzie są po angielsku (ADR-0033). `Error.Message` trafia do użytkownika jako `title`
w `ProblemDetails`; klienci rozpoznają błąd po `code`, nie po treści.

**Dlaczego kontrakt OpenAPI ma `"openapi": "3.0.3"`, a działająca aplikacja zwraca `3.0.4`?**
.NET 10 zapisuje najnowszą łatkę 3.0.4, której część narzędzi (np. Rider) nie rozpoznaje. Treść jest identyczna, więc build
ustawia w commitowanym pliku `3.0.3` (ADR-0019). [08 API i kontrakty](08-api-i-kontrakty.md).

**Dodałem pole do DTO i kontrakt się zmienił. Czy to w porządku?**
Tak, nowe pole (na końcu rekordu) to zmiana wstecznie zgodna. Usunięcie lub zmiana nazwy pola jest zmianą łamiącą i wymaga
nowej wersji API. Diff `openapi/*.json` jest częścią review.

**Dlaczego nie ma `[Produces("application/json")]` na kontrolerach?**
Nadpisywał typ odpowiedzi błędów na `application/json`, a błędy muszą mieć `application/problem+json` (ADR-0015).
Typ odpowiedzi sukcesu deklaruje każdy `[ProducesResponseType]`.

## Dane i migracje

**Nie widzę mojej konfiguracji EF / encja nie ma tabeli.**
Konfiguracje są stosowane tylko z przestrzeni nazw kontekstu i podrzędnych: `Persistence/Write/...` dla zapisu,
`Persistence/Read/...` dla odczytu. Sprawdź katalog i `namespace`.

**Czy zmieniać nazwę pliku nowej migracji?**
Tak: `{data}_Nazwa.cs` → `Nazwa.cs`, plik `.Designer.cs` zostaje z datą. To konwencja ADR-0032; APP005 nie sprawdza katalogu `Migrations`, oznaczonego w `.editorconfig` jako kod generowany, więc build tego nie wymusi: pilnuje tego review.

**Mogę poprawić migrację, którą już wypchnąłem?**
Jeśli trafiła na jakiekolwiek wspólne środowisko albo do gałęzi głównej: nie, dodaj nową migrację. Edycja jest bezpieczna
tylko dla migracji, która istnieje wyłącznie u Ciebie lokalnie.

**Serwis nie startuje: startup probe zwraca 503.**
W bazie są oczekujące migracje. Uruchom Migrator (`dotnet run --project src/Migrator/SuperApp.Migrator --launch-profile local`
albo kontener `migrator`). Serwisy nigdy nie migrują same.

## Zdarzenia i cache

**Opublikowałem zdarzenie integracyjne, ale konsument go nie dostał.**
Wiadomość wysyła proces z włączonym dostarczaniem outboxa, czyli Worker (API tylko zapisuje do outboxa). Sprawdź, czy Worker
serwisu działa, czy komenda się zatwierdziła (bez commitu nie ma wiadomości) i czy wiadomość nie trafiła do kolejki `_error`.
[10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [13 Lokalne środowisko](13-lokalne-srodowisko-i-debugowanie.md).

**Po zmianie danych API przez chwilę zwraca stare dane.**
Cache jest unieważniany po commicie, ale inne repliki mogą jeszcze chwilę serwować kopię z pamięci (L1, domyślnie do 30 s).
Dla danych wymagających natychmiastowej spójności nie używaj cache. [11 Cache](11-cache.md).

## Środowisko i narzędzia

**Jak zdebugować jeden serwis przy reszcie w kontenerach?**
Zatrzymaj jego kontener (`docker compose -f deploy/local/docker-compose.yml stop knowledge-api`) i uruchom projekt z IDE
z profilu `knowledge-api` (ten sam port 5101). Najprościej wołaj API bezpośrednio tokenem `dev-cli`
([13, przepis 9.1](13-lokalne-srodowisko-i-debugowanie.md#91-breakpoint-w-handlerze-komendy-wywołanie-bezpośrednio-do-api)).
Wywołanie bezpośrednie na 5101/5102 służy tylko do debugowania. BFF w kontenerze woła alias
`knowledge-api.knowledge.svc.cluster.local`, który po zatrzymaniu kontenera znika. Żeby debugować przez bramę i BFF z przeglądarki,
podstaw pod alias przekaźnik `socat` do procesu na hoście
([13, przepis 9.2](13-lokalne-srodowisko-i-debugowanie.md#92-breakpoint-w-api-wywołanie-przez-bramę-i-bff-z-przeglądarki)).
Sam BFF debugujesz z IDE na porcie 5120 ([13, przepis 9.8](13-lokalne-srodowisko-i-debugowanie.md#98-breakpoint-w-bff-experience)).

**Czy mogę zaktualizować MediatR lub MassTransit?**
Tylko w ramach wersji open source (MediatR 12.x, MassTransit 8.x). Wersje 13+/9+ są komercyjne i wymagają nowego ADR (ADR-0035).

**Czy mogę dodać pakiet NuGet?**
Tylko z uzasadnieniem w opisie zmiany i po sprawdzeniu ADR (być może decyzja już zapadła, jak Refit w ADR-0014). Wersję
dodaje się w `Directory.Packages.props`.

**Copilot generuje kod niezgodny z zasadami.**
Sprawdź, czy instrukcje są wczytane (`.github/copilot-instructions.md`), użyj poleceń z `.github/prompts/` i zawsze kończ
buildem i testami. Jeśli instrukcja jest nieprecyzyjna, popraw ją w tej samej zmianie.

**Gdzie szukać decyzji, gdy reguła wydaje się dziwna?**
`docs/adr/README.md`: każda reguła ma ADR z kontekstem i konsekwencjami. Jeśli reguła przeszkadza, zaproponuj zmianę ADR.
