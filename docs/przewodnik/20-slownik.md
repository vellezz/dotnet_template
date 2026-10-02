# 20. Słownik

Pojęcia używane w kodzie, dokumentacji i rozmowach w zespole. W nawiasach: gdzie występują w kodzie.

## Pojęcia domenowe

| Pojęcie | Znaczenie |
|---|---|
| **Materiał** (`Material`) | treść edukacyjna w Knowledge: artykuł, wideo albo podcast; ma treść blokową, kategorie i status publikacji |
| **Treść blokowa** (`ContentBlock`, `BlockSpec`, `ContentBuilder`) | treść materiału jako drzewo bloków (nagłówki, akapity z fragmentami tekstu, cytaty, listy, tabele, galerie, osadzenia, transkrypcje…) zapisane relacyjnie, nie jako JSON |
| **Fragment tekstu** (`TextSpan`, `SpanSpec`) | kawałek tekstu akapitu z oznaczeniami (pogrubienie, kursywa, kod…) i opcjonalnym linkiem |
| **Kolekcja** (`Collection`) | uporządkowana lista materiałów z własnym tytułem, opisem i statusem publikacji |
| **Kategoria** (`Category`) | etykieta tematyczna materiałów i kolekcji; ma unikalny `slug` |
| **Status publikacji** (`PublicationStatus`) | `Draft` → `Published` → `Archived`; przejścia tylko w tę stronę |
| **Redaktor** | użytkownik ze scope `knowledge.catalog.write`; widzi i zmienia materiały w każdym statusie |
| **Czytelnik** | użytkownik bez scope redaktora; widzi tylko opublikowane materiały i kolekcje |
| **Ulubione** (`Favorite`) | materiał lub kolekcja zapisana przez użytkownika; jedna pozycja na użytkownika i element |
| **Przeczytane** (`MaterialCompletion`) | oznaczenie materiału jako ukończonego przez użytkownika |
| **Wpis dziennika snu** (`SleepEntry`) | jeden wpis na użytkownika i dzień pobudki: pora snu i pobudki, latencja, wybudzenia, jakość 1–5, notatki |

## Architektura i DDD

| Pojęcie | Znaczenie |
|---|---|
| **Super app** | całe rozwiązanie organizacji: natywny shell i moduły wielu zespołów (ADR-0038) |
| **Shell** | natywna aplikacja-host innego zespołu: tożsamość, feature flags, push, nawigacja |
| **Experience** | jedna funkcjonalność super appki: moduł klienta + BFF experience + 1..n serwisów domenowych; jednostka własności zespołu. Nic nie zakłada, że jest tylko jedna |
| **Moduł** | część kliencka experience działająca w shellu; woła wyłącznie API publiczne BFF swojej experience |
| **BFF (BFF experience)** | bezstanowy host ASP.NET Core MVC experience (`src/Bff/{Experience}.Bff`, szablon `dotnet new superapp-bff`, chart `superapp-bff`): przekazuje wywołania modułu do serwisów experience, orkiestruje je i kształtuje dane dla modułu, waliduje JWT; bez bazy, walidacji treści żądań i logiki biznesowej. Przykład: `Example.Bff` (`example-bff`). **Nie** jest to profil bramy `bff-web` |
| **Fasada (akcja przekazująca)** | akcja BFF, która przekazuje wywołanie do jednej operacji serwisu i zwraca jego odpowiedź bez zmian (`this.ToActionResult(await client.XAsync(...))`); ścieżka BFF = `/v{n}/{serwis}/...` (np. `/v1/knowledge/materials`) |
| **Endpoint komponowany** | akcja BFF składająca odpowiedź z kilku serwisów (`GET /v1/me/summary` w `ExperienceSummaryController`); części ładowane równolegle |
| **Częściowe renderowanie** (`PartialResponseFetcher`, `ResponsePart<T>`, `ResponsePartStatus`) | endpoint komponowany zawsze odpowiada `200`, a każda część odpowiedzi ma status `Ok`, `Forbidden`, `Unavailable` albo `Timeout` (limit 2 s na część odpowiedzi) i dane tylko przy `Ok`; awaria jednego serwisu nie psuje całego ekranu |
| **Serwis domenowy** | mikroserwis = bounded context; tylko wewnętrzny, przyjmuje wywołania od BFF i serwisów tej samej experience |
| **Bounded context (kontekst)** | granica modelu z własnym językiem i regułami; u nas = mikroserwis z własnym schematem |
| **Ubiquitous language** | wspólny język zespołu i ekspertów domeny, używany w nazwach kodu |
| **Agregat** (`AggregateRoot<TId>`) | klaster obiektów z korzeniem pilnującym niezmienników; jednostka spójności transakcyjnej i zapisu |
| **Niezmiennik** | reguła, która musi być prawdziwa po każdej operacji agregatu (np. opublikowany materiał ma treść) |
| **Encja** (`Entity<TId>`) | obiekt z tożsamością wewnątrz agregatu |
| **Value object** (`ISingleValueObject`) | niezmienny obiekt bez tożsamości, porównywany po wartościach, zawsze poprawny (`SleepQuality`, `UserId`) |
| **Silne ID** (`IStronglyTypedId`) | typ identyfikatora per agregat (`MaterialId`); chroni przed pomyleniem identyfikatorów |
| **Zdarzenie domenowe** (`IDomainEvent`) | fakt wewnątrz kontekstu, obsługiwany w tej samej transakcji (`MaterialPublished`) |
| **Zdarzenie integracyjne** | publiczny, wersjonowany fakt dla innych kontekstów, w `{Serwis}.Contracts` (`MaterialPublishedV1`) |
| **Published language** | zbiór zdarzeń integracyjnych kontekstu: jego publiczny kontrakt |
| **ACL (anti-corruption layer)** | warstwa w Infrastructure tłumacząca model innego systemu na pojęcia własnego kontekstu |
| **Port** | interfejs w Domain/Application implementowany w Infrastructure (`ICategoryRepository`, `IIntegrationEventPublisher`) |
| **Composition root** | miejsce rejestracji zależności (`Add{Service}Infrastructure`, `Program.cs`) |
| **Spójność ostateczna** | dane w różnych kontekstach/agregatach zgadzają się po krótkim czasie (zdarzenia), nie natychmiast |

