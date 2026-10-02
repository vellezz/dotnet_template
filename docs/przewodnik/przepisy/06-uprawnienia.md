# Przepis 06: uprawnienia (scope), reguły dostępu do zasobu, endpoint publiczny

**Kiedy:** nowa operacja ma być dostępna tylko dla części użytkowników lub klientów, operacja ma działać tylko na danych
wywołującego, albo dane mają być dostępne bez logowania.
**Najpierw przeczytaj:** [9 Bezpieczeństwo](../09-bezpieczenstwo.md), zwłaszcza [9.6 Autoryzacja](../09-bezpieczenstwo.md#96-autoryzacja-trzy-poziomy)
i [9.7 Scope](../09-bezpieczenstwo.md#97-scope-konwencja-i-dodawanie).
**Przykłady z kodu:** `KnowledgeScopes`, `SleepDiaryScopes`, `GetMaterialHandler` (redaktor i czytelnik), `UpdateSleepEntryHandler`
i `GetSleepEntryHandler` (właściciel wpisu).

## Szybki start: `dotnet superapp add scope`

```bash
dotnet superapp add scope Knowledge note.write   # stała w KnowledgeScopes, client scope w realmie, scope żądany przez bff-web
```

Narzędzie dopasowuje się do stylu klasy scope: `Prefix + "…"` w serwisach z szablonu, pełny literał w Knowledge. Opis scope,
`[RequiresScope]` i przekazanie listy działowi CIAM opisuje część A. `remove scope … --yes` odmawia, dopóki komenda lub zapytanie
wymaga scope. [22 Narzędzie](../22-narzedzie-superapp.md).

## Krok 0: co właściwie potrzebujesz

| Potrzeba | Rozwiązanie | Sekcja |
|---|---|---|
| operacja w obszarze, który ma już scope (kolejna operacja redaktora katalogu) | istniejący scope w `[RequiresScope]`; nic w CIAM | A, tylko kroki 2–4 |
| nowa grupa uprawnień nadawana innym osobom/klientom (np. moderatorzy ocen) | **nowy scope** | A |
| scope tylko dla użytkowników z rolą (jak `knowledge.catalog.write`) | nowy scope + mapowanie na rolę w CIAM | A, krok 5b |
| „tylko własne dane”, „redaktor widzi też szkice” | reguła zasobu w handlerze/agregacie, bez nowego scope | B |
| dane dostępne bez logowania | endpoint publiczny (trzy poziomy zgody) | C |
| nowy serwis (nowy prefiks scope) | [przepis 07](07-nowy-serwis.md) (prefiks w polityce experience `GatewayPolicies.Example`, klient w BFF) i potem A | |
| dostęp innej experience do naszych danych | operacja API wewnętrznego BFF ze scope `{experience}.internal.*` | [przepis 11](11-nowa-experience-i-bff.md) |

Dwa poziomy w serwisie (ADR-0012, ADR-0017):

| Poziom | Pytanie | Gdzie | Błąd |
|---|---|---|---|
| gruboziarnisty | „czy klient może wykonać ten rodzaj operacji?” | `[RequiresScope]` na komendzie/zapytaniu (brama sprawdza tylko, czy token ma scope któregoś serwisu experience; BFF API publicznego nie sprawdza scope) | 403 `auth.missing_scope` |
| drobnoziarnisty | „czy ten użytkownik może ten konkretny zasób?” | handler lub agregat, przez `ICurrentUser` | 404 (nie ujawniaj istnienia) albo 403 z własnym kodem |

---

## A. Nowy scope

Przykład w tym przepisie: hipotetyczny scope `knowledge.ratings.write` (użytkownik może oceniać materiały). Nazwa według
konwencji `{serwis}.{zasób}.{akcja}`; zasób to obszar uprawnień, nie musi być agregatem.

### Krok 1: stała w `{Serwis}Scopes`

`src/Services/Knowledge/Knowledge.Application/KnowledgeScopes.cs`; nigdy literał w atrybucie ani w handlerze:

```csharp
/// <summary>
/// Rate published materials (<c>knowledge.ratings.write</c>): add, change and remove the caller's own rating.
/// </summary>
/// <remarks>Ratings always belong to the user identified by the token's <c>sub</c> claim.</remarks>
public const string RatingsWrite = "knowledge.ratings.write";
```

Opis po angielsku, dla osoby, która nie zna kodu: co pozwala zrobić, na czyich danych, czy zmienia wyniki innych zapytań.

### Krok 2: atrybut na komendzie lub zapytaniu

```csharp
/// <summary>Command: sets the current user's rating of a published material.</summary>
/// <remarks>
/// Requires the scope <see cref="KnowledgeScopes.RatingsWrite"/>. Errors: <c>auth.missing_scope</c> (403), <c>validation.failed</c> (400),
/// <c>knowledge.material.not_found</c> (404) ...
/// </remarks>
[RequiresScope(KnowledgeScopes.RatingsWrite)]
public sealed record RateMaterial(Guid MaterialId, int Stars) : ICommand;
```

- Jeden scope na żądanie; atrybut na **typie żądania**, nie na kontrolerze (`[Authorize]` w kontrolerach serwisów nie jest używane;
  jedyny wyjątek to polityka scope API wewnętrznego BFF, `[Authorize(Policy = ExampleBffScopes.InternalRead)]`).
- Każde żądanie ma `[RequiresScope]` albo (wyjątkowo) `[AllowAnonymousRequest]`. Żądanie bez żadnego atrybutu wymaga tylko
  uwierzytelnienia; nie zostawiaj tego przypadkowi.
- Zapytanie redaktorskie, które ma też działać dla czytelników, deklaruje scope odczytu (`CatalogRead`), a różnicę robi reguła zasobu (sekcja B).

### Krok 3: dokumentacja akcji kontrolera

W `<remarks>` akcji `Required scope: <c>knowledge.ratings.write</c>.`, w `<response code="403">` kod `auth.missing_scope`.
`[ProducesResponseType]` dla 401/403 jest już na kontrolerze ([8.6](../08-api-i-kontrakty.md#86-metadane-odpowiedzi-producesresponsetype)).
Po `dotnet build` sprawdź opis w `openapi/Knowledge.Api.json`.

### Krok 4: testy

Kolejność behaviors gwarantuje, że brak scope daje 403 przed walidacją. Test jednostkowy z prawdziwym pipeline'em i fake'iem
użytkownika (wzór: `Knowledge.Application.Tests/PipelineTests`, `Fakes/FakeCurrentUser`):

```csharp
[Fact]
public async Task Rating_requires_the_ratings_scope()
{
    var result = await SendAsync(new RateMaterial(Guid.NewGuid(), 5), scopes: [KnowledgeScopes.CatalogRead]);

    Assert.Equal("auth.missing_scope", result.Error.Code);
}
```

W testach integracyjnych scope ustawiasz na użytkowniku fixture'a (`Knowledge.IntegrationTests`, `ServiceFixtureExtensions.ActWith`):

```csharp
fixture.ActWith(KnowledgeScopes.CatalogRead, KnowledgeScopes.RatingsWrite);
var rated = await fixture.SendAsync(new RateMaterial(materialId, 5));
Assert.True(rated.IsSuccess);
```

### Krok 5: lokalny CIAM (`deploy/local/keycloak/realm-superapp.json`)

**5a. Client scope.** Dopisz do tablicy `clientScopes` obiekt wzorowany na istniejącym `knowledge.catalog.read`. Mapper audience
jest obowiązkowy: to on dodaje `knowledge-api` do `aud`, bez niego token z samym nowym scope zostanie odrzucony przez serwis (401).
Pola `id` możesz pominąć (Keycloak nada je przy imporcie):

```json
{
  "name": "knowledge.ratings.write",
  "description": "Scope serwisu (knowledge-api)",
  "protocol": "openid-connect",
  "attributes": {
    "include.in.token.scope": "true",
    "display.on.consent.screen": "false"
  },
  "protocolMappers": [
    {
      "name": "audience knowledge-api",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-audience-mapper",
      "consentRequired": false,
      "config": {
        "id.token.claim": "false",
        "access.token.claim": "true",
        "introspection.token.claim": "true",
        "included.custom.audience": "knowledge-api",
        "userinfo.token.claim": "false"
      }
    }
  ]
}
```

Następnie dopisz nazwę scope do `optionalClientScopes` klientów, które mają móc o niego prosić: `bff-web`, `mobile-android`,
`mobile-ios`, `dev-cli` (i `{serwis}-client` tylko dla wywołań systemowych bez użytkownika, które są wyjątkiem; wywołania w
kontekście użytkownika przekazują token użytkownika, [9.8](../09-bezpieczenstwo.md), ADR-0040).

**5b. Scope tylko dla roli** (wzór: `knowledge.catalog.write` i rola `knowledge-editor`). Dodaj rolę do `roles.realm`, mapowanie do
`scopeMappings` i rolę użytkownikowi testowemu:

```json
"scopeMappings": [
  { "clientScope": "knowledge.catalog.write", "roles": ["knowledge-editor"] },
  { "clientScope": "knowledge.ratings.write", "roles": ["knowledge-rater"] }
]
```

Z mapowaniem Keycloak wydaje scope tylko użytkownikom z rolą; pozostali go po prostu nie dostaną, choć klient o niego prosi.

**5c. Załaduj realm.** Realm importuje się przy starcie (`--import-realm`) i tylko wtedy, gdy realmu jeszcze nie ma. Keycloak
lokalnie nie ma wolumenu, więc wystarczy odtworzyć kontener (dane MSSQL zostają):

```bash
docker compose -f deploy/local/docker-compose.yml up -d --force-recreate keycloak
```

Zwykły restart (`docker compose restart keycloak`) **nie** wczyta zmian. Odtworzenie unieważnia wszystkie sesje SSO, więc sesje bramy
(`bff-web`) przestaną się odświeżać (ponowne logowanie). `docker compose down -v` też działa, ale kasuje bazę MSSQL.

### Krok 6: brama `bff-web` prosi o nowy scope

Lokalna brama w profilu `bff-web` (zamiennik wspólnej bramy brzegowej, ADR-0037) prosi CIAM wyłącznie o scope z listy `Authentication:Scopes`. Dopisz kolejny indeks we wszystkich trzech miejscach:

| Plik | Wpis |
|---|---|
| `src/Gateway/SuperApp.Gateway/Properties/launchSettings.json`, profil `bff-web` | `"Authentication__Scopes__6": "knowledge.ratings.write"` |
| `deploy/local/docker-compose.yml`, usługa `bff-web` | `Authentication__Scopes__6: knowledge.ratings.write` |
| `deploy/helm/superapp-gateway/values-bff-web.yaml` | pozycja w `authentication.scopes` |

Brama prosi o wszystkie scope, także te zależne od roli: CIAM wyda tylko dozwolone. Nie dodawaj `offline_access` ([9.3](../09-bezpieczenstwo.md#93-bff-web)).
Aplikacje mobilne (gdy powstaną) dopisują scope do konfiguracji żądania autoryzacji AppAuth.

### Krok 7: sprawdź lokalnie

```bash
TOKEN=$(curl -s http://localhost:8081/realms/superapp/protocol/openid-connect/token \
  -d grant_type=password -d client_id=dev-cli -d username=reader -d password=reader \
  -d "scope=openid knowledge.catalog.read knowledge.ratings.write" \
  | python3 -c "import json,sys; t=json.load(sys.stdin); print(t['scope'], file=sys.stderr); print(t['access_token'])")
# na stderr: openid knowledge.catalog.read knowledge.ratings.write profile email

curl -s -i -X PUT http://localhost:5101/v1/materials/<id>/rating -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"stars":5}'
```

Port 5101 to wywołanie serwisu z pominięciem BFF, tylko do debugowania. Gdy operacja jest już wystawiona w BFF
([przepis 01, krok 9](01-endpoint-komendy.md)), sprawdź ją też przez `http://localhost:5120/v1/knowledge/...` z tym samym tokenem:
BFF nie sprawdza scope API publicznego, więc 403 `auth.missing_scope` przychodzi z serwisu bez zmian.

Bez scope w tokenie oczekiwana odpowiedź:

```json
{"title":"Brak wymaganego uprawnienia 'knowledge.ratings.write'.","status":403,"instance":"/v1/materials/.../rating","code":"auth.missing_scope","traceId":"..."}
```

Przez bramę (`bff-web`): wyloguj się i zaloguj ponownie (`/bff/login`), potem `GET /bff/user` musi pokazać nowy scope w `scopes`.

### Krok 8: zgłoszenie do działu CIAM (dev, test, prod)

Środowiska współdzielone konfiguruje osobny dział (ADR-0016); bez zgłoszenia scope nie będzie istniał poza Twoją maszyną. Treść:

```text
Nowy scope: knowledge.ratings.write
Resource server / audience: knowledge-api (scope ma dodawać audience knowledge-api do access tokenu)
Opis: ocenianie opublikowanych materiałów przez użytkownika (własne oceny)
Klienci, którzy mogą o niego prosić: bff-web, mobile-android, mobile-ios
Komu wydawać: wszystkim użytkownikom   |   tylko użytkownikom z rolą <rola>
W tokenie: claim scope (include.in.token.scope), bez ekranu zgody
Środowiska i termin: dev, test, prod przed wdrożeniem wersji X
Wzorzec: deploy/local/keycloak/realm-superapp.json (client scope knowledge.ratings.write)
```

### Krok 9: wdrożenie

- Kolejność: scope w CIAM → konfiguracja bramy z nową listą scope (lokalnie `bff-web`; na środowiskach wymaganie wobec wspólnej
  bramy, ADR-0037) → serwis z operacją → BFF experience z akcją.
- **Zalogowani użytkownicy nie mają nowego scope**, dopóki nie zalogują się ponownie (odświeżenie tokenu zachowuje zestaw scope
  z logowania). Dla nowej operacji to zwykle akceptowalne; jeśli nowym scope zabezpieczasz **istniejącą** operację, wszyscy zalogowani
  dostaną 403 do czasu ponownego logowania. Taką zmianę uzgodnij i zaplanuj (to zmiana łamiąca dla klientów).

---

## B. Reguła dostępu do zasobu

Scope mówi „jaki rodzaj operacji”; reguła zasobu mówi „który zasób”. Należy do handlera (kto pyta, filtr w zapytaniu) albo do
agregatu (reguła biznesowa z identyfikatorem użytkownika przekazanym jako argument).

### Tylko własne dane (wzór: SleepDiary, biblioteka Knowledge)

1. **Komenda i zapytanie nie mają pola z identyfikatorem użytkownika.** Właściciel zawsze z tokenu.
2. Handler komendy pobiera właściciela przez `RequireUserId()` (helper `CurrentUserExtensions` w Application serwisu) i ładuje agregat
   po kluczu zawierającym właściciela:

   ```csharp
   if (!currentUser.RequireUserId().TryGetValue(out var userId, out var userError))
   {
       return userError;                                   // auth.unauthenticated (403), gdy brak sub
   }

   var entry = await entries.FindAsync(userId, command.Date, cancellationToken);
   if (entry is null)
   {
       return SleepEntryErrors.NotFound;                   // także gdy wpis istnieje, ale jest cudzy
   }
   ```

3. Handler zapytania (Infrastructure) filtruje read model po `Subject` w **każdym** zapytaniu:

   ```csharp
   if (currentUser.Subject is not { } subject)
   {
       return AuthorizationErrors.Unauthenticated;
   }

   var row = await db.Entries.FirstOrDefaultAsync(entry => entry.UserId == subject && entry.Date == query.Date, cancellationToken);
   ```

4. Trasa w kontrolerze bez ID użytkownika: `v1/entries/{date}`, `v1/me/favorites`.
5. Test integracyjny: dwóch użytkowników (zmiana `fixture.CurrentUser.Subject`), drugi dostaje `NotFound` dla zasobu pierwszego.

### Widoczność zależna od scope (wzór: `GetMaterialHandler`)

```csharp
if (currentUser.HasScope(KnowledgeScopes.CatalogWrite))
{
    var any = await LoadAsync(query.MaterialId, publishedOnly: false, cancellationToken);   // redaktor: każdy status, bez cache
    return any is null ? MaterialErrors.NotFound : any;
}

// czytelnik: tylko Published, z cache; szkic = MaterialErrors.NotFound
```

Wynik zależny od uprawnień nie może trafić do wspólnego klucza cache: cache'uj tylko wersję dla najsłabszych uprawnień albo
klucz z rozróżnieniem.

### 404 czy 403

| Sytuacja | Zwróć |
|---|---|
| zasób cudzy albo niewidoczny w tym statusie | `NotFound` (404): nie ujawniaj istnienia |
| zasób widoczny dla wszystkich, ale operacja zabroniona temu użytkownikowi | `Error.Forbidden("{serwis}.{pojęcie}.{problem}", ...)` (403) z własnym kodem |
| operacja wymaga użytkownika, a wywołuje system (Worker, client credentials) | `AuthorizationErrors.Unauthenticated` (403) |

Pamiętaj: w Workerze `ICurrentUser` to system z **wszystkimi** scope i bez `Subject`; konsument nie powinien wysyłać komend opartych
na właścicielu.

---

## C. Endpoint publiczny (rzadko)

Dane bez logowania wymagają zgody na czterech poziomach; brak któregokolwiek daje 401/403:

1. **Komenda/zapytanie:** `[AllowAnonymousRequest]` zamiast `[RequiresScope]`; handler nie zakłada, że `currentUser.Subject` istnieje.
2. **Akcja kontrolera:** `[AllowAnonymous]` (API ma politykę fallback „uwierzytelniony”); w kontrakcie usuń dla tej akcji opis 401/403
   albo opisz, że nie występują.
3. **BFF experience:** akcja przekazująca z `[AllowAnonymous]` (BFF też ma politykę fallback „uwierzytelniony”, `AddAppApi`).
   Bez tokenu w żądaniu klient Refit wywołuje serwis bez nagłówka `Authorization` (`IDownstreamTokenProvider` zwraca `null`), więc
   operacja serwisu musi być anonimowa (poziomy 1–2).
4. **Brama:** osobna trasa z polityką `GatewayPolicies.Anonymous` (`"anonymous"`, kolumna `AuthorizationPolicy` jest NOT NULL, więc
   trasa nigdy nie staje się publiczna przez pominięcie). W `ProxyConfigurationSeed` dodaj dla obu profili trasę o węższej ścieżce
   i niższym `Order` niż trasa experience (np. `Path = "/api/example/v1/knowledge/public/{**rest}"`, `Order = 50`, `RateLimiterPolicy =
   GatewayRateLimits.PerUser`, który dla anonimowych liczy po IP), z transformem `PathRemovePrefix` `/api/example` i do tego samego
   klastra `example` (BFF), i wygeneruj migrację bramy (`dotnet ef migrations add <Nazwa> -p src/Gateway/SuperApp.Gateway`, ADR-0022).
   Trasa prowadzi do API publicznego BFF, nigdy bezpośrednio do serwisu ani do `/internal` (ADR-0039). Dziś seed generuje tylko jedną
   trasę na experience, więc taka trasa wymaga rozszerzenia seeda; to także wymaganie wobec wspólnej bramy (ADR-0037).

W profilu `bff-web` bramy `X-CSRF: 1` jest wymagany także na publicznych `/api/*`. Endpoint publiczny przejdź z zespołem przed implementacją: to zmiana
powierzchni ataku.

---

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| 403 `auth.missing_scope`, choć scope jest w realmie | brama `bff-web` nie prosi o scope (krok 6) albo użytkownik zalogowany przed zmianą | dodaj do listy scope bramy; zaloguj ponownie |
| 401 `invalid audience` z tokenem zawierającym tylko nowy scope | brak mappera audience w client scope | dodaj mapper `oidc-audience-mapper` (krok 5a) |
| zmiany w `realm-superapp.json` nie działają | Keycloak tylko zrestartowany; import pomija istniejący realm | `up -d --force-recreate keycloak` |
| każdy użytkownik dostaje scope roli | brak wpisu w `scopeMappings` | krok 5b |
| `403 auth.forbidden` z bramy | token nie ma żadnego scope serwisów experience | prefiks nowego serwisu dopisz do polityki `GatewayPolicies.Example` (przepis 07) |
| 401 z BFF (port 5120), a serwis z tym samym tokenem odpowiada | token bez audience `example-bff` | klient musi mieć client scope `example-bff-audience` (lokalnie domyślny dla `bff-web`, `mobile-*`, `dev-cli`) |
| 403 `auth.missing_scope` z `/internal/...` BFF | token bez scope `example.internal.read` | poproś o scope (`-d "scope=... example.internal.read"`, opcjonalny dla `dev-cli`) |
| użytkownik widzi cudze dane | ID użytkownika z komendy/trasy albo zapytanie bez filtra po `Subject` | sekcja B |
| redaktor widzi u czytelników szkice | wynik zależny od scope trafił do wspólnego cache | cache tylko dla wersji czytelnika |
| komenda z konsumenta zwraca `auth.unauthenticated` | handler wymaga `Subject`, Worker go nie ma | nie wysyłaj takiej komendy z Workera |
| na dev/test scope nie istnieje | brak zgłoszenia do działu CIAM | krok 8 |

## Do zapamiętania

- Nowy scope: stała → `[RequiresScope]` → dokumentacja → testy → realm (scope z mapperem audience, klienci, ewentualnie rola) →
  lista scope bramy `bff-web` w trzech plikach → zgłoszenie do CIAM.
- Właściciel danych zawsze z tokenu; cudze lub niewidoczne = 404.
- Endpoint publiczny: cztery poziomy zgody (komenda, akcja serwisu, akcja BFF, osobna trasa `anonymous` w bramie).

Powiązane: [9 Bezpieczeństwo](../09-bezpieczenstwo.md), [8 API i kontrakty](../08-api-i-kontrakty.md),
[przepis 07 Nowy serwis](07-nowy-serwis.md), [przepis 09 Testy](09-testy.md), ADR-0012, ADR-0016, ADR-0017, ADR-0022, ADR-0031.
