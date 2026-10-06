# 21. Analityka produktowa i feature flags

**Czego się nauczysz:** jak system współpracuje z PostHog Cloud EU (analityka produktowa, session replay, feature flags); co
wolno wysłać do PostHog, a czego nigdy; jak powstaje pseudonim analityczny użytkownika i gdzie jest zwracany; jak działa proxy
`/ingest` w bramie `bff-web` i dlaczego jest w kodzie, a nie w trasach z bazy; jak forwarder zamienia zdarzenia integracyjne
na zdarzenia analityczne i dlaczego może obyć się bez inboxu; jak deklarować i czytać feature flags w serwisach (wartość
domyślna, zakres DI, timeout, fallback, metryka); jak skonfigurować analitykę lokalnie i na klastrze; jak mają się zachować
klienci web i mobile; jak to testować i obserwować.

**Wymagania wstępne:** [02 Architektura w praktyce](02-architektura-w-praktyce.md) (procesy i bramy),
[06 Warstwa aplikacji](06-warstwa-aplikacji.md) (handlery, `ICurrentUser`), [09 Bezpieczeństwo](09-bezpieczenstwo.md) (BFF,
`/bff/user`, scope), [10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md) (zdarzenia integracyjne, outbox, konsumenci).
Decyzja: [ADR-0036](../adr/0036-analityka-produktowa-i-feature-flags-posthog.md).

> **W skrócie**
>
> - Jedno narzędzie: **PostHog Cloud EU** (`eu.i.posthog.com`, zasoby `eu-assets.i.posthog.com`), osobny projekt PostHog na
>   środowisko. Analityka jest **wyłączona**, dopóki `Analytics:ProjectToken` jest pusty (tak jest lokalnie i w testach).
> - PostHog **nigdy** nie dostaje `sub`, e-maila ani nazwy. Użytkownik to pseudonim `u_` + 32 znaki hex z
>   `HMAC-SHA256(Analytics:IdKey, sub)` (`AnalyticsIdentity`). Zdarzenia bez użytkownika mają identyfikator `system`.
> - Trzy drogi danych: **przeglądarka** → `/ingest` w `bff-web` → PostHog; **aplikacje mobilne** → SDK PostHog; **backend** →
>   zdarzenia integracyjne → RabbitMQ → `SuperApp.AnalyticsForwarder` → PostHog. Serwisy domenowe niczego do PostHog nie wysyłają.
> - Zdarzenia zawierają tylko nazwy, identyfikatory obiektów katalogu i kategorie. **Żadnych danych z dziennika snu** ani
>   treści wpisanej przez użytkownika. Każda nowa właściwość przechodzi przegląd prywatności.
> - Flagi: `FeatureFlag(Key, DefaultValue)` w klasie `{Serwis}FeatureFlags`, odczyt przez port `IFeatureFlags`. Jedno zapytanie
>   do PostHog na zakres DI, timeout 1 s, przy problemie **wartość domyślna z kodu** + log 400/401 + metryka
>   `superapp.feature_flags.fallbacks`. Flaga nigdy nie psuje żądania i **nie jest uprawnieniem**.
> - Bez analityki flagi pochodzą z konfiguracji `FeatureFlags:{klucz}` (lokalnie `FeatureFlags__knowledge_material_ratings=true`).
> - Forwarder nie ma bazy ani inboxu: powtórzona wiadomość powtarza zdarzenie analityczne, co jest akceptowalne. Dostarczanie
>   jest „best effort”, więc logika biznesowa nigdy nie zależy od analityki.

> **Brama w tym rozdziale to lokalny zamiennik (ADR-0037).** Proxy `/ingest`, `analyticsId` w `/bff/user` i `GET /analytics/id`
> są dziś w `SuperApp.Gateway`, czyli w **lokalnym zamienniku** wspólnej bramy brzegowej super appki. Na klastrze nasz zespół tej bramy
> nie wdraża, więc te elementy są:
>
> - **wymaganiem wobec wspólnej bramy** (proxy `/ingest` do PostHog Cloud EU z tymi samymi regułami usuwania nagłówków), do
>   potwierdzenia z jej właścicielem, albo
> - jeśli wspólna brama ich nie zapewni, **nowymi endpointami BFF experience**: identyfikator analityczny jako
>   `GET /v1/analytics/id` w API publicznym BFF, a adres `/ingest` w konfiguracji modułu.
>
> BFF experience istnieje (`src/Bff/Example.Bff`), ale endpointów analityki nie ma: wspólna brama nie odpowiedziała jeszcze na
> to wymaganie. Opis poniżej dotyczy więc kodu lokalnej bramy. BFF nie wysyła zdarzeń do PostHog i nie referuje pakietu
> `PostHog` (reguła 11 testów architektury). Moduł działa w shellu super appki, który
> dostarcza tożsamość, flagi i zgodę; kontrakt z shellem proponuje [ADR-0045](../adr/0045-modul-w-super-appce-kontrakt-z-shellem.md)
> (status Proponowany, czeka na odpowiedzi zespołu shella); do jego akceptacji wzorce klientów z 21.8 i 21.9 dotyczą
> modułu uruchomionego samodzielnie (ADR-0038).

W blokach kodu z repozytorium pominięto komentarze dokumentacji XML (`///`); reszta jest skopiowana z plików wskazanych nad
blokiem. Kod Angulara, Kotlina i Swifta w tym rozdziale to **wzorzec do zastosowania przy budowie klientów**: katalogów `web/`
i `mobile/` jeszcze w repozytorium nie ma.

---

## 21.1 Po co i co jest gdzie

Produkt potrzebuje czterech rzeczy, których reszta architektury nie daje (ADR-0036):

| Potrzeba | Przykład | Kto zbiera |
|---|---|---|
| analityka produktowa | lejek „otwarcie materiału → dodanie do ulubionych”, retencja | klienci web i mobile (SDK PostHog) |
| session replay | nagranie sesji, w której użytkownik nie znalazł przycisku | klient web (posthog-js), opcjonalnie mobile |
| zdarzenia z backendu | „materiał opublikowany”, „wpis snu zapisany”: fakty potwierdzone przez serwis, których klient nie może wiarygodnie zgłosić | `SuperApp.AnalyticsForwarder` |
| feature flags | stopniowe udostępnianie funkcji, eksperyment, wyłącznik awaryjny, spójne między frontendem a backendem | serwisy (port `IFeatureFlags`) i klienci (SDK) |

```mermaid
flowchart LR
    subgraph Klienci
        SPA[Angular SPA<br/>posthog-js]
        MOB[Android / iOS<br/>posthog-android, posthog-ios]
    end
    subgraph Klaster
        BFF[bff-web<br/>/ingest, /bff/user]
        MGW[gateway-mobile<br/>GET /analytics/id]
        SVC[knowledge-api, sleepdiary-api,<br/>workery: IFeatureFlags]
        OUT[(outbox serwisu)]
        MQ[(RabbitMQ)]
        FWD[analytics-forwarder<br/>SuperApp.AnalyticsForwarder]
    end
    PH[(PostHog Cloud EU<br/>eu.i.posthog.com<br/>eu-assets.i.posthog.com)]

    SPA -- "zdarzenia, replay, flagi<br/>same-origin /ingest" --> BFF
    BFF -- "/ingest/** bez Cookie, Authorization,<br/>X-User-*, X-Forwarded-*" --> PH
    SPA -- "GET /bff/user → analyticsId" --> BFF
    MOB -- "GET /analytics/id (Bearer)" --> MGW
    MOB -- "SDK: zdarzenia, flagi" --> PH
    SVC -- "ewaluacja flag dla pseudonimu<br/>(timeout 1 s)" --> PH
    SVC -- "zdarzenie integracyjne<br/>w transakcji" --> OUT
    OUT -- "Worker serwisu" --> MQ
    MQ -- "kolejki analytics-*" --> FWD
    FWD -- "zdarzenia backendowe, partie" --> PH
```

| Element | Gdzie w repozytorium | Rola |
|---|---|---|
| `FeatureFlag`, `IFeatureFlags` | `src/Framework/SuperApp.Framework.Application/FeatureFlags/` | port flag dla handlerów; Application nie zna PostHog |
| `AnalyticsOptions`, `AnalyticsIdentity`, `HybridCacheFeatureFlags`, `ConfigurationFeatureFlags`, `AnalyticsServiceCollectionExtensions` | `src/Framework/SuperApp.Framework.Infrastructure/Analytics/` | ustawienia i ich walidacja, pseudonim, implementacje flag, rejestracja |
| `InfrastructureTelemetry.FeatureFlagFallbacks` | `src/Framework/SuperApp.Framework.Infrastructure/Telemetry/InfrastructureTelemetry.cs` | metryka `superapp.feature_flags.fallbacks` |
| `AddAppEventSubscriber` | `src/Framework/SuperApp.Framework.Infrastructure/Messaging/SubscriberMessagingServiceCollectionExtensions.cs` | MassTransit dla subskrybenta bez bazy (forwarder) |
| `AnalyticsEndpoints` | `src/Gateway/SuperApp.Gateway/Analytics/AnalyticsEndpoints.cs` | proxy `/ingest` (bff-web), `GET /analytics/id` (gateway-mobile) |
| `/bff/user` z `analyticsId` | `src/Gateway/SuperApp.Gateway/Bff/BffEndpoints.cs` | pseudonim dla SPA |
| forwarder | `src/Analytics/SuperApp.AnalyticsForwarder/` (`Program.cs`, `Events/`, `Consumers/`) | zdarzenia integracyjne → zdarzenia produktowe |
| testy | `src/Analytics/SuperApp.AnalyticsForwarder.Tests/`, reguły 10 i 11 w `tests/SuperApp.ArchitectureTests/ArchitectureRules.cs` | pseudonim, walidacja, flagi z konfiguracji, mapowanie konsumentów, granice zależności |
| wdrożenie | `deploy/helm/superapp-analytics-forwarder/`, komentarze sekretów w `deploy/helm/superapp-service/values.yaml` i `deploy/helm/superapp-gateway/values.yaml`, `deploy/local/docker-compose.yml`, `deploy/local/.env.example` | chart forwardera, sekrety `Analytics__*`, lokalne środowisko |

Pakiet `PostHog` 2.15.7 (MIT) jest w `Directory.Packages.props` i referencjonują go **tylko** `SuperApp.Framework.Infrastructure`
i `SuperApp.AnalyticsForwarder`. Pilnuje tego reguła 11 testów architektury (`Only_framework_and_forwarder_use_posthog`). Pakiety
klientów (`posthog-js`, `posthog-android`, `posthog-ios`) dodaje się razem z kodem klientów.

---

## 21.2 Prywatność i zgoda