## CQRS i przypadki użycia

| Pojęcie | Znaczenie |
|---|---|
| **CQRS** | rozdzielenie zapisu (komendy przez agregaty) i odczytu (zapytania z read modeli) |
| **Komenda** (`ICommand`) | żądanie zmiany stanu; jeden przypadek użycia, jeden agregat |
| **Zapytanie** (`IQuery<T>`) | żądanie odczytu bez zmiany stanu; handler w Infrastructure |
| **Handler** | klasa obsługująca jedną komendę lub zapytanie |
| **Pionowy wycinek** (`Features/{Agregat}/{PrzypadekUżycia}`) | wszystkie elementy przypadku użycia w jednym katalogu |
| **Pipeline behavior** | krok wykonywany wokół każdego handlera: logowanie, autoryzacja, walidacja, transakcja |
| **`Result` / `Error`** | wynik operacji z jawnym błędem o stałym kodzie zamiast wyjątku |
| **`TryGetValue`** | sposób odczytu wartości z `Result<T>`: sprawdzenie i nazwanie wartości w jednym kroku |
| **Unit of Work** (`IUnitOfWork`, `WriteDbContextBase`) | jednostka zapisu: dispatch zdarzeń domenowych, zapis zmian i outboxa w jednej operacji |
| **Akcja po commicie** (`IUnitOfWork.OnCommitted`) | praca wykonywana po zatwierdzeniu transakcji (np. unieważnienie cache) |
| **Read model** (`*Row`) | płaska struktura do odczytu, mapowana na tabele kontekstu zapisu |
| **DTO** | obiekt przesyłany przez API; tylko prymitywy i enumy |
| **Klient Refitter** (`Clients/{Serwis}/Generated`) | interfejs Refit (`IKnowledgeApi`) i DTO wygenerowane narzędziem `refitter` z commitowanego kontraktu OpenAPI serwisu (plik `{serwis}.refitter`); regenerowany ręcznie (`dotnet refitter --settings-file ...`), nigdy edytowany; nazwy metod z `operationId` |
| **`AddDownstreamApi`** | rejestracja klienta Refit innego API systemu (`SuperApp.Framework.Infrastructure/Http/Downstream`): adres z `Downstream:{nazwa}:BaseAddress`, standardowa odporność (ponowienia tylko metod bezpiecznych: GET, HEAD, OPTIONS), daty ISO w ścieżce; uzupełniana `AddUserTokenForwarding` albo `AddClientCredentialsToken` |
| **`ToActionResult` (BFF)** | `DownstreamResponseExtensions.ToActionResult`: zamiana odpowiedzi Refit na odpowiedź akcji BFF; błąd serwisu (status, ciało z `code`) przekazany bez zmian |
| **`downstream.unavailable` / `downstream.timeout`** | kody `503` / `504` z BFF (`DownstreamUnavailableExceptionHandler`, log 220), gdy serwis nie odpowiedział: brak połączenia lub otwarty circuit breaker / przekroczony limit czasu |

