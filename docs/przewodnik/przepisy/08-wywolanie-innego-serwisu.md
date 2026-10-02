# Przepis 08: synchroniczne wywołanie innego serwisu (ACL)

**Kiedy:** serwis potrzebuje odpowiedzi innego serwisu **w trakcie** obsługi żądania i nie da się jej utrzymać jako lokalnej
kopii zasilanej zdarzeniami.
**Stan w repozytorium:** mechanizm działa w **BFF experience**: `src/Bff/Example.Bff` woła Knowledge i SleepDiary klientami
Refitter zarejestrowanymi przez `AddDownstreamApi<T>(configuration, name).AddUserTokenForwarding()` (ADR-0040). Framework ma wszystko,
czego potrzebuje także serwis domenowy:

- `AddDownstreamApi<T>` (`SuperApp.Framework.Infrastructure/Http/Downstream`): klient Refit przez `IHttpClientFactory`, adres z
  `Downstream:{name}:BaseAddress`, standardowa odporność z retry tylko metod bezpiecznych (GET, HEAD, OPTIONS), JSON i daty ISO zgodne z kontraktami,
  obsługa niedostępności dostawcy (`503 downstream.unavailable`, `504 downstream.timeout`);
- `AddUserTokenForwarding()` (`Http/UserContext`): przekazanie tokenu użytkownika bez zmian; `IDownstreamTokenProvider` to furtka na
  token exchange;
- `AddClientCredentialsToken(name)` (`Http/ClientCredentials`): token client credentials dla wywołań systemowych;
- `FailSafeCache` i klienci techniczni w lokalnym Keycloaku (`knowledge-client`, `sleepdiary-client`);
- pakiety `Refit`, `Refit.HttpClientFactory`, `Refit.Reflection`, `Microsoft.Extensions.Http.Resilience` (referencje w
  `SuperApp.Framework.Infrastructure`) i narzędzie `refitter` w `.config/dotnet-tools.json`.