SleepDiary przetwarza dane o zdrowiu (art. 9 RODO), a analityka i nagrania sesji w przeglądarce wymagają zgody użytkownika
(ePrivacy). PostHog jest zewnętrznym procesorem danych (umowę powierzenia zawiera organizacja). Dlatego reguły poniżej są
**twarde**, a nie zalecenia.

### Co wolno, a czego nigdy

| Do PostHog może trafić | Do PostHog nie trafia nigdy |
|---|---|
| nazwa zdarzenia z katalogu (`knowledge_material_published`) | `sub` z CIAM, e-mail, imię, nazwa użytkownika, token |
| pseudonim `u_…` albo `system` | treść wpisana przez użytkownika (tytuły, opisy, notatki, wyszukiwane frazy) |
| identyfikatory obiektów katalogu (`material_id`, `collection_id`) | **dane z dziennika snu**: data wpisu, pora snu i pobudki, czas snu, jakość, wybudzenia, notatki |
| kategorie i typy (`material_type = "Article"`) | adres IP klienta (projekt PostHog odrzuca IP, a proxy nie dokleja `X-Forwarded-For`) |
| czas faktu biznesowego, `source = backend`, `message_id` | identyfikatory wpisów snu i innych zasobów opisujących zdrowie |

Przykład z kodu: `SleepEntryRecordedV1` niesie `UserId`, `Date`, `SleepMinutes` i `Quality`, a zdarzenie
`sleepdiary_entry_recorded` informuje **tylko**, że wpis powstał. Test `Sleep_entry_recorded_belongs_to_the_user_and_carries_no_health_data`
pilnuje, że lista właściwości jest pusta (21.10).

Tytuł materiału też nie jest wysyłany (`MaterialPublishedConsumer`): analityka grupuje po identyfikatorze i typie, a tytuły
należą do katalogu. To zasada ogólna: wysyłasz minimum potrzebne do analizy, nie „wszystko, co jest w wiadomości”.

### Przegląd prywatności

Każda nowa właściwość zdarzenia (backendowego i klienckiego) przechodzi przegląd prywatności **przed** scaleniem zmiany.
W praktyce: w opisie pull requestu wymieniasz nazwę zdarzenia, każdą właściwość z przykładową wartością i uzasadnienie, po co
analizie jest potrzebna; recenzent sprawdza ją względem tabeli wyżej. Właściwość z danymi o zdrowiu nie przechodzi nigdy.

### Zgoda

- Klienci web i mobile **inicjalizują PostHog dopiero po zgodzie** użytkownika. Przed zgodą SDK nie istnieje, więc nie zapisuje
  niczego w przeglądarce ani na urządzeniu (ciasteczka, `localStorage`).
- Wycofanie zgody wyłącza zbieranie i usuwa lokalny stan SDK (21.8, 21.9).
- Zgoda dotyczy klientów. Zdarzenia backendowe i ewaluacja flag w serwisach używają pseudonimu i nie zapisują niczego na
  urządzeniu użytkownika; ich zakres opisuje polityka prywatności (zadanie poza repozytorium, ADR-0036).

### Session replay

Najwyższe ryzyko prywatności, dlatego warunki są obowiązkowe **przed** włączeniem nagrań na środowisku:

- maskowanie wszystkich pól formularzy i tekstów jest domyślne (zdejmujesz maskę świadomie, z elementów bez danych osobowych);
- ekrany dziennika snu i każde miejsce z danymi o zdrowiu są **w całości** wyłączone z nagrywania (nagrywanie zatrzymane, nie
  tylko zamaskowane);
- projekt PostHog ma włączone odrzucanie adresów IP klienta (ustawienie projektu, nie kodu).

---

## 21.3 Pseudonim analityczny

### Jak jest liczony

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Analytics/AnalyticsIdentity.cs
public sealed class AnalyticsIdentity(IOptions<AnalyticsOptions> options)
{
    public const string System = "system";

    private const string Prefix = "u_";

    public string? ForSubject(string? subject)
    {
        var settings = options.Value;
        if (string.IsNullOrEmpty(subject) || !settings.Enabled || string.IsNullOrEmpty(settings.IdKey))
        {
            return null;
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.IdKey), Encoding.UTF8.GetBytes(subject));
        return Prefix + Convert.ToHexStringLower(mac, 0, 16);
    }
}
```

- Pseudonim to `u_` + pierwsze 16 bajtów (128 bitów) HMAC-SHA256 zapisane jako 32 małe znaki hex, np.
  `u_3f9c0d1e8a7b6c5d4e3f2a1b0c9d8e7f`.
- **HMAC, a nie zwykły hash.** `SHA256(sub)` dałoby się odwrócić, przeliczając znane identyfikatory użytkowników. Bez klucza
  `Analytics:IdKey` (Vault, minimum 32 znaki) nie da się powiązać pseudonimu z kontem w CIAM.
- **Stabilny w środowisku.** Ten sam `sub` i ten sam klucz dają ten sam pseudonim w każdym procesie: w bramie, w serwisach,
  w forwarderze i u klientów. Dzięki temu lejek „kliknięcie w SPA → zdarzenie z backendu” i rollout flagi procentowej są spójne
  dla jednej osoby.
- **`null`**, gdy nie ma użytkownika albo analityka jest wyłączona. Klient, który dostał `null`, nie inicjalizuje PostHog.
- **`system`** to identyfikator zdarzeń i flag bez użytkownika (publikacja materiału, konsument w Workerze). Forwarder oznacza
  takie zdarzenia `$process_person_profile = false`, żeby PostHog nie zakładał dla nich osoby.

> **Klucza się nie rotuje** bez decyzji właściciela produktu. Zmiana `IdKey` zmienia pseudonim każdego użytkownika, więc
> w PostHog historia dzieli się na „starą” i „nową” osobę. Klucz musi być **ten sam** we wszystkich procesach środowiska
> (bramy, serwisy, forwarder); inny klucz w jednym procesie oznacza, że ta sama osoba ma tam inny pseudonim.

### Gdzie jest zwracany

Obie drogi są w lokalnej bramie; na klastrze to wymaganie wobec wspólnej bramy albo nowy `GET /v1/analytics/id` w BFF
experience, którego dziś w `Example.Bff` nie ma (ADR-0037).

| Endpoint | Brama | Dostęp | Odpowiedź |
|---|---|---|---|
| `GET /bff/user` | `bff-web` | sesja bramy (`__Host-bff`) | `{ sub, name, scopes, logoutUrl, analyticsId }`; `analyticsId` = `u_…` albo `null` |
| `GET /analytics/id` | `gateway-mobile` | ważny access token (bez niego `401`), limit `per-user` | `{ "analyticsId": "u_…" }` albo `{ "analyticsId": null }` |

```csharp
// src/Gateway/SuperApp.Gateway/Bff/BffEndpoints.cs (fragment)
bff.MapGet("/user", (HttpContext context, AnalyticsIdentity analytics) => Results.Ok(new
{
    sub = context.User.FindFirst("sub")?.Value,
    name = context.User.FindFirst("name")?.Value,
    scopes = context.User.FindAll("scope").SelectMany(claim => claim.Value.Split(' ')).Distinct().ToArray(),
    logoutUrl = LogoutUrl(context.User),
    analyticsId = analytics.ForSubject(context.User.FindFirst("sub")?.Value),
}))
    .RequireAuthorization();
```

`sub` w `/bff/user` jest dla SPA (np. do porównań w interfejsie). **Do PostHog idzie wyłącznie `analyticsId`**: nigdy
`posthog.identify(user.sub)`.

Przykłady (lokalnie, analityka włączona testowym tokenem, 21.12):

```http
GET https://localhost:5001/bff/user
Cookie: __Host-bff=CfDJ8...

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"sub":"accd5527-f904-4c13-b747-5cfe51a437e0","name":"Czytelnik Testowy","scopes":["openid","knowledge.catalog.read","..."],
 "logoutUrl":"/bff/logout?sid=20YGMKpLdb-xJo3pDwDT8LOA","analyticsId":"u_5b1f0c7e2d9a4e8b8c3d6f1a2b4c7e90"}
```

```http
GET https://localhost:5002/analytics/id
Authorization: Bearer eyJhbGciOiJSUzI1NiIs...

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"analyticsId":"u_5b1f0c7e2d9a4e8b8c3d6f1a2b4c7e90"}
```

Ten sam użytkownik dostaje ten sam pseudonim z obu bram (sprawdzone lokalnie). Bez tokenu `GET /analytics/id` kończy się
`401 auth.invalid_token` (polityka domyślna bramy wymaga uwierzytelnionego użytkownika). Przy wyłączonej analityce oba endpointy
działają i zwracają `"analyticsId": null`.

---

## 21.4 Proxy `/ingest` w bramie `bff-web`

> Proxy jest w lokalnym zamienniku wspólnej bramy. Opisane tu zachowanie (trasy, usuwane nagłówki, limit żądań, brak ciasteczek)
> to specyfikacja wymagania wobec wspólnej bramy brzegowej (ADR-0037).

### Po co

Angular wysyła zdarzenia na `/ingest` **we własnym originie** (`api_host: '/ingest'`), a brama przekazuje je do PostHog Cloud EU:

- blokery treści w przeglądarkach nie blokują żądań do własnej domeny aplikacji, więc dane analityczne są kompletne;
- polityka CSP aplikacji nie musi dopuszczać zewnętrznej domeny;
- brama kontroluje, co wychodzi: usuwa ciasteczko sesji, tokeny i nagłówki tożsamości oraz adres IP klienta.

### Dlaczego w kodzie, a nie w trasach z bazy

Trasy bramy są w bazie i zmienia się je migracjami (ADR-0022). Ten mechanizm ma dwie gwarancje, których proxy do PostHog
**nie może** mieć:

| Trasa z bazy (ADR-0022) | Proxy `/ingest` |
|---|---|
| cel **tylko w klastrze**: ograniczenie `CK_Destinations_ClusterAddress` przyjmuje wyłącznie `http://{serwis}.{namespace}.svc.cluster.local:{port}` | cel zewnętrzny (`https://eu.i.posthog.com`) |
| transform bezpieczeństwa **zawsze** dokleja `Authorization: Bearer <access token>` | token użytkownika **nigdy** nie może opuścić organizacji |

Gdyby dopisać PostHog do tras w bazie, trzeba by osłabić ograniczenie `CHECK` (każdy, kto pisze migracje bramy, mógłby
skierować ruch z tokenami gdziekolwiek) i dodać wyjątek w transformie. Dlatego proxy jest zdefiniowane w kodzie
(`MapForwarder`), z własnym, osobnym zestawem transformów, a trasy z bazy zachowują wszystkie gwarancje.

### Kod