## Dane i infrastruktura

| Pojęcie | Znaczenie |
|---|---|
| **Schemat serwisu** | schemat MSSQL (`knowledge`, `sleepdiary`, `gateway`) z tabelami, historią migracji, outboxem i inboxem serwisu |
| **Migracja** | zmiana schematu generowana z kontekstu zapisu; stosowana przez Migrator (dev/test) albo DBA (prod) |
| **Expand / contract** | dwuetapowa zmiana schematu: najpierw rozszerzenie zgodne ze starą wersją, potem usunięcie starego |
| **Outbox** | tabela wiadomości zapisywana w transakcji z danymi i wysyłana do RabbitMQ po commicie |
| **Inbox** | rejestr przetworzonych wiadomości konsumenta; zapewnia, że ta sama wiadomość nie zostanie obsłużona dwa razy |
| **Kolejka `_error`** | kolejka RabbitMQ na wiadomości, których przetwarzanie nie powiodło się po ponowieniach |
| **Fail-safe** (`FailSafeCache`) | zwrot ostatniej znanej wartości z cache, gdy odświeżenie zawiodło z błędem przejściowym |
| **L1 / L2** | poziomy cache: pamięć procesu / Redis współdzielony przez repliki |
| **Tag cache** | etykieta wpisów cache umożliwiająca ich wspólne unieważnienie |
| **Row version** | kolumna `Version` do optymistycznej kontroli współbieżności |

## Bezpieczeństwo i bramy

| Pojęcie | Znaczenie |
|---|---|
| **CIAM** | zewnętrzny dostawca tożsamości (OIDC / OAuth 2.0); lokalnie Keycloak |
| **Brama brzegowa** (edge gateway) | wspólna brama super appki, poza zakresem experience: zamiana sesji na JWT, walidacja JWT, CSRF, unieważnianie tokenów (ADR-0037) |
| **`SuperApp.Gateway`** | lokalny zamiennik wspólnej bramy brzegowej (compose, E2E, IDE) i specyfikacja wymagań wobec niej; bez logiki experience |
| **`bff-web`** | profil `SuperApp.Gateway` dla przeglądarki: logowanie OIDC, sesja po stronie serwera, ciasteczko zamiast tokenów; nazwa historyczna, to brama, a nie BFF experience |
| **Gateway mobile** (`gateway-mobile`) | profil `SuperApp.Gateway` dla aplikacji mobilnych: walidacja JWT, przekazanie tokenu |
| **API publiczne BFF** | `/v{n}/...`; jedyny konsument to moduł experience, ruch przez bramę brzegową (`/api/{experience}/v{n}/**`, np. `/api/example/v1/knowledge/materials`); kontrakt `openapi/{Experience}.Bff_public.json` (ADR-0039) |
| **API wewnętrzne BFF** | `/internal/v{n}/...`; konsumenci to BFF-y innych experience, tylko w klastrze, nigdy przez bramę, zmiany wyłącznie wstecznie zgodne; scope `{experience}.internal.*` (`example.internal.read`, polityka `RequireScope`); kontrakt `openapi/{Experience}.Bff_internal.json` (ADR-0039, ADR-0040) |
| **Przekazywanie tokenu** | wywołanie w kontekście użytkownika z jego tokenem bez zmian w `Authorization`; odbiorca waliduje JWT, scope i reguły zasobu (ADR-0040) |
| **Wywołanie systemowe** | wywołanie bez użytkownika (client credentials), wyjątek: jawnie oznaczone operacje, audyt `azp`; scope `{experience}.internal.system.*` dla API wewnętrznego BFF, `{serwis}.system.{akcja}` między serwisami jednej experience (ADR-0040, ADR-0042) |
| **Token exchange** (RFC 8693) | wymiana tokenu użytkownika na token z audience zawężonym do celu i tożsamością pośrednika; otwarta furtka, niewdrożona (ADR-0040) |
| **NetworkPolicy** | reguły ruchu w Kubernetes gwarantujące izolację experience: serwisy domenowe tylko od BFF i serwisów tej samej experience (ADR-0041); lokalnie nieegzekwowane |
| **Etykiety experience** | `app.kubernetes.io/part-of: {experience}` i `superapp.example/experience-role` (`bff`, `domain-service`, `worker`) w chartach Helm; selektory NetworkPolicy |
| **`ClockSkew`** | tolerancja czasu przy walidacji ważności tokenu: 30 s (`HostingExtensions.TokenClockSkew`), a nie domyślne 5 min |
| **YARP** | reverse proxy .NET, na którym zbudowane są bramy |
| **Scope** | uprawnienie w tokenie, konwencja `{serwis}.{zasób}.{akcja}` (`knowledge.catalog.write`) |
| **Audience** | odbiorca tokenu (API, dla którego token jest przeznaczony) |
| **`sub`** | identyfikator użytkownika w tokenie; w kodzie `ICurrentUser.Subject` i `UserId` |
| **`sid`** | identyfikator sesji SSO w CIAM; wiąże sesję bramy (`bff-web`) z SSO i chroni wylogowanie przed CSRF |
| **CSRF** | atak wysyłający żądania w imieniu zalogowanego użytkownika; ochrona: nagłówek `X-CSRF: 1`, `SameSite=Strict`, `sid` |
| **Back-channel logout** | wywołanie bramy (`bff-web`) przez CIAM po wylogowaniu w innym miejscu; brama usuwa sesję |
| **Client credentials** | przepływ OAuth bez użytkownika; u nas tylko dla wywołań systemowych (wyjątek), nie dla wywołań w kontekście użytkownika |
| **Zero trust** | żaden komponent nie ufa sieci ani nagłówkom; każdy serwis sam waliduje token |