**Żaden serwis domenowy nie woła jeszcze innego serwisu synchronicznie.** Nazwy typów w przykładach (`IKnowledgeGateway`,
`KnowledgeGateway`, `MaterialReference`) są propozycją dla pierwszego takiego przypadku. Jeśli to BFF ma pokazać dane z kilku
serwisów, nie potrzebujesz tego przepisu: wystarczy akcja albo endpoint komponowany w BFF
([przepis 02](02-endpoint-zapytania.md), wariant C).
**Przykład:** SleepDiary pokazuje tytuł materiału z Knowledge zalecanego przy wpisie snu, więc pyta Knowledge o materiał. Oba
serwisy należą do tej samej experience.
**Wyjaśnienia:** [10.12 Wywołania synchroniczne](../10-zdarzenia-i-integracja.md#1012-wywołania-synchroniczne-acl),
[9.8 Token użytkownika albo client credentials](../09-bezpieczenstwo.md#98-wywołania-synchroniczne-token-użytkownika-albo-client-credentials),
[11 Cache](../11-cache.md).

## Krok 0: czy na pewno synchronicznie?

| Sytuacja | Rozwiązanie |
|---|---|
| potrzebujesz danych innego serwisu często, mogą być lekko nieaktualne | **lokalna kopia** (read model w swoim schemacie) zasilana jego zdarzeniami integracyjnymi ([przepis 05](05-zdarzenia-i-cache.md), część C) |
| reagujesz na zmianę w innym serwisie | **zdarzenie integracyjne** + konsument |
| potrzebujesz odpowiedzi teraz, dane zmieniają się często albo są duże, kopia byłaby nieproporcjonalna | wywołanie synchroniczne przez ACL (ten przepis) |
| potrzebujesz, żeby inny serwis **coś zrobił** w tej samej transakcji | nie da się; proces między kontekstami = zdarzenia (sagi są dopiero planowane) |

Wywołanie synchroniczne wiąże dostępność Twojego serwisu z dostępnością dostawcy. Uzasadnij je w opisie zmiany i przedstaw
plan przed implementacją (nowe pakiety, scope, wymagania NetworkPolicy).

## Krok 0a: kogo wołasz i jakim tokenem

| Kogo wołasz | Jak | Token |
|---|---|---|
| serwis domenowy **tej samej experience** (ten przepis) | klient Refit z kontraktu serwisu, przez ACL | **wariant A (domyślny):** token użytkownika przekazany bez zmian; **wariant B (wyjątek):** client credentials dla wywołań systemowych |
| **inną experience** | wyłącznie API wewnętrzne jej BFF (`/internal/v{n}`, kontrakt `{Experience}.Bff_internal.json`, ADR-0039); ten sam wzorzec ACL, tylko źródłem jest kontrakt wewnętrzny BFF (wzór operacji: `GET /internal/v1/widgets/sleep-summary` w `Example.Bff`) | token użytkownika przekazany bez zmian (scope `{experience}.internal.*`); systemowo client credentials ze scope `{experience}.internal.system.*` |
| serwis domenowy **innej experience** | **nigdy**: NetworkPolicy odrzuci połączenie na klastrze (ADR-0041), choć lokalnie w compose zadziała | nie dotyczy |
| **system zewnętrzny** (np. dostawca) | ACL z kontraktem dostawcy | **własne poświadczenia** z Vault; token użytkownika nigdy nie opuszcza granicy zaufania (ADR-0012) |

- **Wariant A, w kontekście użytkownika** (żądanie HTTP z zalogowanym użytkownikiem): token użytkownika ma audience wszystkich naszych
  serwisów (ADR-0012), więc dostawca przyjmie go bez dodatkowej konfiguracji CIAM. Dostawca sam sprawdza scope i reguły zasobu, a
  wołający nigdy nie przesyła identyfikatora użytkownika w payloadzie zamiast tokenu.
- **Wariant B, systemowy** (Worker, zadanie w tle, dane ogólne lub zbiorcze): tylko do jawnie oznaczonych operacji, nigdy dla danych
  konkretnej osoby wskazanej identyfikatorem z payloadu; dostawca loguje wołającego (`azp`), a uzasadnienie jest w dokumentacji
  operacji w kontrakcie (ADR-0040).

## Pliki, które powstają (konsument SleepDiary, dostawca Knowledge)

```
SleepDiary.Application/Integrations/IKnowledgeGateway.cs                    port w języku SleepDiary
SleepDiary.Application/Integrations/MaterialReference.cs                    model SleepDiary (nie DTO Knowledge)
SleepDiary.Application/Integrations/KnowledgeGatewayErrors.cs               błędy SleepDiary dla wyników dostawcy
SleepDiary.Infrastructure/Integrations/Knowledge/knowledge.refitter         konfiguracja Refitter
SleepDiary.Infrastructure/Integrations/Knowledge/Generated/KnowledgeApi.cs  wygenerowany interfejs Refit i DTO (internal, commitowane)
SleepDiary.Infrastructure/Integrations/Knowledge/KnowledgeGateway.cs        implementacja portu: wywołanie, mapowanie, cache
SleepDiary.Infrastructure/InfrastructureServiceCollectionExtensions.cs      rejestracja klienta
SleepDiary.Api/appsettings*.json, SleepDiary.Worker/appsettings*.json      Downstream:Knowledge:BaseAddress, ClientCredentials (wariant B, bez sekretu)
deploy/local/docker-compose.yml, deploy/helm/superapp-service/values-sleepdiary.yaml  adres dostawcy (Downstream__Knowledge__BaseAddress)
deploy/local/keycloak/realm-superapp.json                                        scope systemowy dostawcy dla sleepdiary-client (wariant B, lokalnie)
tests: SleepDiary.Application.Tests (fake portu), test ACL z fake'iem interfejsu Refit
```

## Krok 1: kontrakt i uprawnienia po stronie dostawcy

1. Operacja istnieje w commitowanym kontrakcie dostawcy (`src/Services/Knowledge/Knowledge.Api/openapi/Knowledge.Api.json`),
   np. `GET /v1/materials/{materialId}` (scope `knowledge.catalog.read`).
   - **Wariant A:** użytkownik i tak ma `knowledge.catalog.read` (brama `bff-web` prosi o wszystkie scope), więc nic więcej nie trzeba. Dostawca
     odpowiada tak, jak odpowiedziałby temu użytkownikowi: redaktorowi z `knowledge.catalog.write` zwróci także szkic (uwaga na cache,
     krok 5).
   - **Wariant B:** kroki 2 i 3 poniżej.
2. (Wariant B) Operacja dostępna systemowo jest po stronie dostawcy **jawnie oznaczona** scope systemowym
   `{dostawca}.system.{akcja}` (ADR-0042), np. `[RequiresScope(KnowledgeScopes.SystemRead)]` ze stałą
   `SystemRead = "knowledge.system.read"`, i ma w dokumentacji uzasadnienie, dlaczego nie działa w kontekście użytkownika. Dziś
   takiej operacji ani scope w Knowledge nie ma: `GET /v1/materials/{materialId}` wymaga `knowledge.catalog.read` i jest operacją
   w kontekście użytkownika, więc wariant B wymaga najpierw nowej operacji systemowej dostawcy.
3. (Wariant B) Wywołujący serwis ma w CIAM klienta `{konsument}-client` (confidential, client credentials). Scope systemowy dostaje
   **tylko** ten klient techniczny (nie klienci użytkowników); scope dokłada do tokenu audience dostawcy (`knowledge-api`), który
   serwis dostawcy waliduje (ADR-0012, ADR-0042).
   - **Lokalnie:** w `deploy/local/keycloak/realm-superapp.json` dodaj client scope `knowledge.system.read` z mapperem
     `audience knowledge-api` (wzór: `knowledge.catalog.read`) i dopisz go do `optionalClientScopes` klienta `sleepdiary-client`
     (dziś ma tylko domyślne scope).
   - **Środowiska dev/test/prod:** to samo jako wymaganie dla działu CIAM (ADR-0016).

   Token klienta nie reprezentuje użytkownika (ma `sub` konta serwisowego): operacja systemowa nie może opierać się na `Subject`
   ani zwracać danych konkretnej osoby ([9.8](../09-bezpieczenstwo.md#98-wywołania-synchroniczne-token-użytkownika-albo-client-credentials)).

W obu wariantach ruch `sleepdiary` → `knowledge-api` mieści się w regule NetworkPolicy „serwisy tej samej experience” (ADR-0041),
pod warunkiem że oba wydania mają w chartach tę samą wartość `experience`.

Sprawdzenie lokalne wariantu A (tokenem `dev-cli`, bez bramy, rozdział 13):

```bash
TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=dev-cli -d username=reader -d password=reader \
  -d "scope=openid knowledge.catalog.read" | jq -r .access_token)

curl -s -i http://localhost:5101/v1/materials/3f2c8a51-0d7e-4c0b-9a57-6c1f1f7b2e10 -H "Authorization: Bearer $TOKEN"
# HTTP/1.1 200 OK
# {"id":"3f2c8a51-...","type":"Article","title":"Higiena snu", ... ,"status":"Published", ... }

# materiał nieopublikowany albo nieistniejący
# HTTP/1.1 404 Not Found
# Content-Type: application/problem+json
# {"title":"Materiał nie istnieje.","status":404,"instance":"/v1/materials/...","code":"knowledge.material.not_found","traceId":"..."}
```

Wariant B sprawdzisz tak samo tokenem klienta (`-d grant_type=client_credentials -d client_id=sleepdiary-client
-d client_secret=dev-sleepdiary-client-secret -d scope=knowledge.system.read`) na operacji systemowej, gdy ona i scope już istnieją.
Bez scope w tokenie dostawca odpowie 401 (zła audience) albo 403 z `code` `auth.missing_scope`.

## Krok 2: pakiety

Nic nie dodajesz. Pakiety z ADR-0014 są w `Directory.Packages.props` i trafiają do serwisu przez referencję
`SuperApp.Framework.Infrastructure`; narzędzie `refitter` (2.3.0) jest w `.config/dotnet-tools.json`. `Refit.Reflection` jest wymagany
przez Refit 16 dla `AddRefitClient`; framework go referuje, więc nie usuwaj go jako „nieużywanego”.

## Krok 3: generowanie klienta (Refitter)

Plik `SleepDiary.Infrastructure/Integrations/Knowledge/knowledge.refitter` (wzór: `src/Bff/Example.Bff/Clients/Knowledge/knowledge.refitter`;
różnice: typy `internal`, przestrzeń nazw konsumenta i filtr operacji; nazwę opcji filtra sprawdź w dokumentacji Refitter 2.3):

```json
{
  "openApiPath": "../../../../Knowledge/Knowledge.Api/openapi/Knowledge.Api.json",
  "namespace": "SleepDiary.Infrastructure.Integrations.Knowledge.Generated",
  "naming": { "useOpenApiTitle": false, "interfaceName": "KnowledgeApi" },
  "outputFolder": "./Generated",
  "outputFilename": "KnowledgeApi.cs",
  "useCancellationTokens": true,
  "returnIApiResponse": true,
  "typeAccessibility": "Internal",
  "usePolymorphicSerialization": true,
  "generateOperationHeaders": false,
  "operationNameTemplate": "{operationName}Async",
  "operationNameGenerator": "SingleClientFromOperationId",
  "codeGeneratorSettings": { "dateType": "System.DateOnly", "dateTimeType": "System.DateTimeOffset", "timeType": "System.TimeOnly" },
  "includePathMatches": [ "^/v1/materials/\\{materialId\\}$" ]
}
```

Nazwy metod pochodzą z `operationId` kontraktu dostawcy (`Materials_Get` → `MaterialsGetAsync`, `OpenApiOperationIdTransformer`).
`usePolymorphicSerialization` jest potrzebne, gdy odpowiedź zawiera typy polimorficzne (bloki treści Knowledge, `x-abstract` w
kontrakcie); zdublowaną właściwość dyskryminatora usuwa `PolymorphicDiscriminatorProperty`, który `AddDownstreamApi` stosuje
automatycznie.

```bash
dotnet tool restore
dotnet refitter --settings-file src/Services/SleepDiary/SleepDiary.Infrastructure/Integrations/Knowledge/knowledge.refitter
```

Zasady (ADR-0014):

- Źródłem jest **commitowany** kontrakt dostawcy (w monorepo ścieżka względna; poza monorepo kopia pliku w repozytorium
  konsumenta). Nie generuj z działającego serwisu.
- Tylko używane operacje (`includePathMatches` / tagi); typy `internal`; `CancellationToken` w każdej metodzie; odpowiedzi
  jako `IApiResponse<T>` (błąd HTTP nie jest wyjątkiem, mapujesz go sam).
- Wygenerowany kod commitujesz. Zmiana kontraktu dostawcy = regeneracja; błąd kompilacji po regeneracji to zamierzony sygnał
  niezgodności, nie coś do obejścia.
- Typy wygenerowane leżą w przestrzeni nazw konsumenta, więc test architektury `Service_does_not_depend_on_other_services`
  ich nie zgłasza. Nie dodawaj referencji do projektów dostawcy.

## Krok 4: port w Application (język konsumenta)

```csharp
// SleepDiary.Application/Integrations/IKnowledgeGateway.cs
public interface IKnowledgeGateway
{
    Task<Result<MaterialReference>> GetPublishedMaterialAsync(Guid materialId, CancellationToken cancellationToken);
}

// SleepDiary.Application/Integrations/MaterialReference.cs: tylko to, czego potrzebuje SleepDiary
public sealed record MaterialReference(Guid MaterialId, string Title);

// SleepDiary.Application/Integrations/KnowledgeGatewayErrors.cs
public static class KnowledgeGatewayErrors
{
    public static readonly Error MaterialNotFound =
        Error.NotFound("sleepdiary.material.not_found", "Materiał nie istnieje albo nie jest opublikowany.");
}
```

Port zwraca `Result` z błędami **konsumenta** (`sleepdiary.*`), nie kody dostawcy: klient SleepDiary nie powinien wiedzieć,
że pod spodem jest Knowledge. Wszystko z dokumentacją XML (ADR-0033).

## Krok 5: implementacja portu (ACL) w Infrastructure

```csharp
// SleepDiary.Infrastructure/Integrations/Knowledge/KnowledgeGateway.cs
internal sealed class KnowledgeGateway(IKnowledgeApi api, FailSafeCache cache) : IKnowledgeGateway
{
    private static readonly FailSafeOptions Materials = new(Fresh: TimeSpan.FromMinutes(10), MaxStale: TimeSpan.FromHours(2));

    public async Task<Result<MaterialReference>> GetPublishedMaterialAsync(Guid materialId, CancellationToken cancellationToken)
    {
        var material = await cache.GetOrCreateAsync(
            $"sleepdiary:knowledge-material:v1:{materialId}",
            token => new ValueTask<MaterialReference?>(LoadAsync(materialId, token)),
            Materials,
            tags: null,
            cancellationToken);

        return material is null ? KnowledgeGatewayErrors.MaterialNotFound : material;
    }

    private async Task<MaterialReference?> LoadAsync(Guid materialId, CancellationToken cancellationToken)
    {
        var response = await api.MaterialsGetAsync(materialId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode || response.Content is null)
        {
            // 401/403 = błąd konfiguracji (scope, audience), 5xx = awaria dostawcy: oba to błędy techniczne.
            throw new HttpRequestException($"Knowledge zwrócił {(int)response.StatusCode} dla materiału.", null, response.StatusCode);
        }

        return new MaterialReference(response.Content.Id, response.Content.Title);
    }
}
```

Mapowanie odpowiedzi:

| Odpowiedź dostawcy | Wynik ACL | Dlaczego |
|---|---|---|
| 200 | `MaterialReference` (model konsumenta) | obce DTO nie wychodzą z `Integrations/Knowledge` |
| 404 | `Result` z `sleepdiary.material.not_found` | oczekiwany wynik biznesowy, w języku konsumenta |
| 400 | wyjątek | konsument wysłał złe dane: błąd programistyczny |
| 401 / 403 | wyjątek | błąd konfiguracji (scope, audience, sekret); nie udawaj „nie znaleziono” |
| 5xx, timeout, brak połączenia | wyjątek (`HttpRequestException`, `TimeoutRejectedException`, `BrokenCircuitException`) | resilience już ponowiła; `FailSafeCache` zwróci ostatnią znaną wartość, jeśli jest; inaczej `DownstreamUnavailableExceptionHandler` zamieni wyjątek na `503 downstream.unavailable` albo `504 downstream.timeout` |

Cache w ACL:

- **Wariant A:** odpowiedź zależy od użytkownika (redaktor dostaje też szkice). Współdzielony klucz cache wolno stosować tylko do
  danych, które są takie same dla wszystkich; w przykładzie ACL musi odrzucić materiał, który nie jest opublikowany, zanim trafi do
  cache. Dane osobowe (np. wpisy dziennika) nigdy nie trafiają do cache wspólnego dla użytkowników.
- Klucz z prefiksem **konsumenta** (`sleepdiary:`) i wersją; brak tagów, bo konsument nie dostaje zdarzeń o zmianie tytułu
  (o świeżości decyduje `Fresh`). Jeśli dostawca publikuje zdarzenie o zmianie, możesz je skonsumować i unieważniać tagiem.
- Wynik `null` (404) też jest cache'owany przez `Fresh`; materiał opublikowany w tym czasie będzie widoczny z opóźnieniem.
  Jeśli to nieakceptowalne, nie cache'uj `null`.
- Danych z cache **nie** używaj do decyzji w komendach (cache tylko po stronie odczytu, [11.8](../11-cache.md#118-czego-nigdy-nie-cacheujemy)).
  W komendzie wołaj dostawcę bez cache albo, lepiej, przenieś sprawdzenie do odczytu.
- Wywołanie w handlerze komendy odbywa się przy otwartej transakcji bazy (`TransactionBehavior` otwiera ją przed handlerem):
  trzymaj je krótkie i wykonuj przed zmianą agregatu.

## Krok 6: rejestracja

Rejestracja jest taka sama jak w BFF (`src/Bff/Example.Bff/Program.cs`); różni się tylko sposób uwierzytelnienia wywołań. Po
`AddDownstreamApi` wybierasz **dokładnie jeden**:

```csharp
// SleepDiary.Infrastructure/InfrastructureServiceCollectionExtensions.cs, w AddSleepDiaryInfrastructure (nie w Core)
services.AddScoped<IKnowledgeGateway, KnowledgeGateway>();

// Wariant A (domyślny): token użytkownika z bieżącego żądania, bez zmian (ADR-0040).
services.AddDownstreamApi<IKnowledgeApi>(configuration, "Knowledge").AddUserTokenForwarding();

// Wariant B (wyjątek, wywołanie systemowe): token client credentials klienta technicznego.
services.AddDownstreamApi<IKnowledgeApi>(configuration, "Knowledge").AddClientCredentialsToken("knowledge-client");
```

- `AddDownstreamApi` ustawia adres z `Downstream:Knowledge:BaseAddress` (brak albo adres względny zatrzymuje start), JSON z enumami
  jako tekst, daty ISO w ścieżce i query (`DownstreamUrlParameterFormatter`) oraz `AddStandardResilienceHandler` z retry tylko dla
  metod idempotentnych (`DisableForUnsafeHttpMethods`, ADR-0014). Rejestruje też `DownstreamUnavailableExceptionHandler`.
- Handler tokenu dodany po `AddDownstreamApi` leży wewnątrz potoku odporności, więc każda próba (także ponowienie) przechodzi
  przez niego.
- (Wariant A) `AddUserTokenForwarding()` przepisuje nagłówek `Authorization: Bearer` z bieżącego żądania HTTP przez
  `IDownstreamTokenProvider`. Poza żądaniem HTTP (Worker, konsument) tokenu nie ma i wywołanie idzie bez nagłówka; dostawca odrzuci
  je, chyba że operacja jest anonimowa. Z Workera używaj wariantu B. Nie pisz własnego handlera w serwisie: przejście na token
  exchange to podmiana implementacji `IDownstreamTokenProvider` w jednym miejscu (ADR-0040).
- (Wariant B) `AddClientCredentialsToken("knowledge-client")` dokłada `Authorization: Bearer` z tokenem pobranym z CIAM i trzymanym
  w pamięci do 60 s przed wygaśnięciem; równoległe żądania czekają na jedno pobranie tokenu. Nazwa wskazuje sekcję
  `ClientCredentials:knowledge-client`.
- Rejestracja w `Add{Serwis}Infrastructure`, nie w `Add{Serwis}Core`: testy integracyjne budują kontener z `Core` i podstawiają
  fake portu.
- Generowanie OpenAPI przy buildzie uruchamia aplikację bez konfiguracji. `AddDownstreamApi` sam podstawia wtedy adres zastępczy
  (`BuildTimeDocumentGeneration.IsActive`), ale walidacja `ClientCredentials` (sprawdzana przy starcie hosta) zatrzymałaby build:
  w wariancie B pomiń rejestrację tokenu przy `BuildTimeDocumentGeneration.IsActive`, tak jak robi to `AddAppMessaging`.

Konfiguracja (`appsettings.json` Api i Workera; w wariancie A wystarczy sekcja `Downstream`, sekret wariantu B wyłącznie z Vault
jako `ClientCredentials__knowledge-client__ClientSecret`):

```json
"Downstream": {
  "Knowledge": { "BaseAddress": "http://knowledge-api.knowledge.svc.cluster.local:8080" }
},
"ClientCredentials": {
  "knowledge-client": {
    "TokenEndpoint": "https://sso.example.com/realms/superapp/protocol/openid-connect/token",
    "ClientId": "sleepdiary-client",
    "Scope": "knowledge.system.read"
  }
}
```

Nazwa sekcji `ClientCredentials` to nazwa **dostawcy** (`knowledge-client`), a `ClientId` to klient **wywołującego**
(`sleepdiary-client`). Lokalnie: w `appsettings.Development.json` `Downstream:Knowledge:BaseAddress` = `http://localhost:5101`,
`TokenEndpoint` = `http://localhost:8081/realms/superapp/protocol/openid-connect/token`, sekret `dev-sleepdiary-client-secret` przez user
secrets lub `.env` compose; w compose `Downstream__Knowledge__BaseAddress: http://knowledge-api.knowledge.svc.cluster.local:8080`
(alias sieciowy kontenera), a w Helm zmienna środowiskowa o tej samej nazwie. Brak `TokenEndpoint`, `ClientId` albo `ClientSecret`
zatrzymuje start hosta komunikatem „ClientCredentials:knowledge-client wymaga TokenEndpoint (absolutny URI), ClientId i
ClientSecret.”.

## Krok 7: użycie

Handler zapytania (Infrastructure) albo komendy (Application) wstrzykuje `IKnowledgeGateway`, nigdy `IKnowledgeApi`:

```csharp
if (!(await knowledge.GetPublishedMaterialAsync(query.MaterialId, cancellationToken)).TryGetValue(out var material, out var error))
{
    return error;
}
```

## Krok 8: testy

- **Application.Tests:** handler z fake'iem `IKnowledgeGateway` (sukces, `MaterialNotFound`).
- **Test ACL:** `KnowledgeGateway` z fake'iem wygenerowanego `IKnowledgeApi` zwracającym `IApiResponse<T>` dla 200, 404, 403,
  500: mapowanie na model, błąd konsumenta, wyjątki. Wzór budowania odpowiedzi Refit w teście: `src/Bff/Example.Bff.Tests/Responses.cs`;
  wzór sprawdzenia, że klient z `AddDownstreamApi` wysyła daty ISO i przekazuje token: `DownstreamClientTests`. Fail-safe sprawdzasz, każąc fake'owi rzucić `HttpRequestException` przy
  przeterminowanym wpisie (zegar `IClock`).
- **Integracyjnie (opcjonalnie):** dostawca przez `WebApplicationFactory` albo zaślepka HTTP (ADR-0014).
- `dotnet build SuperApp.slnx` i `dotnet test --solution SuperApp.slnx`, w tym testy architektury.

## Checklista

- [ ] Uzasadnienie, dlaczego nie zdarzenia / lokalna kopia; plan zaakceptowany.
- [ ] Cel wywołania dozwolony: serwis tej samej experience albo API wewnętrzne BFF innej experience; nigdy serwis domenowy innej
      experience; system zewnętrzny tylko z własnymi poświadczeniami.
- [ ] Wariant A (token użytkownika) tam, gdzie jest użytkownik; wariant B tylko dla jawnie oznaczonych operacji systemowych, z audytem
      `azp` i uzasadnieniem w kontrakcie; żadnego identyfikatora użytkownika w payloadzie zamiast tokenu.
- [ ] (Wariant B) Operacja dostawcy oznaczona scope `{dostawca}.system.{akcja}` (ADR-0042); scope przydzielony tylko klientowi
      `{konsument}-client` lokalnie (realm) i zgłoszony do CIAM.
- [ ] Obie strony mają tę samą wartość `experience` w chartach (NetworkPolicy, ADR-0041).
- [ ] Klient wygenerowany z commitowanego kontraktu dostawcy; tylko używane operacje; typy `internal`; kod commitowany.
- [ ] Port w Application w języku konsumenta, `Result` z błędami konsumenta; obce DTO tylko w `Infrastructure/Integrations/...`.
- [ ] 404 → błąd konsumenta; 401/403/5xx/timeout → wyjątek; retry tylko GET, HEAD, OPTIONS.
- [ ] Rejestracja przez `AddDownstreamApi` + `AddUserTokenForwarding()` (A) albo `AddClientCredentialsToken` (B), bez własnych handlerów tokenu.
- [ ] Sekret tylko z Vault; adres (`Downstream:{Dostawca}:BaseAddress`) i `ClientId` w konfiguracji, compose i Helm.
- [ ] Cache (jeśli jest) z prefiksem konsumenta i wersją; nieużywany do decyzji w komendach; bez danych zależnych od użytkownika
      pod wspólnym kluczem.
- [ ] Testy: fake portu, fake interfejsu Refit; build i testy zielone.