```csharp
// src/Gateway/SuperApp.Gateway/Analytics/AnalyticsEndpoints.cs
internal static class AnalyticsEndpoints
{
    public const string IngestPrefix = "/ingest";

    public static void MapAnalyticsEndpoints(this WebApplication app, string profile)
    {
        var options = app.Services.GetRequiredService<IOptions<AnalyticsOptions>>().Value;

        if (profile == GatewayProfiles.Mobile)
        {
            app.MapGet("/analytics/id", (HttpContext context, AnalyticsIdentity identity) =>
                    Results.Ok(new { analyticsId = identity.ForSubject(context.User.FindFirst("sub")?.Value) }))
                .RequireAuthorization()
                .RequireRateLimiting(GatewayRateLimits.PerUser);
            return;
        }

        if (!options.Enabled)
        {
            return;
        }

        app.MapForwarder($"{IngestPrefix}/static/{{**path}}", options.AssetsHost.ToString(), ConfigureTransforms)
            .AllowAnonymous()
            .RequireRateLimiting(GatewayRateLimits.PerUser);
        app.MapForwarder($"{IngestPrefix}/{{**path}}", options.Host.ToString(), ConfigureTransforms)
            .AllowAnonymous()
            .RequireRateLimiting(GatewayRateLimits.PerUser);
    }

    // The request reaches PostHog with the browser library's own headers and body only: no session cookie, no token, no identity headers,
    // no client IP from the gateway. The Host header is the destination's (YARP's default).
    private static void ConfigureTransforms(TransformBuilderContext context)
    {
        context.UseDefaultForwarders = false;
        context.AddPathRemovePrefix(IngestPrefix);
        context.AddRequestHeaderRemove("Cookie");
        context.AddRequestHeaderRemove("Authorization");
        context.AddRequestTransform(transform =>
        {
            var removed = transform.ProxyRequest.Headers
                .Select(header => header.Key)
                .Where(name => name.StartsWith("X-User-", StringComparison.OrdinalIgnoreCase)
                               || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                               || name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                               || name.Equals("X-CSRF", StringComparison.OrdinalIgnoreCase))
                .ToList();
            removed.ForEach(name => transform.ProxyRequest.Headers.Remove(name));
            return ValueTask.CompletedTask;
        });
        context.AddResponseHeaderRemove("Set-Cookie");
    }
}
```

`Program.cs` bramy woła `app.MapAnalyticsEndpoints(profile)` **przed** `app.MapReverseProxy()`.

### Co dokładnie robi

| Element | Zachowanie | Dlaczego |
|---|---|---|
| `/ingest/static/{**path}` | do `Analytics:AssetsHost` (`https://eu-assets.i.posthog.com`), np. `/ingest/static/array.js` → `/static/array.js` | posthog-js z `api_host: '/ingest'` dociąga rozszerzenia (nagrywanie sesji, ankiety) z `{api_host}/static/...` |
| `/ingest/{**path}` | do `Analytics:Host` (`https://eu.i.posthog.com`), prefiks `/ingest` usunięty | zdarzenia, nagrania, flagi klienta |
| `AllowAnonymous()` | działa bez sesji | analityka przed zalogowaniem (po zgodzie) |
| `RequireRateLimiting("per-user")` | stałe okno 1 min, domyślnie 600 żądań (`Gateway:RateLimit:PermitPerMinute`), partycja po `sub`, a bez sesji po adresie IP połączenia; po przekroczeniu `429` | ochrona przed zalaniem proxy |
| żądanie: usunięte `Cookie`, `Authorization`, `X-User-*`, `X-Forwarded-*`, `Forwarded`, `X-CSRF`; `UseDefaultForwarders = false` | brama nie dokleja własnych `X-Forwarded-*`, a te przysłane przez klienta albo Ingress usuwa | PostHog nie dostaje sesji, tokenu, nagłówków tożsamości ani adresu IP klienta |
| odpowiedź: usunięty `Set-Cookie` | PostHog nie ustawia ciasteczek w domenie aplikacji | ciasteczka aplikacji należą tylko do bramy (`bff-web`) |
| tylko przy włączonej analityce | przy pustym `ProjectToken` ścieżka nie jest mapowana; trafia do polityki domyślnej (uwierzytelniony użytkownik), więc kończy się `401` | wyłączona analityka nie wypuszcza żadnego ruchu |
| CSRF | `CsrfHeaderMiddleware` dotyczy tylko `/api/*`; `/ingest` nie wymaga `X-CSRF: 1` | posthog-js nie dodaje własnych nagłówków; proxy i tak usuwa `X-CSRF` |

Przykład (analityka włączona, sprawdzone lokalnie):

```bash
curl -sk -i https://localhost:5001/ingest/static/array.js | head -5
# HTTP/1.1 200 OK
# Content-Type: application/javascript
# ... (brak Set-Cookie)
```

Ta sama komenda przy wyłączonej analityce zwraca `HTTP/1.1 401 Unauthorized` z `code` `auth.invalid_token` (ADR-0044).

---

## 21.5 Zdarzenia z backendu: `SuperApp.AnalyticsForwarder`

### Dlaczego osobny proces

Serwisy domenowe **nie** wysyłają zdarzeń analitycznych i nie zależą od PostHog. Fakty biznesowe i tak publikują jako zdarzenia
integracyjne (`{Serwis}.Contracts`) przez outbox. Forwarder je subskrybuje i tłumaczy na zdarzenia produktowe. Skutki:

- awaria PostHog albo zmiana modelu analityki nie dotyka komend: serwis nawet nie wie, że analityka istnieje;
- zdarzenie backendowe opisuje fakt **już zatwierdzony** w bazie i dostarczony przez outbox, a nie „próbę”;
- lista dozwolonych właściwości jest w jednym miejscu (konsumenci forwardera), łatwym do przeglądu.

Forwarder zna serwisy wyłącznie przez ich `Contracts` (published language). Pilnuje tego reguła 10 testów architektury
(`Analytics_forwarder_depends_only_on_contracts_of_services`); reguła 1 dopuszcza zależność od cudzych `Contracts`, a zabrania
zależności od pozostałych warstw innych serwisów.

### Przepływ

```mermaid
sequenceDiagram
    autonumber
    participant API as knowledge-api
    participant DB as MSSQL (knowledge.OutboxMessage)
    participant W as knowledge-worker
    participant MQ as RabbitMQ
    participant F as analytics-forwarder
    participant S as IProductEventSink
    participant PH as PostHog Cloud EU

    API->>DB: COMMIT: Material + MaterialPublishedV1 w outboxie
    W->>MQ: publikacja z outboxa (exchange Knowledge.Contracts:MaterialPublishedV1)
    MQ->>F: kolejka analytics-material-published
    F->>S: MaterialPublishedConsumer: ProductEvent("knowledge_material_published", {material_id, material_type})
    alt analityka włączona
        S->>S: PostHogProductEventSink: distinct id (u_… / system), source, message_id, kolejka w pamięci
        S-->>PH: partie w tle, z ponowieniami
    else analityka wyłączona
        S->>S: LoggingProductEventSink: log 9001 (nazwa, właściciel, nazwy właściwości)
    end
    F-->>MQ: ACK (konsument nie czeka na PostHog)
```

### `Program.cs`

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddAppServiceDefaults("analytics-forwarder");
builder.AddAppWorker();
builder.Services.AddAppAnalytics(builder.Configuration);
builder.Services.AddProductEventSink(builder.Configuration);
builder.Services.AddAppEventSubscriber(builder.Configuration, "analytics", bus => bus.AddConsumers(typeof(Program).Assembly));

var app = builder.Build();

app.MapAppDefaultEndpoints();

await app.RunAsync();
```

| Wywołanie | Co daje |
|---|---|
| `AddAppServiceDefaults("analytics-forwarder")` | **niegeneryczna** wersja bez bazy: OpenTelemetry, `IClock`, `ProblemDetails`, check `shutdown` (i `redis`, jeśli skonfigurowany); tak samo woła ją BFF experience. Serwisy i brama wołają wersję generyczną `AddAppServiceDefaults<TWriteDbContext>`, która woła tę i dodaje checki migracji i MSSQL. **Nie wołaj obu**: nazwy health checków muszą być unikalne. |
| `AddAppWorker()` | `ICurrentUser` = `SystemCurrentUser` |
| `AddAppAnalytics` | `AnalyticsOptions` z walidacją przy starcie, `AnalyticsIdentity`, klient PostHog (gdy włączona) |
| `AddProductEventSink` | `PostHogProductEventSink` + `PostHogFlushOnShutdown` albo `LoggingProductEventSink` |
| `AddAppEventSubscriber(..., "analytics", ...)` | MassTransit **bez outboxu i inboxu**: kolejki quorum o nazwach `analytics-{konsument}`, retry 100/500/1000/5000 ms |

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Messaging/SubscriberMessagingServiceCollectionExtensions.cs
public static IServiceCollection AddAppEventSubscriber(
    this IServiceCollection services,
    IConfiguration configuration,
    string subscriberPrefix,
    Action<IBusRegistrationConfigurator> configureConsumers)
{
    if (BuildTimeDocumentGeneration.IsActive)
    {
        return services;
    }

    services.AddMassTransit(bus =>
    {
        bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(subscriberPrefix, includeNamespace: false));
        configureConsumers(bus);

        bus.AddConfigureEndpointsCallback((_, _, endpoint) =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 5000));

            if (endpoint is IRabbitMqReceiveEndpointConfigurator rabbitMq)
            {
                rabbitMq.SetQuorumQueue();
            }
        });

        bus.UsingRabbitMq((context, rabbitMq) =>
        {
            var connectionString = configuration.GetConnectionString("RabbitMq");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Brak connection stringu RabbitMq.");
            }

            rabbitMq.Host(new Uri(connectionString));
            rabbitMq.ConfigureEndpoints(context);
        });
    });

    return services;
}
```

### Konsument: tylko mapowanie

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Consumers/MaterialPublishedConsumer.cs
public sealed class MaterialPublishedConsumer(IProductEventSink sink) : IConsumer<MaterialPublishedV1>
{
    public Task Consume(ConsumeContext<MaterialPublishedV1> context)
    {
        var message = context.Message;
        sink.Capture(new ProductEvent(
            ProductEventNames.KnowledgeMaterialPublished,
            message.PublishedAt,
            context.MessageId,
            Subject: null,
            new Dictionary<string, object>
            {
                ["material_id"] = message.MaterialId.ToString(),
                ["material_type"] = message.Type,
            }));
        return Task.CompletedTask;
    }
}
```

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Consumers/SleepEntryRecordedConsumer.cs
public sealed class SleepEntryRecordedConsumer(IProductEventSink sink) : IConsumer<SleepEntryRecordedV1>
{
    public Task Consume(ConsumeContext<SleepEntryRecordedV1> context)
    {
        var message = context.Message;
        sink.Capture(new ProductEvent(
            ProductEventNames.SleepDiaryEntryRecorded,
            message.RecordedAt,
            context.MessageId,
            message.UserId,
            new Dictionary<string, object>()));
        return Task.CompletedTask;
    }
}
```