## Analityka produktowa

| Pojęcie | Znaczenie |
|---|---|
| **PostHog** | narzędzie analityki produktowej, session replay i feature flags; PostHog Cloud EU, projekt na środowisko (ADR-0036) |
| **Pseudonim analityczny** (`AnalyticsIdentity`, `analyticsId`) | identyfikator użytkownika w PostHog: `u_` + 32 znaki hex z `HMAC-SHA256(Analytics:IdKey, sub)`; zwracany przez `/bff/user` i `GET /analytics/id`; zdarzenia bez użytkownika mają `system` |
| **Feature flag** (`FeatureFlag`, `IFeatureFlags`) | przełącznik funkcji sterowany z PostHog, z wartością domyślną z kodu używaną, gdy PostHog nie odpowie; nie jest uprawnieniem |
| **Forwarder analityki** (`SuperApp.AnalyticsForwarder`) | proces subskrybujący zdarzenia integracyjne i wysyłający dozwolone zdarzenia produktowe do PostHog; bez bazy i inboxu |
| **Zdarzenie produktowe** (`ProductEvent`, `ProductEventNames`) | zdarzenie w PostHog o nazwie `{serwis}_{obiekt}_{czasownik}`; nazwy są kontraktem z osobami budującymi analizy |
| **Proxy `/ingest`** | trasa lokalnej bramy `bff-web` zdefiniowana w kodzie, przekazująca ruch posthog-js do PostHog bez ciasteczek, tokenów i adresu IP; na klastrze wymaganie wobec wspólnej bramy albo konfiguracja modułu (ADR-0037) |
| **Session replay** | nagranie sesji użytkownika w przeglądarce; pola i teksty maskowane, ekrany z danymi o zdrowiu wyłączone z nagrywania |
| **Przegląd prywatności** | sprawdzenie każdej nowej właściwości zdarzenia analitycznego przed scaleniem zmiany |

## Narzędzia i jakość

| Pojęcie | Znaczenie |
|---|---|
| **APP001–APP006** | reguły analizatorów projektu (`src/Tools/SuperApp.Analyzers`), zgłaszane jako błędy kompilacji |
| **Testy architektury** | testy ArchUnitNET pilnujące zależności między warstwami i serwisami |
| **Testcontainers** | biblioteka uruchamiająca MSSQL w Dockerze na potrzeby testów integracyjnych |
| **Kontrakt OpenAPI** | plik generowany przy buildzie i commitowany (serwis: `openapi/{Serwis}.Api.json`, BFF: dwa pliki w `openapi/`); źródło generowanych klientów; `operationId` = `{Kontroler}_{Akcja}` |
| **ADR** | Architecture Decision Record: zapis decyzji z kontekstem i konsekwencjami (`docs/adr/`) |
| **`EventId`** | stały numer komunikatu logu z zakresu komponentu (`docs/logowanie-eventid.md`) |