Zasady konsumenta forwardera (inne niż w Workerach serwisów, [10.6](10-zdarzenia-i-integracja.md#106-konsumenci)):

- **Nie wysyła komendy MediatR.** Forwarder nie ma komend ani domeny, tylko mapowanie. To uzasadniony wyjątek od zasady
  „konsument deleguje do komendy” (ADR-0036).
- `OccurredAt` = czas faktu z wiadomości (`PublishedAt`, `ArchivedAt`, `RecordedAt`), nie czas przekazania.
- `Subject` = `sub` użytkownika, którego dotyczy zdarzenie, albo `null` dla zdarzenia systemowego. `sub` nie wychodzi z procesu:
  sink zamienia go na pseudonim, a sink logujący go nie loguje.
- Właściwości: klucze w snake case, wartości prymitywne (identyfikatory jako `string`), wyłącznie z listy dozwolonych.

### `ProductEvent` i sinki

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Events/ProductEvent.cs
public sealed record ProductEvent(
    string Name,
    DateTimeOffset OccurredAt,
    Guid? MessageId,
    string? Subject,
    IReadOnlyDictionary<string, object> Properties);
```

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Events/PostHogProductEventSink.cs
internal sealed partial class PostHogProductEventSink(IPostHogClient client, AnalyticsIdentity identity, ILogger<PostHogProductEventSink> logger)
    : IProductEventSink
{
    public void Capture(ProductEvent productEvent)
    {
        var properties = new Dictionary<string, object>(productEvent.Properties, StringComparer.Ordinal)
        {
            ["source"] = "backend",
        };
        if (productEvent.MessageId is { } messageId)
        {
            properties["message_id"] = messageId.ToString();
        }

        var distinctId = identity.ForSubject(productEvent.Subject);
        if (distinctId is null)
        {
            distinctId = AnalyticsIdentity.System;
            properties["$process_person_profile"] = false;
        }

        if (!client.Capture(distinctId, productEvent.Name, properties, groups: null, flags: null, timestamp: productEvent.OccurredAt))
        {
            LogNotQueued(logger, productEvent.Name);
        }
    }

    [LoggerMessage(9002, LogLevel.Warning, "Product event {EventName} was not queued for PostHog (queue full or client disposed)")]
    private static partial void LogNotQueued(ILogger logger, string eventName);
}
```

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Events/LoggingProductEventSink.cs
internal sealed partial class LoggingProductEventSink(ILogger<LoggingProductEventSink> logger) : IProductEventSink
{
    public void Capture(ProductEvent productEvent) =>
        LogNotSent(logger, productEvent.Name, productEvent.Subject is null ? "system" : "user", string.Join(",", productEvent.Properties.Keys));

    [LoggerMessage(9001, LogLevel.Information, "Analytics disabled: product event {EventName} ({EventOwner}, properties: {PropertyNames}) not sent")]
    private static partial void LogNotSent(ILogger logger, string eventName, string eventOwner, string propertyNames);
}
```

| Sink | Kiedy | Co robi |
|---|---|---|
| `PostHogProductEventSink` | `Analytics:ProjectToken` ustawiony | distinct id = pseudonim albo `system` (+ `$process_person_profile = false`), dodaje `source = backend` i `message_id`, znacznik czasu = czas faktu; `Capture` tylko kolejkuje w pamięci klienta; gdy kolejka pełna lub klient zamknięty: log 9002 |
| `LoggingProductEventSink` | analityka wyłączona (lokalnie, w testach) | log 9001 z nazwą zdarzenia, właścicielem (`system`/`user`) i **nazwami** właściwości; wartości i `sub` nie są logowane |

`PostHogFlushOnShutdown` (`IHostedService`) w `StopAsync` wywołuje `client.FlushAsync()`: przy SIGTERM (rollout, skalowanie
w dół) wysyła to, co zostało w kolejce. Usługi hostowane zatrzymują się w odwrotnej kolejności rejestracji, więc MassTransit
przestaje konsumować, zanim zacznie się opróżnianie. Czas jest ograniczony limitem zamykania hosta i
`terminationGracePeriodSeconds: 30` w chartcie.

### Katalog nazw: `ProductEventNames`

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder/Events/ProductEventNames.cs
public static class ProductEventNames
{
    public const string KnowledgeMaterialPublished = "knowledge_material_published";

    public const string KnowledgeMaterialArchived = "knowledge_material_archived";

    public const string KnowledgeCollectionArchived = "knowledge_collection_archived";

    public const string SleepDiaryEntryRecorded = "sleepdiary_entry_recorded";
}
```

| Zdarzenie | Z kontraktu | Właściciel | Właściwości (poza `source`, `message_id`) | Kolejka |
|---|---|---|---|---|
| `knowledge_material_published` | `MaterialPublishedV1` | `system` | `material_id`, `material_type` | `analytics-material-published` |
| `knowledge_material_archived` | `MaterialArchivedV1` | `system` | `material_id` | `analytics-material-archived` |
| `knowledge_collection_archived` | `CollectionArchivedV1` | `system` | `collection_id` | `analytics-collection-archived` |
| `sleepdiary_entry_recorded` | `SleepEntryRecordedV1` | użytkownik (`UserId` → pseudonim) | brak | `analytics-sleep-entry-recorded` |

Konwencja nazw: `{serwis}_{obiekt}_{czasownik w czasie przeszłym}`, snake case. Nazwy są **kontraktem z osobami budującymi
analizy** (wykresy, lejki, kohorty w PostHog). Nowe się dodaje, istniejących się nie zmienia; gdy zmienia się znaczenie, dodaje
się nową nazwę i przestaje wysyłać starą. Zdarzenia klientów opisujące interfejs mają tę samą konwencję bez prefiksu serwisu
(`material_opened`).

### „Best effort” i brak inboxu

Forwarder nie ma bazy, domeny ani inboxu. To świadoma decyzja, a nie przeoczenie:

| Sytuacja | Skutek | Dlaczego to akceptowalne |
|---|---|---|
| RabbitMQ dostarczy wiadomość ponownie (restart poda w trakcie, utrata ACK) | zdarzenie analityczne może się powtórzyć (z tym samym `message_id`) | konsumenci nie zmieniają żadnego stanu; dla statystyk pojedynczy duplikat nie ma znaczenia, a `message_id` pozwala go odfiltrować w analizie |
| PostHog chwilowo nie odpowiada | klient PostHog ponawia wysyłkę partii w tle | wiadomość z RabbitMQ jest już potwierdzona; komendy w serwisach nie są dotknięte |
| proces ginie bez czystego zatrzymania (OOM, `kill -9`) | zdarzenia z kolejki w pamięci przepadają | rzadkie; analiza nie zastępuje danych biznesowych z serwisów |
| wyjątek w konsumencie (np. błąd mapowania) | retry 100/500/1000/5000 ms, potem kolejka `analytics-..._error` | błąd w kodzie forwardera jest widoczny jak każdy inny błąd konsumenta |

Inbox chroniłby przed duplikatami kosztem bazy i transakcji na każdą wiadomość, a duplikat w analityce jest nieszkodliwy.
Konsekwencja dla Ciebie: **żadna logika biznesowa nie może zależeć od tego, czy zdarzenie dotarło do PostHog**, a liczby
z PostHog to statystyka, nie księgowość.

### Wdrożenie

Chart `deploy/helm/superapp-analytics-forwarder`: Deployment `analytics-forwarder` (bez API poza `/health/*`), PodDisruptionBudget
(`maxUnavailable: 1`) i `ScaledObject` KEDA skalujący po długości **każdej** z czterech kolejek (`keda.queues` w `values.yaml`,
`queueLength: "200"`, 1–3 repliki). Nowy konsument = nowa kolejka = nowa pozycja w `keda.queues`
([przepis 10](przepisy/10-zdarzenie-analityczne-i-feature-flag.md)). Sekret `analytics-forwarder-secrets` (External Secrets)
zawiera `ConnectionStrings__RabbitMq`, `Analytics__ProjectToken`, `Analytics__IdKey`.

---

## 21.6 Feature flags

### Deklaracja: `FeatureFlag`

```csharp
// src/Framework/SuperApp.Framework.Application/FeatureFlags/FeatureFlag.cs
public sealed record FeatureFlag(string Key, bool DefaultValue);
```

Flagę deklarujesz **raz**, jako pole `static readonly` w klasie `{Serwis}FeatureFlags` w projekcie Application, obok
`{Serwis}Scopes`. Nigdy nie przekazujesz literałów kluczy po kodzie. Wzorzec (takiej klasy jeszcze w repozytorium nie ma; żaden
handler nie używa dziś `IFeatureFlags`):

```csharp
// Knowledge.Application/KnowledgeFeatureFlags.cs (wzorzec)
namespace Knowledge.Application;

/// <summary>
/// Feature flags of the Knowledge service (ADR-0036): switches for features being rolled out gradually and kill switches of existing
/// features, evaluated through <see cref="SuperApp.Framework.Application.FeatureFlags.IFeatureFlags"/>.
/// </summary>
/// <remarks>
/// Keys are PostHog flag keys with the <c>knowledge_</c> prefix. Each default value is the behaviour users get when PostHog cannot answer,
/// so it must be safe to run with indefinitely. A flag is not a permission: scopes still apply (ADR-0012).
/// </remarks>
public static class KnowledgeFeatureFlags
{
    /// <summary>
    /// <c>knowledge_material_ratings</c>: readers can rate published materials. Rolled out gradually; off by default, so the feature stays
    /// hidden when PostHog is unreachable.
    /// </summary>
    public static readonly FeatureFlag MaterialRatings = new("knowledge_material_ratings", DefaultValue: false);
}
```

| Reguła | Dlaczego |
|---|---|
| klucz: małe litery, cyfry, `_`, prefiks serwisu (`knowledge_material_ratings`) | wszystkie serwisy dzielą jeden projekt PostHog środowiska; prefiks wyklucza kolizje; ten sam klucz jest kluczem konfiguracji `FeatureFlags:{klucz}` |
| `DefaultValue` = zachowanie **bezpieczne do trwałego działania** | to wartość przy awarii PostHog, timeoucie, nieznanej fladze i w każdym środowisku bez analityki |
| nowa funkcja w rolloucie: `false` | przy awarii PostHog funkcja znika, zamiast pojawić się wszystkim naraz |
| wyłącznik awaryjny istniejącej funkcji: `true` | przy awarii PostHog funkcja działa dalej |
| jedna flaga = jedna decyzja | flaga „wszystko nowe w module” jest niemożliwa do bezpiecznego wyłączenia po kawałku |

### Odczyt: `IFeatureFlags`

```csharp
// src/Framework/SuperApp.Framework.Application/FeatureFlags/IFeatureFlags.cs
public interface IFeatureFlags
{
    ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken);
}
```

Używasz go w handlerach komend i zapytań (konstruktor główny), żeby przełączyć zachowanie. Wzorzec:

```csharp
// Knowledge.Application/Features/Library/RateMaterial/RateMaterialHandler.cs (wzorzec)
internal sealed class RateMaterialHandler(
    IMaterialRatingRepository ratings,
    IMaterialRepository materials,
    IFeatureFlags flags,
    ICurrentUser currentUser,
    IClock clock) : ICommandHandler<RateMaterial>
{
    public async Task<Result> Handle(RateMaterial command, CancellationToken cancellationToken)
    {
        if (!await flags.IsEnabledAsync(KnowledgeFeatureFlags.MaterialRatings, cancellationToken))
        {
            return LibraryErrors.RatingsNotAvailable;
        }

        // ... the rest of the use case: user, material, aggregate method
    }
}
```

### Źle / dobrze

```csharp
// ŹLE: literał klucza, wartość domyślna rozsiana po kodzie, flaga w agregacie
if (await flags.IsEnabledAsync(new FeatureFlag("material_ratings", true), cancellationToken)) { ... }

// DOBRZE: jedna deklaracja z prefiksem serwisu i przemyślaną wartością domyślną
if (await flags.IsEnabledAsync(KnowledgeFeatureFlags.MaterialRatings, cancellationToken)) { ... }
```

Dlaczego: literał bez prefiksu koliduje z flagami innych serwisów, a każde miejsce z własnym `DefaultValue` może przy awarii
PostHog zachować się inaczej.

```csharp
// ŹLE: flaga zastępuje uprawnienie
if (await flags.IsEnabledAsync(KnowledgeFeatureFlags.MaterialRatings, cancellationToken))
{
    // brak [RequiresScope] na komendzie, "bo flaga i tak jest włączona tylko dla redaktorów"
}

// DOBRZE: scope decyduje o dostępie, flaga o tym, czy funkcja w ogóle istnieje
[RequiresScope(KnowledgeScopes.LibraryWrite)]
public sealed record RateMaterial(Guid MaterialId, int Stars) : ICommand;
```

Dlaczego: flagę zmienia się w panelu PostHog jednym kliknięciem, bez review, a targetowanie procentowe nie zna uprawnień.
O dostępie decydują scope i reguły zasobu (ADR-0012, [09.6](09-bezpieczenstwo.md#96-autoryzacja-trzy-poziomy)).

```csharp
// ŹLE: agregat sam pyta o flagę (Domain zależałby od Application) albo handler przenosi regułę biznesową do siebie
// DOBRZE: handler odczytuje flagę i przekazuje decyzję do metody agregatu jako argument, jeśli agregat jej potrzebuje
// (przykład: takiej flagi i takiej metody w repozytorium nie ma)
var detailedCompletion = await flags.IsEnabledAsync(KnowledgeFeatureFlags.DetailedCompletion, cancellationToken);
var result = material.Complete(userId, detailedCompletion, clock.UtcNow);
```

### Implementacja na PostHog i HybridCache: `HybridCacheFeatureFlags`

Zgodnie z architekturą (ADR-0036) serwisy domenowe **nie wykonują bezpośrednich zapytań do PostHog Cloud EU**, co chroni izolację sieciową w klastrze (brak konieczności otwierania ruchu wychodzącego `egress` do Internetu w NetworkPolicy).
Zamiast tego ewaluacja flag opiera się na bibliotece **`HybridCache`** (L1 w pamięci RAM + L2 w Redis, ADR-0020) oraz na dedykowanym serwisie wewnętrznym **`SuperApp.AnalyticsForwarder`**, który pełni rolę jedynej bramy wychodzącej do PostHog:

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Analytics/HybridCacheFeatureFlags.cs
internal sealed partial class HybridCacheFeatureFlags(
    HybridCache cache,
    IHttpClientFactory httpClientFactory,
    ICurrentUser currentUser,
    AnalyticsIdentity identity,
    IOptions<AnalyticsOptions> options,
    ILogger<HybridCacheFeatureFlags> logger) : IFeatureFlags
{
    private const string ForwarderClientName = "AnalyticsForwarder";

    public async ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken = default)
    {
        var distinctId = identity.ForSubject(currentUser.Subject) ?? AnalyticsIdentity.System;
        var cacheKey = $"superapp:featureflags:{distinctId}";

        IReadOnlyDictionary<string, bool>? evaluations = null;

        try
        {
            evaluations = await cache.GetOrCreateAsync(
                cacheKey,
                async cancel => await FetchFlagsAsync(distinctId, cancel),
                new HybridCacheEntryOptions
                {
                    LocalCacheExpiration = TimeSpan.FromSeconds(30),
                    Expiration = TimeSpan.FromMinutes(5),
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            InfrastructureTelemetry.FeatureFlagFallbacks.Add(1, new KeyValuePair<string, object?>("reason", "cache_error"));
            LogEvaluationFailed(logger, exception);
        }

        if (evaluations is null || !evaluations.TryGetValue(flag.Key, out var isEnabled))
        {
            Fallback(flag, "unknown flag or fallback", exception: null);
            return flag.DefaultValue;
        }

        return isEnabled;
    }
...
```

Krok po kroku, co się dzieje w żądaniu:

```mermaid
flowchart TD
    A["handler: IsEnabledAsync(flag)"] --> B["distinct id = pseudonim ICurrentUser.Subject<br/>albo 'system'"]
    B --> C["HybridCache.GetOrCreateAsync<br/>(klucz superapp:featureflags:{id})"]
    C -- "trafienie L1 (RAM) lub L2 (Redis)" --> D["zwrócenie słownika flag z cache"]
    C -- "cache miss (brak lub wygaśnięcie)" --> E["GET /internal/flags?distinctId={id}<br/>do analytics-forwarder"]
    E -- "sukces HTTP" --> F["zapis do L1 (30 s) i L2 (5 min)"]
    F --> D
    E -- "błąd / timeout / niedostępność forwardera" --> G["log 401, superapp.feature_flags.fallbacks<br/>wynik = pusty słownik"]
    G --> H["DefaultValue z kodu"]
    D --> I{"flaga znana w słowniku?"}
    I -- "tak" --> J["wartość flagi (true / false)"]
    I -- "nie" --> K["DefaultValue<br/>log 400, reason = unknown flag"]
```

| Cecha | Zachowanie | Konsekwencja dla Ciebie |
|---|---|---|
| brak egressu z serwisów | serwisy domenowe nie łączą się z PostHog Cloud EU; ruch wychodzi wyłącznie przez wewnętrzny serwis `analytics-forwarder` | pełna izolacja NetworkPolicy mikroserwisów; brak problemów z ruchem do Internetu |
| dwupoziomowy cache | L1 (pamięć procesu, 30 s) + L2 (Redis, 5 min) w `HybridCache` | odczyt flag w mikroserwisie jest błyskawiczny (0 ms narzutu w L1), a zapytanie HTTP do forwardera następuje tylko przy wygaśnięciu cache |
| odświeżanie w tle | `AnalyticsForwarder` cyklicznie (co 30 s) odświeża bazowy snapshot flag w tle z PostHog (`FeatureFlagsRefreshWorker`, EventId 9005/9006) | forwarder zawsze ma gotowy snapshot w pamięci RAM |
| dla kogo | pseudonim `ICurrentUser.Subject`; bez użytkownika (`SystemCurrentUser` w Workerze, endpoint anonimowy) `system` | rollout procentowy jest „przyklejony” do osoby i zgodny z tym, co widzi SPA |
| timeout | `Analytics:FeatureFlagsTimeout`, domyślnie 1 s | ograniczenie czasu oczekiwania na wewnętrzny forwarder |
| fallback | błąd cache, niedostępność forwardera lub nieznany klucz: wartość domyślna z kodu (`FeatureFlag.DefaultValue`, log 400/401) | flaga nigdy nie psuje żądania; `DefaultValue` musi być bezpieczne do trwałego działania |

### Bez analityki: `ConfigurationFeatureFlags`

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Analytics/ConfigurationFeatureFlags.cs
internal sealed class ConfigurationFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    public const string SectionName = "FeatureFlags";

    public ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(configuration.GetValue<bool?>($"{SectionName}:{flag.Key}") ?? flag.DefaultValue);
    }
}
```

Rejestrowana, gdy `Analytics:ProjectToken` jest pusty: lokalnie i w testach. Wartość jest **taka sama dla wszystkich
użytkowników** (procenty i targetowanie istnieją tylko w PostHog). Konfiguracja jest czytana przy każdym wywołaniu, więc zmiana
`appsettings.Development.json` działa bez restartu. Włączenie lokalne:

```json
// appsettings.Development.json projektu Api lub Worker
{
  "FeatureFlags": {
    "knowledge_material_ratings": true
  }
}
```

albo zmienna środowiskowa (profil w `launchSettings.json`, terminal): `FeatureFlags__knowledge_material_ratings=true`. Wartość
musi być `true` albo `false`; inny tekst kończy się wyjątkiem konwersji przy odczycie.

### Flaga, scope czy konfiguracja?

| Potrzebujesz | Użyj | Nie używaj |
|---|---|---|
| udostępnić funkcję stopniowo (5 % → 50 % → 100 %), eksperyment, szybkie wyłączenie bez wdrożenia | feature flag | konfiguracji (wymaga wdrożenia) |
| zdecydować, **kto może** wykonać operację | scope (`[RequiresScope]`) i reguły zasobu | flagi |
| ustawienie techniczne środowiska (timeout, adres, limit) | konfiguracja (`appsettings`, zmienne, Vault) | flagi |
| ukryć element interfejsu, którego serwer i tak nie obsługuje inaczej | flaga w kliencie (SDK) | — |
| zmienić zachowanie serwera | flaga sprawdzana **w backendzie** (flaga w UI służy tylko wyglądowi) | samej flagi w UI |

---

## 21.7 Konfiguracja i sekrety

### Sekcja `Analytics`

| Klucz | Domyślnie | Sekret? | Znaczenie |
|---|---|---|---|
| `ProjectToken` | brak | nie (klienci go osadzają), ale ustawiany per środowisko | `phc_…`; **włącza** analitykę (`Enabled`) |
| `IdKey` | brak | **tak** (Vault) | klucz HMAC pseudonimu; wymagany przy włączonej analityce, minimum 32 znaki, ten sam we wszystkich procesach środowiska |
| `FeatureFlagsKey` | brak | **tak** (Vault), opcjonalny | `phs_…`; lokalna ewaluacja flag w procesie |
| `Host` | `https://eu.i.posthog.com` | nie | host zdarzeń i flag; tylko `https://*.posthog.com` |
| `AssetsHost` | `https://eu-assets.i.posthog.com` | nie | host zasobów statycznych (proxy `/ingest/static`); tylko `https://*.posthog.com` |
| `FeatureFlagsTimeout` | `00:00:01` | nie | maksymalny czas ewaluacji flag; po nim wartości domyślne |

Osobno sekcja `FeatureFlags:{klucz}`: wartości flag, gdy analityka jest wyłączona.

### Walidacja przy starcie

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Analytics/AnalyticsOptions.cs (fragment)
public bool Enabled => !string.IsNullOrWhiteSpace(ProjectToken);

internal bool IsValid() =>
    !Enabled
    || (IdKey is { Length: >= MinimumIdKeyLength }
        && IsPostHogHost(Host)
        && IsPostHogHost(AssetsHost)
        && FeatureFlagsTimeout > TimeSpan.Zero);

// The hosts are reached from inside the cluster and by the BFF proxy; only PostHog's own HTTPS hosts are accepted, so a configuration
// mistake cannot turn the proxy into a gateway to an arbitrary site.
private static bool IsPostHogHost(Uri host) =>
    host is { IsAbsoluteUri: true, Scheme: "https", AbsolutePath: "/", Query: "", UserInfo: "" }
    && host.Host.EndsWith(".posthog.com", StringComparison.OrdinalIgnoreCase);
```

Gdy analityka jest włączona, a `IdKey` brakuje, jest krótszy niż 32 znaki, host nie jest `https://*.posthog.com` (albo ma
ścieżkę, zapytanie, dane logowania) lub timeout nie jest dodatni, **proces nie startuje** (`ValidateOnStart`) z komunikatem:

```text
Microsoft.Extensions.Options.OptionsValidationException: Analytics: with ProjectToken set, IdKey (at least 32 characters) is required
and Host/AssetsHost must be https://*.posthog.com.
```

Ograniczenie hostów to zabezpieczenie: proxy `/ingest` przekazuje ruch na `Host`, więc pomyłka w konfiguracji nie może zrobić
z bramy otwartego proxy do dowolnej strony. Bez `ProjectToken` analityka jest wyłączona i nic nie jest wysyłane; reszta sekcji
nie jest wtedy sprawdzana.

### Rejestracja: kto co woła

```csharp
// src/Framework/SuperApp.Framework.Infrastructure/Analytics/AnalyticsServiceCollectionExtensions.cs
public static IServiceCollection AddAppAnalytics(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<AnalyticsOptions>()
        .Bind(configuration.GetSection(AnalyticsOptions.SectionName))
        .Validate(
            options => options.IsValid(),
            "Analytics: with ProjectToken set, IdKey (at least 32 characters) is required and Host/AssetsHost must be https://*.posthog.com.")
        .ValidateOnStart();
    services.TryAddSingleton<AnalyticsIdentity>();

    var settings = Settings(configuration);
    if (!settings.Enabled || BuildTimeDocumentGeneration.IsActive || services.Any(descriptor => descriptor.ServiceType == typeof(IPostHogClient)))
    {
        return services;
    }

    services.AddPostHog();
    services.PostConfigure<PostHogOptions>(posthog =>
    {
        posthog.ProjectToken = settings.ProjectToken;
        posthog.HostUrl = settings.Host;
        posthog.SecretKey = settings.FeatureFlagsKey;
    });
    return services;
}

public static IServiceCollection AddAppFeatureFlags(this IServiceCollection services, IConfiguration configuration)
{
    services.AddAppAnalytics(configuration);

    if (Settings(configuration).Enabled && !BuildTimeDocumentGeneration.IsActive)
    {
        services.AddHybridCache();
        services.AddHttpClient("AnalyticsForwarder", (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AnalyticsOptions>>().Value;
            client.BaseAddress = options.ForwarderUrl;
            client.Timeout = options.FeatureFlagsTimeout;
        });
        services.TryAddScoped<IFeatureFlags, HybridCacheFeatureFlags>();
    }
    else
    {
        services.TryAddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
    }

    return services;
}
```

| Proces | Wywołanie | Uwagi |
|---|---|---|
| `bff-web`, `gateway-mobile` (`SuperApp.Gateway/Program.cs`) | `AddAppAnalytics` | pseudonim w `/bff/user` i `/analytics/id`, proxy `/ingest`; brama nie ma `IFeatureFlags` |
| API i Worker każdego serwisu (`Add{Serwis}Core` w `{Serwis}.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, także szablon `superapp-service`) | `AddAppFeatureFlags` | wymaga `ICurrentUser`: `AddAppApi` w API, `AddAppWorker` w Workerze, fake w testach |
| `SuperApp.AnalyticsForwarder` | `AddAppAnalytics` + `AddProductEventSink` | forwarder nie czyta flag |

Nowy serwis z szablonu (`dotnet new superapp-service`) ma `AddAppFeatureFlags` od razu; niczego nie dopisujesz.

### Sekrety na klastrze

Wszystkie wartości `Analytics__*` dostarcza dział infrastruktury przez External Secrets (Vault) w sekretach procesów
(ADR-0016); w `appsettings*.json` nie ma ich nigdy.

| Chart | Sekret | Klucze analityki |
|---|---|---|
| `superapp-service` (każdy serwis, API i Worker) | `secretName` z `values.yaml` | `Analytics__ProjectToken`, `Analytics__IdKey`, opcjonalnie `Analytics__FeatureFlagsKey` |
| `superapp-gateway` (`bff-web`, `gateway-mobile`) | jw. | `Analytics__ProjectToken`, `Analytics__IdKey` |
| `superapp-analytics-forwarder` | `analytics-forwarder-secrets` | `Analytics__ProjectToken`, `Analytics__IdKey` (+ `ConnectionStrings__RabbitMq`) |

Wymagania poza repozytorium: osobny projekt PostHog na środowisko (dev, test, prod), DPA, wyjście z klastra do
`eu.i.posthog.com` i `eu-assets.i.posthog.com` (NetworkPolicy egress lub proxy) dla bram, serwisów i forwardera.

### Lokalnie

Domyślnie **wyłączona**: compose przekazuje `Analytics__ProjectToken: ${ANALYTICS_PROJECT_TOKEN:-}` i
`Analytics__IdKey: ${ANALYTICS_ID_KEY:-}` (kotwica `analytics-env` w `deploy/local/docker-compose.yml`) do serwisów, bram
i forwardera, a bez `deploy/local/.env` obie wartości są puste. Włączenie: 21.12.

---

## 21.8 Klient web (Angular): wzorzec

> Kodu Angulara nie ma jeszcze w repozytorium. Poniższy kod to wzorzec zgodny z ADR-0036 do zastosowania przy budowie SPA;
> nazwy opcji sprawdź w dokumentacji aktualnej wersji `posthog-js`, dodając pakiet.

Kolejność, której trzeba się trzymać:

```mermaid
sequenceDiagram
    participant U as Użytkownik
    participant A as Angular
    participant B as bff-web
    participant P as PostHog (przez /ingest)

    A->>A: start: brak zgody → PostHog nie istnieje (nic w localStorage, brak ciasteczek)
    U->>A: zgoda na analitykę
    A->>P: posthog.init(token, { api_host: '/ingest', ... })
    A->>B: GET /bff/user
    B-->>A: { ..., analyticsId: "u_…" }
    A->>P: posthog.identify("u_…")
    U->>A: wylogowanie
    A->>P: posthog.reset()
    A->>B: nawigacja GET /bff/logout?sid=…
    U->>A: wycofanie zgody
    A->>P: posthog.opt_out_capturing(), posthog.reset()
```

```typescript
// web/src/app/analytics/analytics.service.ts (wzorzec)
import { Injectable } from '@angular/core';
import posthog from 'posthog-js';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private initialized = false;

  /** Called only after the user has given consent; before that the SDK must not exist. */
  start(): void {
    // Empty token = analytics disabled in this environment (the BFF does not map /ingest then).
    if (this.initialized || !environment.posthogProjectToken) {
      return;
    }

    posthog.init(environment.posthogProjectToken, {
      api_host: '/ingest',                 // same-origin proxy in bff-web, never eu.i.posthog.com directly
      ui_host: 'https://eu.posthog.com',
      person_profiles: 'identified_only',
      capture_pageview: 'history_change',
      session_recording: {
        maskAllInputs: true,               // every form field masked
        maskTextSelector: '*',             // every text masked; unmask only elements without personal data
      },
    });
    this.initialized = true;
  }

  /** After login: the pseudonymous id from /bff/user, never the CIAM sub. */
  identify(analyticsId: string | null): void {
    if (this.initialized && analyticsId) {
      posthog.identify(analyticsId);
    }
  }

  /** On logout: forget the user on this device. */
  reset(): void {
    if (this.initialized) {
      posthog.reset();
    }
  }

  /** On consent withdrawal: stop capturing and remove the SDK's local state. */
  withdrawConsent(): void {
    if (this.initialized) {
      posthog.opt_out_capturing();
      posthog.reset();
    }
  }
}
```

Zasady dla SPA:

- **Token projektu** jest jawny i pochodzi z konfiguracji środowiska SPA (np. `environment.posthogProjectToken`). Musi być
  zgodny z ustawieniem bram: SPA z tokenem przy wyłączonej analityce w `bff-web` dostanie `401` na `/ingest`.
- **`identify` tylko z `analyticsId`** z `/bff/user`; przy `null` (analityka wyłączona) nie wołaj `identify`.
- **Interceptor CSRF** dodaje `X-CSRF: 1` tylko do `/api/*`; żądań posthog-js nie dotyka (i nie musi).
- **Ekrany dziennika snu** wyłączasz z nagrywania w całości, np. na zmianę trasy:

  ```typescript
  // web/src/app/analytics/replay-guard.ts (wzorzec)
  router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe((event) => {
    const healthScreen = (event as NavigationEnd).urlAfterRedirects.startsWith('/sleep-diary');
    if (healthScreen) {
      posthog.stopSessionRecording();
    } else {
      posthog.startSessionRecording();
    }
  });
  ```

  Pojedyncze elementy z danymi osobowymi poza tymi ekranami oznaczasz klasą `ph-no-capture` (posthog-js ich nie nagrywa).
- **Flagi w SPA** (`posthog.isFeatureEnabled('knowledge_material_ratings')`) służą wyłącznie wyglądowi. Jeśli serwer zachowuje
  się inaczej przy włączonej fladze, sprawdza ją sam przez `IFeatureFlags` (dla tego samego pseudonimu, więc wynik jest
  zgodny).
- Zdarzenia UI: `posthog.capture('material_opened', { material_id })` z tą samą listą dozwolonych właściwości co backend.

---

## 21.9 Klienci mobilni: wzorzec

> Aplikacji mobilnych nie ma jeszcze w repozytorium. Kod poniżej to wzorzec; nazwy API sprawdź w dokumentacji aktualnych
> wersji `posthog-android` i `posthog-ios`.

Aplikacje mobilne używają oficjalnych SDK i wysyłają dane **bezpośrednio** do `https://eu.i.posthog.com` (bez proxy: blokery
treści przeglądarek ich nie dotyczą). Pseudonim pobierają z bramy mobilnej po zalogowaniu:

```kotlin
// mobile/android/.../analytics/AnalyticsIdApi.kt (wzorzec, Ktor)
@Serializable
data class AnalyticsIdResponse(val analyticsId: String?)

suspend fun fetchAnalyticsId(client: HttpClient, accessToken: String): String? =
    client.get("$gatewayMobileUrl/analytics/id") {
        bearerAuth(accessToken)
    }.body<AnalyticsIdResponse>().analyticsId
```

```kotlin
// mobile/android/.../analytics/Analytics.kt (wzorzec)
fun startAfterConsent(context: Context) {
    val config = PostHogAndroidConfig(apiKey = BuildConfig.POSTHOG_PROJECT_TOKEN, host = "https://eu.i.posthog.com").apply {
        sessionReplay = true
        sessionReplayConfig.maskAllTextInputs = true
        sessionReplayConfig.maskAllImages = true
    }
    PostHogAndroid.setup(context, config)
}

fun onLoggedIn(analyticsId: String?) { analyticsId?.let { PostHog.identify(it) } }   // null = analytics disabled
fun onLoggedOut() = PostHog.reset()
fun onConsentWithdrawn() { PostHog.optOut(); PostHog.reset() }
```

```swift
// mobile/ios/.../Analytics/Analytics.swift (wzorzec)
func startAfterConsent() {
    let config = PostHogConfig(apiKey: Secrets.posthogProjectToken, host: "https://eu.i.posthog.com")
    config.sessionReplay = true
    config.sessionReplayConfig.maskAllTextInputs = true
    config.sessionReplayConfig.maskAllImages = true
    PostHogSDK.shared.setup(config)
}

func fetchAnalyticsId(accessToken: String) async throws -> String? {
    var request = URLRequest(url: gatewayMobileURL.appending(path: "analytics/id"))
    request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
    let (data, _) = try await URLSession.shared.data(for: request)
    return try JSONDecoder().decode(AnalyticsIdResponse.self, from: data).analyticsId
}

func onLoggedIn(analyticsId: String?) { if let id = analyticsId { PostHogSDK.shared.identify(id) } }
func onLoggedOut() { PostHogSDK.shared.reset() }
```

Te same reguły co w web: inicjalizacja dopiero po zgodzie, `identify` tylko z `analyticsId` (nigdy z `sub` z tokenu), ekrany
dziennika snu wyłączone z nagrań (Android: `tag = "ph-no-capture"` na widoku, iOS: `accessibilityIdentifier = "ph-no-capture"`,
albo nagrywanie wyłączone dla całego ekranu), flagi w aplikacji tylko do wyglądu.

---

## 21.10 Testy

### Co jest testowane

`src/Analytics/SuperApp.AnalyticsForwarder.Tests` (12 testów, bez Dockera):

| Klasa | Co sprawdza |
|---|---|
| `AnalyticsIdentityTests` | format `^u_[0-9a-f]{32}$`, stabilność dla tego samego `sub`, brak `sub` w wyniku; różne `sub` i różne klucze = różne pseudonimy; `null` bez `sub` i przy wyłączonej analityce |
| `AnalyticsSettingsTests` | włączona analityka wymaga długiego `IdKey` i hosta `https://*.posthog.com` (4 przypadki teorii); bez analityki flagi z konfiguracji z wartością domyślną jako fallbackiem; `AddAppAnalytics` buduje się z `ValidateOnBuild` bez `ICurrentUser` (brama i forwarder) |
| `ConsumerMappingTests` | prawdziwe konsumenty na test harness MassTransit: nazwy zdarzeń, właściciel (`system` / użytkownik), czas faktu, `message_id`, **dokładna** lista właściwości; `sleepdiary_entry_recorded` bez danych o zdrowiu |

Testy architektury: reguła 10 (`Analytics_forwarder_depends_only_on_contracts_of_services`) i reguła 11
(`Only_framework_and_forwarder_use_posthog`) w `tests/SuperApp.ArchitectureTests/ArchitectureRules.cs`.

```bash
dotnet test --project src/Analytics/SuperApp.AnalyticsForwarder.Tests
```

### Test nowego konsumenta forwardera

Wzorzec z `ConsumerMappingTests`: prawdziwy konsument, `RecordingProductEventSink` zamiast PostHog, test harness MassTransit
w pamięci.

```csharp
// src/Analytics/SuperApp.AnalyticsForwarder.Tests/ConsumerMappingTests.cs (fragmenty)
[Fact]
public async Task Sleep_entry_recorded_belongs_to_the_user_and_carries_no_health_data()
{
    var captured = await ConsumeAsync(new SleepEntryRecordedV1(Guid.NewGuid(), "user-sub-1", new DateOnly(2026, 9, 30), 465, 4, At));

    Assert.Equal(ProductEventNames.SleepDiaryEntryRecorded, captured.Name);
    Assert.Equal("user-sub-1", captured.Subject);
    Assert.Empty(captured.Properties);
}

private static async Task<ProductEvent> ConsumeAsync<TMessage>(TMessage message)
    where TMessage : class
{
    var sink = new RecordingProductEventSink();
    await using var provider = new ServiceCollection()
        .AddSingleton<IProductEventSink>(sink)
        .AddMassTransitTestHarness(bus => bus.AddConsumers(typeof(MaterialPublishedConsumer).Assembly))
        .BuildServiceProvider(validateScopes: true);

    var harness = provider.GetRequiredService<ITestHarness>();
    await harness.Start();
    await harness.Bus.Publish(message, TestContext.Current.CancellationToken);

    Assert.True(await harness.Consumed.Any<TMessage>(TestContext.Current.CancellationToken));
    return Assert.Single(sink.Captured);
}
```

Najważniejsza asercja to **dokładna lista kluczy** właściwości (`Assert.Equal([...], captured.Properties.Keys.Order())` albo
`Assert.Empty`): dodanie pola do kontraktu nie może „przeciec” do analityki bez zmiany testu, a więc bez przeglądu.

### Test handlera z flagą

Handler komendy testujesz z fake'iem portu, jak inne porty (`FakeCurrentUser`, `FakeClock`). Fake (wzorzec, w
`{Serwis}.Application.Tests/Fakes/`):

```csharp
// Knowledge.Application.Tests/Fakes/FakeFeatureFlags.cs (wzorzec)
internal sealed class FakeFeatureFlags(params FeatureFlag[] enabled) : IFeatureFlags
{
    public ValueTask<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken) =>
        ValueTask.FromResult(enabled.Contains(flag));
}
```

Testuj **oba** stany flagi: włączona (nowe zachowanie) i wyłączona (stare zachowanie albo błąd „niedostępne”). Testy
integracyjne (`ServiceFixture`) nie ustawiają `Analytics:ProjectToken`, więc działa `ConfigurationFeatureFlags`: flagę
włączasz wpisem `FeatureFlags:{klucz}` w konfiguracji fixture'a albo podmieniasz `IFeatureFlags` w kontenerze testu.
Pełny przykład: [przepis 10, część B](przepisy/10-zdarzenie-analityczne-i-feature-flag.md#b-nowa-flaga-w-serwisie).

### Testy jednostkowe i integracyjne

Testy automatyczne `HybridCacheFeatureFlags` (cache hit/miss, timeout, błąd serwisu, nieznana flaga) oraz `FeatureFlagsCache`
znajdują się w `SuperApp.AnalyticsForwarder.Tests`.

---

## 21.11 Obserwowalność

| Sygnał | Gdzie | Interpretacja | Reakcja |
|---|---|---|---|
| log `EventId` 400, Warning, „Feature flag {FlagKey}: {Reason}, using the default value {DefaultValue}” | serwisy (`HybridCacheFeatureFlags`) | kod pyta o flagę, której PostHog / forwarder nie zna (`unknown flag`) | utwórz flagę w projekcie PostHog środowiska albo usuń martwy kod flagi |
| log `EventId` 401, Warning, „Feature flags could not be evaluated; every flag of this request uses its default value” | serwisy (`HybridCacheFeatureFlags`) | błąd pobrania z forwardera lub błąd pamięci podręcznej; fallback do wartości domyślnych | sprawdź pod `analytics-forwarder` (logi 9005/9006, health) oraz łączność w klastrze |
| metryka `superapp.feature_flags.fallbacks{reason}` (`timeout`, `unavailable`, `unknown flag`, `cache_error`), meter `SuperApp.Infrastructure` | Prometheus | ile ewaluacji skończyło się wartością domyślną | **alert** przy stałym wzroście `timeout`/`unavailable`/`cache_error`; `unknown flag` po wdrożeniu = brak flagi w PostHog |
| log `EventId` 9001, Information, „Analytics disabled: product event {EventName} ({EventOwner}, properties: {PropertyNames}) not sent” | forwarder | analityka wyłączona; tak sprawdzasz mapowanie lokalnie | na środowisku z analityką 9001 oznacza brak `Analytics__ProjectToken` w sekrecie forwardera |
| log `EventId` 9002, Warning, „Product event {EventName} was not queued for PostHog (queue full or client disposed)” | forwarder | klient PostHog nie przyjął zdarzenia (kolejka pełna, proces się zamyka) | sprawdź łączność z PostHog i obciążenie; pojedyncze wpisy przy zamykaniu poda są oczekiwane |
| log `EventId` 9005, Information, „Feature flags refreshed from PostHog ({Count} flags)” | forwarder | cykliczny worker odświeżył snapshot flag w pamięci | normalna praca cykliczna co 30 s |
| log `EventId` 9006, Warning, „Failed to refresh feature flags from PostHog; continuing with cached snapshot” | forwarder | błąd odświeżenia flag z PostHog (sieć/timeout); forwarder kontynuuje z poprzednim stanem | sprawdź egress forwardera do `eu.i.posthog.com` i dostępność SaaS |
| kolejki `analytics-*` i `analytics-*_error` | RabbitMQ, KEDA | rosnąca kolejka = forwarder nie nadąża albo nie działa; `_error` = wyjątek w konsumencie | jak dla każdego konsumenta ([17, rozdział 4](17-rozwiazywanie-problemow.md#4-messaging)) |

Metryka z tagiem `reason` jest liczona dla nieudanego pobrania lub błędu cache, a dla `unknown flag` raz na każde wywołanie
z nieznanym kluczem. Zakresy `EventId`: 400–499 framework (feature flags), 9000–9999 forwarder ([rejestr](../logowanie-eventid.md)).

---

## 21.12 Lokalnie krok po kroku

### Domyślnie: analityka wyłączona

```bash
docker compose -f deploy/local/docker-compose.yml --profile app up -d --build
```

- Kontener `analytics-forwarder` (profil `app`, bez portów hosta) konsumuje kolejki `analytics-*` i loguje każde zdarzenie
  (9001). Z IDE uruchamiasz go profilem `analytics-forwarder` (`http://localhost:5180`, RabbitMQ z
  `appsettings.Development.json`); zatrzymaj wtedy kontener.
- Serwisy czytają flagi z konfiguracji (`ConfigurationFeatureFlags`); `/bff/user` i `/analytics/id` zwracają
  `"analyticsId": null`; `/ingest` nie istnieje (`401`).

Sprawdzenie mapowania: opublikuj materiał (token `dev-cli` użytkownika `editor`, jak w [01 Start](01-start.md)) albo zapisz
wpis snu, a potem:

```bash
docker compose -f deploy/local/docker-compose.yml logs analytics-forwarder | grep "\[9001\]" -A1
# info: SuperApp.AnalyticsForwarder.Events.LoggingProductEventSink[9001]
#       Analytics disabled: product event knowledge_material_published (system, properties: material_id,material_type) not sent
# info: SuperApp.AnalyticsForwarder.Events.LoggingProductEventSink[9001]
#       Analytics disabled: product event sleepdiary_entry_recorded (user, properties: ) not sent
```

### Flaga lokalnie

Najprościej w trybie hybrydowym: zatrzymaj kontener API, uruchom API z IDE z `FeatureFlags__knowledge_material_ratings=true`
w zmiennych profilu `launchSettings.json` (lokalnie, bez commitowania) albo z wpisem w `appsettings.Development.json`. Zmienne
`FeatureFlags__*` nie są przekazywane do kontenerów w `docker-compose.yml`.

### Włączenie analityki do własnego projektu PostHog

Tylko do **deweloperskiego** projektu PostHog, nigdy produkcyjnego:

```bash
# deploy/local/.env (w .gitignore)
ANALYTICS_PROJECT_TOKEN=phc_twoj_projekt_deweloperski
ANALYTICS_ID_KEY=dowolny-losowy-tekst-co-najmniej-32-znaki
```

```bash
docker compose -f deploy/local/docker-compose.yml --profile app up -d
```

Wtedy (sprawdzone lokalnie): `/bff/user` i `/analytics/id` zwracają ten sam `u_…`, `GET https://localhost:5001/ingest/static/array.js`
daje `200` z `application/javascript` bez `Set-Cookie`, forwarder wysyła zdarzenia do PostHog (log 9001 znika), a serwisy liczą
flagi w PostHog (bez flagi w projekcie: log 400 i wartość domyślna). Procesy uruchamiane z IDE nie czytają `.env`: ustaw im
`Analytics__ProjectToken` i `Analytics__IdKey` w zmiennych profilu.

---

## Typowe błędy

| Objaw | Przyczyna | Naprawa |
|---|---|---|
| Proces nie startuje: `OptionsValidationException: Analytics: with ProjectToken set, IdKey (at least 32 characters) is required...` | ustawiony `ProjectToken` bez `IdKey`, `IdKey` krótszy niż 32 znaki, host spoza `https://*.posthog.com` albo z `http://` | uzupełnij `Analytics__IdKey` w sekrecie (Vault) albo lokalnie w `.env`; popraw host; ewentualnie usuń `ProjectToken`, jeśli analityka ma być wyłączona |
| `/ingest/...` zwraca `401 auth.invalid_token` | analityka w `bff-web` wyłączona (pusty `ProjectToken`), więc proxy nie jest mapowane | włącz analitykę w bramie albo nie inicjalizuj PostHog w SPA (pusty token w konfiguracji SPA) |
| `/ingest/...` zwraca `429` | limit `per-user` (600/min); przed zalogowaniem partycja jest po adresie IP połączenia, za Ingressem wspólna dla wielu użytkowników | sprawdź liczbę żądań posthog-js (replay generuje dużo), `Gateway:RateLimit:PermitPerMinute`; zgłoś, jeśli dotyczy anonimowego ruchu za Ingressem |
| Zdarzenia z backendu nie pojawiają się w PostHog | forwarder nie działa, brak `Analytics__ProjectToken` w jego sekrecie (log 9001), brak egress do PostHog, zdarzenie integracyjne nie wyszło z outboxa | `docker compose logs analytics-forwarder` / logi poda; kolejka `analytics-*` w RabbitMQ (`Consumers: 1`); `OutboxMessage` serwisu ([17, 4.1](17-rozwiazywanie-problemow.md#41-zdarzenie-nie-dociera-do-konsumentów-outbox-nie-jest-wysyłany)) |
| Ta sama osoba ma w PostHog dwa różne identyfikatory | różny `IdKey` w procesach środowiska (np. brama i forwarder) albo klient wywołał `identify` z czymś innym niż `analyticsId` | jeden klucz w Vault dla bram, serwisów i forwardera; `identify` tylko z `analyticsId` |
| W PostHog widać `sub` albo e-mail | klient wołał `identify(user.sub)` albo wysłał właściwość z danymi osobowymi | popraw klienta, usuń dane w PostHog zgodnie z procedurą incydentu RODO |
| Flaga zawsze ma wartość domyślną, log 400 `unknown flag` | flaga nie istnieje w projekcie PostHog **tego** środowiska albo klucz w kodzie różni się od klucza w PostHog | utwórz flagę w projekcie środowiska z identycznym kluczem |
| Flaga zawsze ma wartość domyślną, log 401, rośnie `superapp.feature_flags.fallbacks{reason="timeout"}` | PostHog nie odpowiada w `FeatureFlagsTimeout` (egress, awaria, wolne łącze) | sprawdź egress i status PostHog; ustaw `Analytics__FeatureFlagsKey` (lokalna ewaluacja) |
| Lokalnie flaga nie działa mimo wpisu | analityka lokalnie włączona (wtedy liczy PostHog, nie konfiguracja), wpis w złym procesie (API zamiast Workera) albo literówka w kluczu | `FeatureFlags__{klucz}` w procesie, który wykonuje handler; bez `ANALYTICS_PROJECT_TOKEN` |
| Flaga w konsumencie Workera nie respektuje rolloutu procentowego | w Workerze `ICurrentUser` to `SystemCurrentUser`: flaga liczona dla `system`, jedna wartość dla wszystkich | targetuj `system` warunkiem albo 0 % / 100 %; decyzje per użytkownik podejmuj w API |
| Użytkownik bez uprawnień wykonuje operację „ukrytą za flagą” | flaga użyta zamiast scope | `[RequiresScope]` na komendzie; flaga tylko włącza funkcję |
| Nowy konsument forwardera nie skaluje się przy kolejce | kolejka nie dopisana do `keda.queues` w `deploy/helm/superapp-analytics-forwarder/values.yaml` | dopisz kolejkę `analytics-{konsument}` |
| `Only_framework_and_forwarder_use_posthog` nie przechodzi | `PackageReference Include="PostHog"` w projekcie serwisu | flagi przez `IFeatureFlags`, zdarzenia przez forwarder |
| `Analytics_forwarder_depends_only_on_contracts_of_services` nie przechodzi | forwarder referencjonuje `{Serwis}.Application`/`Domain` | tylko `{Serwis}.Contracts`; brakujące dane dodaj do zdarzenia integracyjnego (zgodnie wstecz) |

## Do zapamiętania

- PostHog Cloud EU; analityka włączona tylko z `Analytics:ProjectToken`; lokalnie i w testach wyłączona.
- PostHog zna użytkownika wyłącznie jako `u_` + HMAC (`AnalyticsIdentity`); `IdKey` z Vault, ten sam w całym środowisku,
  nierotowany. Bez użytkownika: `system`.
- Zdarzenia: tylko nazwy, identyfikatory katalogu i kategorie; nigdy treść użytkownika ani dane z dziennika snu; każda nowa
  właściwość = przegląd prywatności i test z dokładną listą kluczy.
- Klienci inicjalizują PostHog dopiero po zgodzie, `identify(analyticsId)` po zalogowaniu, `reset()` przy wylogowaniu; replay
  z maskowaniem i bez ekranów zdrowia.
- `/ingest` jest w kodzie bramy, nie w trasach z bazy; usuwa sesję, tokeny, nagłówki tożsamości i IP; istnieje tylko przy
  włączonej analityce. Lokalna brama to zamiennik: na klastrze `/ingest` i `analyticsId` to wymaganie wobec wspólnej bramy albo
  nowe endpointy BFF experience, których dziś nie ma (ADR-0037).
- Backend: zdarzenie integracyjne → forwarder → `ProductEventNames`; bez inboxu, best effort, nazwy się nie zmieniają.
- Flagi: `{Serwis}FeatureFlags` + `IFeatureFlags`; jedna ewaluacja na zakres DI, timeout 1 s, wartość domyślna przy problemie
  (log 400/401, `superapp.feature_flags.fallbacks`); flaga to nie uprawnienie; lokalnie `FeatureFlags__{klucz}`.

## Powiązane

- Rozdziały: [02 Architektura w praktyce](02-architektura-w-praktyce.md), [06 Warstwa aplikacji](06-warstwa-aplikacji.md),
  [09 Bezpieczeństwo](09-bezpieczenstwo.md), [10 Zdarzenia i integracja](10-zdarzenia-i-integracja.md), [12 Testy](12-testy.md),
  [13 Lokalne środowisko i debugowanie](13-lokalne-srodowisko-i-debugowanie.md),
  [14 Logowanie i obserwowalność](14-logowanie-i-obserwowalnosc.md), [17 Rozwiązywanie problemów](17-rozwiazywanie-problemow.md).
- Przepis: [10 Zdarzenie analityczne i feature flag](przepisy/10-zdarzenie-analityczne-i-feature-flag.md).
- ADR: [0036](../adr/0036-analityka-produktowa-i-feature-flags-posthog.md) (analityka, replay, flagi),
  [0012](../adr/0012-audience-tokenow.md) (scope zamiast flag), [0016](../adr/0016-infrastruktura-i-ciam-dostarczane-zewnetrznie.md)
  (sekrety, egress), [0022](../adr/0022-konfiguracja-yarp-w-bazie.md) (trasy z bazy), [0025](../adr/0025-testy-architektury.md)
  (reguły 10 i 11), [0034](../adr/0034-lokalne-srodowisko-docker-compose.md) (lokalne środowisko),
  [0037](../adr/0037-superapp-gateway-jako-lokalny-zamiennik-wspolnej-bramy.md) (lokalna brama jako zamiennik wspólnej),
  [0038](../adr/0038-experience-modul-bff-i-serwisy-domenowe.md) (moduł w shellu, BFF experience),
  [0045](../adr/0045-modul-w-super-appce-kontrakt-z-shellem.md) (proponowany kontrakt z shellem: flagi i zgoda od shella).
- [Rejestr EventId](../logowanie-eventid.md).
