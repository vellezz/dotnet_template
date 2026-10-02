# Weryfikacja architektury repozytorium względem notatki ze spotkania

- **Data:** 2026-10-01 (wersja 4.1, po doprecyzowaniu zakresu, decyzjach P1–P2 i stanie P6/P6a)
- **Źródła:**
  - `notatka_spotkanie.md` (notatka po OCR, miejscami nieczytelna; „BFA”/„BFE” w notatce oznacza BFF);
  - stan repozytorium: zasady architektury, `docs/architektura.md` v0.3, ADR-0001…0036, kod w `src/`, `deploy/`, `templates/`.
- **Metoda:**
  - trzy niezależne analizy tylko do odczytu: topologia bram, BFF i model domen; uwierzytelnianie, autoryzacja i tokeny; aplikacja
    mobilna, kontrakty API, dostawca i narzędzia;
  - kluczowe twierdzenia sprawdzone bezpośrednio w kodzie i w realmie.
  - Fakty z kodu mają dowód w postaci ścieżki. Interpretacje notatki są oznaczone.

## 0. Zakres i założenia (doprecyzowane przez właściciela projektu)

- **Super app** to określenie całego rozwiązania organizacji: natywny shell innego zespołu plus moduły wielu zespołów.
- **My budujemy jedną experience** (jedną funkcjonalność, jeden moduł). Experience składa się z:
  1. modułu aplikacyjnego osadzonego w super appce,
  2. **BFF experience**,
  3. jednego lub kilku **serwisów domenowych**.
- **Podział na API publiczne i wewnętrzne jest na poziomie BFF:**
  - **API publiczne BFF** ma jednego konsumenta: nasz moduł. Ruch idzie przez bramę brzegową.
  - **API wewnętrzne BFF** jest dla innych experience organizacji, np. dashboardu, który pokazuje nasz widget.
  - **Serwisy domenowe** nie są wystawiane na brzegu. Ich API jest wewnętrzne i wołają je nasz BFF albo inne serwisy.
- **Kontekst wywołań API wewnętrznego** (doprecyzowanie P2): domyślnie w imieniu użytkownika; wywołania systemowe są dopuszczalne
  tylko jako wyjątek.
- **Dashboard i inne experience** są poza naszym zakresem. Dla nas liczy się tylko to, że mogą być konsumentami naszego
  wewnętrznego API.
- **Brama brzegowa jest wspólna dla całej super appki i leży poza zakresem experience** (decyzja P1). Zamiana sesji na JWT, walidacja
  JWT na brzegu, CSRF i unieważnianie po wylogowaniu należą do jej właściciela. `SuperApp.Gateway` w repo staje się **lokalnym zamiennikiem**
  tej bramy (development, E2E) i **specyfikacją wymagań** wobec niej, a nie komponentem wdrażanym przez nasz zespół.
- **API wewnętrzne naszego BFF konsumują BFF-y innych experience** (decyzja P2). Cel: inne BFF-y nie wołają **bezpośrednio** naszych
  serwisów domenowych, cały ruch do naszej domeny przechodzi przez nasz BFF. Nasze serwisy domenowe przyjmują wywołania wyłącznie od
  naszego BFF i od naszych serwisów. **Gwarancją jest konfiguracja sieci (NetworkPolicy)**, a nie mechanizm tokenów. Token exchange
  (RFC 8693) zostaje **otwartą furtką** na wypadek, gdyby wymagania bezpieczeństwa wzrosły.
- **Nie zamykamy drogi:**
  - repozytorium i szablony mają pozwalać na jedną experience albo kilka;
  - inne zespoły mogą zbudować swoje experience z tych samych szablonów, a my możemy kiedyś dodać kolejną;
  - nic w rozwiązaniu nie może zakładać, że experience jest tylko jedna.

```
super app (całe rozwiązanie)
 ├─ shell (inny zespół): tożsamość, feature flags, push, nawigacja
 ├─ brama brzegowa (wspólna, poza zakresem): sesja -> JWT, walidacja JWT, CSRF, unieważnianie
 ├─ inne experience: moduł -> brama -> ICH BFF ──(API wewnętrzne)──┐
 └─ NASZ MODUŁ ──> brama brzegowa ──> NASZ BFF experience <─────────┘
                                        ├─ API publiczne (/v1, konsument: nasz moduł)
                                        ├─ API wewnętrzne (/internal/v1, konsumenci: BFF-y innych experience)
                                        └─> NASZE serwisy domenowe (wyłącznie od naszego BFF i naszych serwisów) ──> ACL ──> dostawca
   ✗ BFF innej experience ──X──> nasze serwisy domenowe (zabronione: wymuszone przez NetworkPolicy, pakiet B)
```

---

## Stan realizacji (2026-10-01)

| Pozycja | Stan |
|---|---|
| D2 `ClockSkew` 30 s | **zrobione:** `HostingExtensions.TokenClockSkew` w `AddAppApi` i w lokalnej bramie mobilnej |
| D3 `offline_access` dla `bff-web` | **zrobione** w lokalnym realmie (zgodność z ADR-0011); dla mobile decyzja należy do shella |
| A0 | **ADR-0037** `SuperApp.Gateway` jako lokalny zamiennik wspólnej bramy |
| A1 | **ADR-0038** Experience: moduł, BFF i serwisy domenowe |
| B1 | **ADR-0039** API publiczne i wewnętrzne na poziomie BFF |
| B3 + C1 | **ADR-0040** dostęp do API wewnętrznego i serwisów domenowych, przekazywanie tokenu, token exchange jako furtka |
| B4 | **ADR-0041** NetworkPolicy jako gwarancja izolacji; etykiety `app.kubernetes.io/part-of: {experience}` i `superapp.example/experience-role` w chartach `superapp-service` i `superapp-analytics-forwarder` (procedura z działem infrastruktury otwarta) |
| Scope wywołań systemowych serwis → serwis | **ADR-0042:** `{serwis}.system.{akcja}` serwisu docelowego |
| Wymagana wartość `experience` w chartach | **zrobione:** pusta przerywa renderowanie (`superapp-service`, `superapp-analytics-forwarder`); przykłady mają `experience: example` |
| zasady architektury | zaktualizowane (§1, §3, §4, §5, §10, §13, §14) |
| A2 | **zrobione:** projekt `src/Bff/Example.Bff` (ASP.NET Core MVC bez bazy, `AddAppServiceDefaults`, `AddAppApi`, klienci Refitter `Clients/{Knowledge,SleepDiary}`, Dockerfile, testy `Example.Bff.Tests`), szablon `src/Tools/SuperApp.Cli/Templates/superapp-bff` (`dotnet new superapp-bff`) i chart `deploy/helm/superapp-bff` (wymagane `experience`, `downstream` → `Downstream__{Nazwa}__BaseAddress`); `HybridCache` dopuszczalny, ale BFF przykładowej experience go nie używa |
| A3 | **zrobione:** `AddDownstreamApi<T>` (`Http/Downstream`: adres z `Downstream:{nazwa}:BaseAddress`, standardowa odporność z ponowieniami tylko metod idempotentnych, daty ISO 8601 w ścieżce), `AddUserTokenForwarding` z portem `IDownstreamTokenProvider` jako furtką token exchange (`Http/UserContext`), `ToActionResult` przekazujący odpowiedź serwisu bez zmian, `503 downstream.unavailable` / `504 downstream.timeout` (EventId 220), `RequireScope` dla API wewnętrznego; wywołania równoległe z limitem 2 s i statusem części odpowiedzi w `SuperApp.Framework.Infrastructure/Http/Downstream/PartialResponseFetcher.cs` (`GET /v1/me/summary`) |
| A4 | **zrobione:** reguły 12–14 w `tests/SuperApp.ArchitectureTests`: BFF nie referuje serwisów (także `Contracts`) ani innych BFF, nie ma `DbContext`; serwisy nie referują BFF |
| A6 | **zrobione:** Knowledge i SleepDiary za BFF experience `example`: fasada 30 operacji pod `/v1/knowledge/...` i `/v1/sleepdiary/...`, endpoint komponowany `GET /v1/me/summary`, API wewnętrzne `GET /internal/v1/widgets/sleep-summary` (scope `example.internal.read`); kontener `example-bff` w compose (port 5120), client scope `example-bff-audience` i `example.internal.read` w lokalnym realmie; sprawdzone E2E przez `https://localhost:5001/api/example/v1/...` |
| B2 | **zrobione** w lokalnym zamienniku: migracja `RouteExperienceThroughBff` usuwa trasy `/api/knowledge/**` i `/api/sleepdiary/**` i dodaje jedną trasę `/api/example/v{version:int}/{**rest}` do `example-bff` (polityka `GatewayPolicies.Example`, `PathRemovePrefix /api/example`); test bramy sprawdza, że żadna trasa nie zawiera `internal`. Dla wspólnej bramy pozostaje wymaganiem (A0) |
| B5 | **zrobione w części kodowej:** `securitySchemes` Bearer (`OpenApiBearerSecurityTransformer`), stabilne `operationId` (`{Controller}_{Action}`) i `x-abstract` dla typów polimorficznych w kontraktach serwisów i BFF; dwa kontrakty BFF (`Example.Bff_public.json`, `Example.Bff_internal.json`) z testem podziału. **Otwarte:** krok CI wykrywający zmiany łamiące (brak pipeline'u w repozytorium) |
| `docs/architektura.md`, instrukcje Copilota, przewodnik | synchronizacja z ADR-0037–0042 i z kodem BFF experience |
| Następne kroki | A5 (drugi host Api w szablonie serwisu), B4 (manifesty `NetworkPolicy` po ustaleniu procedury), krok CI z B5, C2–C4, D1, D4, E1–E4 |

---

## 1. Najważniejsze wnioski

1. **Słowo „BFF” znaczy co innego w notatce i w repo.**
   - W repo `bff-web` to brama brzegowa (token handler): logowanie OIDC, sesja, CSRF, zamiana ciasteczka na token. Nic nie
     orkiestruje.
   - W notatce i w docelowym modelu experience BFF to osobny komponent pod bramą, który orkiestruje wywołania naszych domen. Takiego
     komponentu (**BFF experience**) w repo **nie ma**, nie ma też dla niego szablonu.
   - To najważniejsza luka. Mechanizm brzegowy zgadza się z notatką.
2. **Granica publiczne/wewnętrzne przebiega dziś w złym miejscu.**
   - Brama ma trasę `/api/{serwis}/{**rest}` dla każdego serwisu domenowego w obu profilach
     (`src/Gateway/SuperApp.Gateway/Persistence/Seed/ProxyConfigurationSeed.cs:89`).
   - W efekcie **całe API serwisów domenowych jest publiczne**, a BFF, który miałby mieć API publiczne i wewnętrzne, nie istnieje.
   - W docelowym modelu brzeg kieruje ruch tylko do API publicznego BFF. API wewnętrzne BFF i serwisów nie jest routowane przez brzeg.
3. **Zasadę „do naszej domeny tylko przez nasz BFF” (P2) wymusza konfiguracja sieci, a nie tokeny.**
   - Token użytkownika ma audience wszystkich serwisów (ADR-0012). Po stronie tokenów inny BFF mógłby więc zawołać nasz serwis
     domenowy.
   - Decyzja właściciela projektu: **NetworkPolicy** dopuszcza ruch HTTP do naszych serwisów domenowych wyłącznie z podów naszego BFF
     i naszych serwisów. Model tokenów (ADR-0012) zostaje bez zmian.
   - Serwisy nadal walidują JWT i reguły zasobu (zero trust na poziomie tokenu i danych). Sieć decyduje tylko o tym, **kto może się
     połączyć**.
   - **Otwarta furtka:** token exchange (RFC 8693) z audience zawężonym do naszego serwisu i tożsamością pośrednika. Wprowadzamy go
     dopiero przy wyższych wymaganiach, np. dla danych medycznych o podwyższonej ochronie albo gdy NetworkPolicy nie wystarczy
     organizacyjnie. Kod i ADR-y nie mogą tej drogi zablokować (pakiet C, C1).
   - **Ryzyko rezydualne (akceptowane):** błąd konfiguracji sieci albo przejęty pod w dopuszczonym namespace'ie daje dostęp do API
     serwisu z dowolnym ważnym tokenem użytkownika. Środki zaradcze: test konfiguracji NetworkPolicy w pipeline'ie wdrożeniowym,
     przegląd zmian polityk, monitoring odrzuconych połączeń (B4).
4. **Wywołania serwis→serwis i BFF→serwis nie mają dziś kontroli dostępu do danych, problem opisany w notatce.**
   - Wzorzec repo dla s2s to token client credentials z identyfikatorem w payloadzie (ADR-0014, przewodnik 9.8).
   - Komponent z takim tokenem może pytać o dane dowolnej osoby.
   - Takich wywołań jeszcze nie ma w kodzie, więc teraz jest najtańszy moment na zmianę.
   - Model experience powiela ten problem dwa razy:
     - nasz BFF woła nasze serwisy w imieniu użytkownika;
     - inne experience wołają nasze API wewnętrzne, też w imieniu użytkownika (np. widget dashboardu).
5. **Zero trust w repo idzie dalej niż w notatce. Trzeba to zachować.**
   - Każdy serwis waliduje JWT z własnym audience.
   - Właściciel zasobu pochodzi tylko z `sub`, a cudzy zasób daje 404.
   - Notatka mówi „uprawnienia sprawdza BFF”. To nie może zastąpić kontroli w serwisie: BFF sprawdza wcześniej, serwis sprawdza zawsze.
6. **Tokeny wymagają uzupełnienia w dwóch miejscach: minimalizacji i unieważniania.**
   - Minimalizacja: realm wzorcowy wkłada imię, nazwisko i e-mail do access tokenu. Serwisy z tych danych nie korzystają, więc zmiana
     jest bez wpływu na kod.
   - Unieważnianie po wylogowaniu w mobile nie istnieje:
     - `ClockSkew` ma domyślne 5 min;
     - `offline_access` jest dozwolony dla `bff-web` i klientów mobilnych.
   - Unieważnianie na brzegu to zadanie wspólnej bramy (P1). Po naszej stronie zostaje krótki `ClockSkew` w BFF i serwisach oraz
     wymagania wobec bramy i CIAM.
7. **Nasz moduł działa w shellu super appki.**
   - Tożsamość, flagi, push i nawigacja należą do shella.
   - Repo zakłada co innego: moduł loguje się sam (AppAuth, ADR-0031) i ma SDK PostHog (ADR-0036).
   - Kod bramy przyjmie token shella bez zmian. Zmienić trzeba zasady i ADR-y.
8. **Integracja z dostawcą (~99% funkcjonalności) nie ma jeszcze wzorca. Repo celowo blokuje skróty:**
   - trasy prowadzą tylko do klastra;
   - token użytkownika nie wychodzi na zewnątrz.

   Właściwa droga to nasz serwis domenowy z ACL i listą dozwolonych operacji dla komponentów UI dostawcy. Brakuje też kanału dla
   komponentów webowych dostawcy w aplikacji mobilnej (webview).

---

## 2. Mapowanie pojęć

| Notatka / model experience | Repo dziś | Uwagi |
|---|---|---|
| super app | brak (otoczenie) | Całe rozwiązanie; my dostarczamy jeden moduł. |
| shell | brak (otoczenie) | Tożsamość, flagi, push, nawigacja (inny zespół). |
| edge gateway = API gateway | `SuperApp.Gateway`, profile `bff-web` (ciasteczko→token) i `gateway-mobile` (walidacja JWT) | Funkcja ta sama. **Brama jest wspólna dla super appki i poza naszym zakresem (P1)**: `SuperApp.Gateway` to lokalny zamiennik i specyfikacja wymagań. |
| BFF (experience) | **brak** | Pakiet A. |
| API publiczne / wewnętrzne BFF | brak; publiczne są dziś API serwisów | Pakiet B. |
| komponent domenowy | serwis domenowy = bounded context (`{Serwis}.*`, szablon `superapp-service`) | Zgodne. |
| moduł aplikacyjny | brak (`web/`, `mobile/` nie istnieją) | Kod klienta jest poza repo albo dopiero powstanie. |
| dashboard, Core/Shell BFF | brak | **Poza zakresem**; to tylko potencjalni konsumenci naszego API wewnętrznego. |

**Rekomendacja nazewnicza:**
- w dokumentach profil `bff-web` nazywać „bramą brzegową web (token handler)”;
- „BFF” zarezerwować dla BFF experience;
- nazw w kodzie (`/bff/*`, `GatewayProfiles.BffWeb`) nie zmieniać, bo są kontraktem z SPA.

---

## 3. Tabela zgodności

Oceny: **Z** zgodne, **C** częściowo, **R** rozbieżne, **B** brak, **P** poza zakresem (repo lub experience).

### 3.1 Uwierzytelnianie i brama

| Ustalenie z notatki | Stan w repo | Ocena |
|---|---|---|
| Zamiana sesji (ciasteczko) na JWT na krawędzi, token nie trafia do przeglądarki | `bff-web`: ciasteczko `__Host-bff` (HttpOnly, Secure, Strict), sesja w MSSQL, transform dokleja Bearer i usuwa `Cookie` (`SuperApp.Gateway/Program.cs`, `Proxy/Transforms/SecurityTransforms.cs`, ADR-0011/0013) | Z |
| Gateway weryfikuje podpis i ważność JWT kluczami IdP | `gateway-mobile`: `AddJwtBearer` (issuer, audience, JWKS); dodatkowo **każdy serwis** waliduje JWT (ADR-0007) | Z (repo idzie dalej) |
| BFF sprawdza uprawnienie do API (np. aktywna usługa fizjoterapii), gateway bez kontroli domenowej | Brama sprawdza tylko „ma scope serwisu” (`Security/GatewayPolicies.cs`); serwis sprawdza `[RequiresScope]` i reguły zasobu. Pojęcia uprawnienia do usługi (entitlement) nie ma, BFF też nie | C |
| Unieważnianie tokenu po wylogowaniu w mobile; krótki TTL albo czarna lista | Web: back-channel logout kończy sesję BFF. Mobile: brak mechanizmu. AT 300 s, `ClockSkew` domyślnie 5 min, `offline_access` dozwolony (`deploy/local/keycloak/realm-superapp.json`) | B (mobile), C (web) |
| JWT w integracji domenowej jeszcze w dyskusji; legacy bez JWT | Repo wymaga JWT w każdym serwisie bez wyjątków (`AddAppApi`, polityka fallback) | R (repo idzie dalej) |
| Zero trust dla nowych komponentów, wyjątki nie mogą stać się regułą | Zero trust jest regułą; brak procedury i rejestru wyjątków dla legacy | C |
| Token zawiera imię, nazwisko, e-mail, telefon; rozważany podział na access token i identity token | Realm: PII w access tokenie (mappery `profile` i `email`; `phone` jako scope opcjonalny). Kod czyta z tokenu tylko `sub` i `scope` | R (realm), Z (kod) |
| s2s: uchwyt obiektu w payloadzie, brak kontroli data access | Client credentials + identyfikator w payloadzie (ADR-0014, przewodnik 9.8); Worker działa jako `SystemCurrentUser` z każdym scope | R (ten sam problem) |
| Sam access token nie wystarczy; serwis musi porównać identyfikator obiektu z claimami (także pośredni) | Wzorzec „właściciel z `sub`, filtr w zapytaniu, cudze = 404” działa tylko przy bezpośredniej własności; brak mapowania `sub` na pacjenta | C |
| API ogólne (placówki, metryki) bez kontekstu użytkownika | Polityka `anonymous`, `[AllowAnonymousRequest]`, `[AllowAnonymous]` (trzy poziomy) | Z |

### 3.2 BFF experience i API

| Ustalenie z notatki (w zakresie experience) | Stan w repo | Ocena |
|---|---|---|
| BFF experience pod bramą, orkiestruje wywołania naszych domen i może czytać inne domeny (EHR, konfiguracja) | Brak komponentu i szablonu; s2s tylko wyjątkowo przez ACL | B |
| API publiczne BFF z jednym konsumentem (nasz moduł), ten sam zespół, bez breaking changes | Brak BFF. Dziś klient woła API serwisów domenowych przez bramę | B |
| API wewnętrzne dla innych experience (konsumenci: BFF-y innych experience), niepubliczne | Brak; nie ma ścieżki ani dokumentu „internal”; całe API serwisów jest publiczne przez catch-all (`ProxyConfigurationSeed.cs:89`) | R |
| Inne BFF-y nie wołają bezpośrednio naszych serwisów domenowych, ruch idzie przez nasz BFF (P2) | Brak NetworkPolicy w repo i w wymaganiach (ADR-0016 mówi ogólnie „ruch tylko z namespace'u bram”); tokeny tego nie ograniczają (ADR-0012), i zgodnie z decyzją nie muszą | B (NetworkPolicy) |
| Zabezpieczenie, by z API modułu nie korzystały inne moduły | Brak. Uwaga: w super appce moduły mają wspólny token shella, więc izolacja techniczna wymaga osobnego audience lub scope dla naszego BFF (pytanie P4) | B |
| BFF nie woła innych BFF-ów | Nie dotyczy (nasz BFF niczego takiego nie robi); reguła analogiczna do reguły 1 testów architektury | — |
| Unikanie zmian łamiących | `/v1` w ścieżce, reguły w przewodniku 8; **brak CI**, więc wykrywanie zmian łamiących (ADR-0014/0019) jest tylko deklaracją; kontrakty bez `securitySchemes` | C |
| Dashboard, częściowe renderowanie, Core/Shell BFF, kilkanaście BFF-ów organizacji | — | P (inne experience) |

### 3.3 Domeny i dostawca

| Ustalenie z notatki | Stan w repo | Ocena |
|---|---|---|
| Experience = moduł aplikacyjny + BFF + 1..n serwisów domenowych | Jest serwis domenowy i jego szablon; brak BFF i modułu | C |
| Odrębne obszary (Vital, fizjoterapia, mental), bez wspólnego komponentu „podstawowego” | Mikroserwis = bounded context, zakaz współdzielenia modelu (zasady architektury §6) | Z |
| Dzienniczek (np. nastroju) jako osobny mikroserwis | SleepDiary to ten sam wzorzec (ADR-0029) | Z |
| Domeny nie muszą być małe; podział pacjent/konfiguracja głównie dla skalowania | Szablon ma jeden host Api na serwis; osobne skalowanie wymagałoby dziś podziału kontekstu | C |
| Plan opieki łączy kilka obszarów w jednej ścieżce | Sagi, zdarzenia integracyjne, lokalne kopie | Z (mechanizm jest) |
| API proxy do dostawcy w BFF albo w serwisie, dla specjalisty i pacjenta | Brak wzorca. Trasy tylko do klastra (ADR-0022), token nie wychodzi na zewnątrz (ADR-0012), ACL Refit + Refitter gotowy (ADR-0014). Brak rozróżnienia pacjent/specjalista | C |
| Szablony planów i automatyczne ćwiczenia po stronie zespołu | Kandydat na agregaty naszego serwisu; szablon serwisu gotowy | P (domena) |

### 3.4 Moduł w super appce i narzędzia

| Ustalenie z notatki | Stan w repo | Ocena |
|---|---|---|
| Shell dostarcza tożsamość | Repo: moduł loguje się sam (AppAuth, klienci `mobile-*`, ADR-0031). Kod bramy przyjmie token shella z audience `gateway-mobile` | R (zasady), Z (kod) |
| Shell dostarcza feature flags | PostHog: `IFeatureFlags` w backendzie, SDK PostHog w mobile (ADR-0036) | R (mobile), C (backend jest za portem) |
| Shell dostarcza push i nawigację | — | P |
| Moduł obejmuje całą journey | Pojęcie modułu klienta nie istnieje | B |
| Komponenty dostawcy (React) opakowane w Angular, w mobile przez warstwę pośrednią (webview) | Kanały to tylko ciasteczko (web) i natywny Bearer (mobile); brak kanału webview | B |
| Modelowanie w narzędziu, ręczne kopiowanie Markdowna do Git | Docs-as-code (arc42, ADR, przewodnik), diagramy Mermaid | C |

---

## 4. Co w repo jest lepsze niż notatka (nie cofać)

1. **Zero trust w każdym serwisie:** walidacja JWT z własnym audience, polityka fallback „uwierzytelniony”, właściciel z `sub`, 404
   dla cudzych zasobów. Kontroli nie przenosić wyłącznie do BFF.
2. **Brama nie zna domeny.** Polityki są w kodzie, wskazywane z bazy po nazwie; trasy tylko do klastra, z podwójną walidacją
   (ADR-0022). Logiki BFF nie dokładać do `SuperApp.Gateway`.
3. **Sesja i tokeny tylko po stronie serwera:**
   - back-channel logout;
   - wylogowanie chronione `sid`;
   - jedno odświeżanie na sesję w klastrze;
   - brak `offline_access` w kodzie BFF.

   To webowa połowa problemu z notatki.
4. **Token użytkownika nie wychodzi poza granicę zaufania** (ADR-0012). Ten bezpiecznik wymusza poprawny wzorzec integracji z
   dostawcą.
5. **Serwisy nie czytają PII z tokenu; analityka używa pseudonimu zamiast `sub`** (ADR-0036).
6. **Zmiany tras migracjami z review i przeładowaniem bez restartu.** Gotowy mechanizm, żeby przełączyć trasę experience z
   „brama → serwis” na „brama → BFF”.
7. **Zdarzenia i lokalne kopie zamiast synchronicznych łańcuchów; sagi dla procesów.** BFF czyta, nie koordynuje zapisów.
8. **`IFeatureFlags` i `ICurrentUser` jako porty.** Dostawcę flag (np. shell) i źródło tożsamości można zmienić bez dotykania serwisów.

---

## 5. Zakres poprawy

**Zasada nadrzędna:**
- jedna experience w repozytorium to jeden BFF i 1..n serwisów domenowych;
- brzeg kieruje ruch modułu wyłącznie do API publicznego BFF;
- API wewnętrzne BFF i API serwisów są dostępne tylko w klastrze;
- szablony (`superapp-bff`, `superapp-service`) i reguły nie zakładają liczby experience, więc kolejna experience (nasza albo innego zespołu)
  powstaje tak samo.

Zaakceptowanych ADR-ów nie edytuje się merytorycznie (ADR-0001), więc każda zmiana to **nowy ADR**, który zastępuje lub doprecyzowuje
istniejący. Nakład: S do 2 dni, M do tygodnia, L powyżej tygodnia.

### Pakiet A: BFF experience (priorytet wysoki)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| A0 | ADR „`SuperApp.Gateway` jako lokalny zamiennik wspólnej bramy brzegowej”, który doprecyzowuje ADR-0006, ADR-0011, ADR-0013 i ADR-0022:<br>• brama jest wspólna dla super appki i poza zakresem experience;<br>• w repo zostaje do developmentu i E2E (compose);<br>• Helm `superapp-gateway` i jego migracje nie są artefaktem wdrożenia experience;<br>• zachowanie lokalnej bramy jest **specyfikacją wymagań** wobec wspólnej bramy: sesja po stronie serwera, `__Host-` cookie, CSRF, back-channel logout, usuwanie `X-User-*`/`Cookie`, Bearer do BFF, unieważnianie;<br>• lokalnie trasy prowadzą do naszego BFF;<br>• proxy `/ingest` (ADR-0036) to wymaganie wobec wspólnej bramy albo endpoint naszego BFF | `docs/adr`, zasady architektury §3–4, §12–13, `docs/architektura.md` §5 i §7, przewodnik 02/09/13 | S | architekt (potwierdzone P1) |
| A1 | ADR „Experience: moduł, BFF i serwisy domenowe”: słownik (super app, shell, experience, brama brzegowa, BFF); BFF bezstanowy, bez bazy i domeny, orkiestruje w obrębie experience; waliduje JWT (zero trust); nie woła innych BFF-ów; „BFF sprawdza wcześniej, serwis zawsze”; jedna experience = jeden BFF, wiele experience = wiele BFF z tego samego szablonu | `docs/adr`, zasady architektury §1, §3–4, §13, `docs/architektura.md` §3 i §5, przewodnik | S | architekt + właściciel projektu |
| A2 | Projekt BFF experience w repo i szablon `src/Tools/SuperApp.Cli/Templates/superapp-bff`: ASP.NET Core MVC, `AddAppServiceDefaults` (wariant bez bazy już jest), `AddAppApi`, klienci Refitter do naszych serwisów, `HybridCache`, osobne kontrakty OpenAPI (publiczny i wewnętrzny, pakiet B), Dockerfile, chart Helm | `src/Bff/…` (nazwa do ustalenia), `templates/`, `deploy/helm` | M | po A1 |
| A3 | Framework dla orkiestracji w BFF: przekazywanie kontekstu użytkownika do serwisów (pakiet C) oraz wywołania równoległe z timeoutem per wywołanie i obsługą częściowej niedostępności (`Microsoft.Extensions.Http.Resilience`, już w stacku) | `SuperApp.Framework.Infrastructure/Http` | M | po A1 |
| A4 | Testy architektury dla BFF: zależy tylko od wygenerowanych klientów i `Contracts`, nie od warstw serwisów ani innego BFF; nie ma `DbContext`; serwisy nie zależą od BFF. Dziś `Solution.cs` rozpoznaje tylko serwisy z `Domain.dll` | `tests/SuperApp.ArchitectureTests` | S–M | — |
| A5 | Opcja szablonu serwisu: drugi host Api (np. konfiguracyjny lub administracyjny) na tym samym kontekście, do osobnego skalowania bez dzielenia domeny (notatka: podział pacjent/konfiguracja głównie dla skalowania) | `src/Tools/SuperApp.Cli/Templates/superapp-service`, ADR uzupełniający ADR-0030, chart | M | właściciel projektu |
| A6 | Migracja przykładów: Knowledge i SleepDiary za BFF przykładowej experience, żeby repo pokazywało docelowy przepływ moduł → brama → BFF → serwisy | `src/`, migracja tras bramy, E2E | M | po A2 i B2 |

### Pakiet B: API publiczne i wewnętrzne na poziomie BFF (priorytet wysoki)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| B1 | ADR „API publiczne i wewnętrzne”:<br>• **BFF:** publiczne `v{n}/...` (konsument: nasz moduł, przez brzeg) i wewnętrzne `internal/v{n}/...` (konsumenci: inne experience, tylko w klastrze);<br>• **serwisy domenowe:** całe API wewnętrzne, konsumentem jest nasz BFF lub serwisy;<br>• dwa dokumenty OpenAPI BFF (`*.public.json` dla klienta modułu, `*.internal.json` dla innych experience i Refittera); kontrakty serwisów zostają, ale jako wewnętrzne;<br>• doprecyzowanie ADR-0009/0019 („jeden kontrakt per serwis”) | `docs/adr`, `HostingExtensions.ConfigureOpenApi`, generowanie kontraktów | M | architekt |
| B2 | Trasy brzegu: `/api/{experience}/v{n}/**` tylko do BFF, nigdy do serwisów domenowych ani `internal`. Dla wspólnej bramy to **wymaganie** wobec jej właściciela; w lokalnym zamienniku usunąć catch-all per serwis (migracja bramy oraz test „żadna trasa nie prowadzi do serwisu domenowego ani `internal`”) | wymagania (A0), lokalnie `ProxyConfigurationSeed.cs`, migracja, `SuperApp.Gateway.Tests` | S–M | po B1 |
| B3 | ADR „Dostęp do API wewnętrznego i do serwisów domenowych” (P2):<br>• **gwarancja sieciowa:** serwisy domenowe przyjmują ruch tylko od naszego BFF i naszych serwisów (B4);<br>• **inne BFF-y wołają nasze API wewnętrzne domyślnie w kontekście użytkownika:** przekazują token użytkownika (audience naszego BFF jest w tokenie, ADR-0012) i potrzebują scope `{experience}.internal.*`; nasz BFF i serwisy stosują reguły zasobu jak dla własnego modułu;<br>• **wyjątek, wywołania systemowe:** client credentials, tylko do jawnie oznaczonych operacji (`{experience}.internal.system.*`), nigdy z danymi konkretnej osoby bez jej kontekstu, z audytem wołającego i uzasadnieniem w kontrakcie;<br>• **nasz BFF → nasze serwisy:** przekazuje token użytkownika bez zmian (zmienia zasadę „nigdy token użytkownika” w obrębie experience; przewodnik 9.8, zasady architektury §3/§5);<br>• **otwarta furtka:** token exchange (C1) jako opcja na później, bez zmian w kontraktach | ADR, polityki w BFF, `SuperApp.Framework.Infrastructure/Http` (handler przekazujący token) | S–M | architekt |
| B4 | **NetworkPolicy jako główny mechanizm P2**. Wymaganie wobec działu infrastruktury (nowy ADR uzupełniający ADR-0016) i manifesty w chartach Helm, jeśli dział je przyjmuje od zespołów:<br>• ingress do serwisów domenowych: tylko pody naszego BFF i naszych serwisów (selektory etykiet experience);<br>• ingress do API wewnętrznego naszego BFF: tylko namespace'y BFF-ów innych experience;<br>• ingress do API publicznego BFF: tylko wspólna brama brzegowa;<br>• **egzekwowanie:** test konfiguracji polityk w pipeline'ie (np. weryfikacja renderowanych manifestów), przegląd zmian, alert na odrzucone połączenia;<br>• lokalnie compose nie odwzorowuje NetworkPolicy: zaznaczyć to w przewodniku 13 | `deploy/helm` (etykiety experience, opcjonalnie `NetworkPolicy`), ADR, wymagania | S–M | architekt + dział infrastruktury |
| B5 | `securitySchemes` (Bearer) w kontraktach OpenAPI (zmiana niełamiąca) i CI z wykrywaniem zmian łamiących, szczególnie dla API publicznego (aplikacja w sklepie, długie życie wersji) i wewnętrznego (konsumenci z innych zespołów) | transformer w `SuperApp.Framework`, pipeline CI (brak `.github/workflows`) | S–M | zespół + DevOps |

### Pakiet C: autoryzacja w kontekście użytkownika i dane (priorytet wysoki, dane medyczne)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| C1 | ADR „Wywołania w kontekście użytkownika”:<br>• **teraz:** nasz BFF przekazuje token użytkownika bez zmian do naszych serwisów, a BFF-y innych experience do naszego BFF; ochronę dają NetworkPolicy (B4), walidacja JWT i reguły zasobu w serwisie;<br>• client credentials tylko dla operacji systemowych i danych ogólnych, ze scope `{experience}.internal.system.*` (ADR-0040) i audytem;<br>• **otwarta furtka:** token exchange (RFC 8693) z audience zawężonym do celu i tożsamością pośrednika. Port lub `DelegatingHandler` w `SuperApp.Framework.Infrastructure/Http` projektujemy tak, żeby przejście z przekazywania tokenu na wymianę było zmianą konfiguracji i implementacji handlera, bez zmian w BFF i serwisach | `docs/adr`, `SuperApp.Framework.Infrastructure/Http` (handler przekazujący token, z miejscem na wymianę) | S–M | architekt; dział CIAM dopiero przy uruchomieniu furtki |
| C2 | Autoryzacja na poziomie obiektu: identyfikator podmiotu domenowego (np. pacjent) w `ICurrentUser` lub porcie, mapowany z `sub` (claim od CIAM albo mapa w serwisie); przepis w przewodniku dla identyfikatorów pośrednich (dokument → wizyta → pacjent, filtr w SQL, 404) | `SuperApp.Framework.Application/Security`, przewodnik 9 | M–L | architekt |
| C3 | Uprawnienie do usługi (np. aktywna fizjoterapia) jako fakt domeny, z właścicielem i zdarzeniami. Serwis go egzekwuje (lokalna kopia lub port `I{Usługa}EntitlementGateway`), BFF sprawdza wcześniej dla UX. Uprawnienie nie trafia do tokenu jako claim | ADR, przewodnik | S (zasady) + M | architekt (kto jest źródłem prawdy) |
| C4 | ADR „Wyjątki od zero trust”: rejestr wyjątków (komponent, powód, właściciel, data wygaśnięcia) i środki kompensujące | `docs/adr` | S | architekt |

### Pakiet D: tokeny (część to szybkie zmiany bez ryzyka)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| D1 | Minimalny access token: `profile`, `email`, `phone` z `access.token.claim: false` albo lightweight access token. PII zostają w ID tokenie i userinfo (`/bff/user` już korzysta z userinfo). Wymaganie wobec CIAM: access token bez PII. ID tokenu **nie** przekazywać do serwisów: jego `aud` to klient, nie API | realm, ADR uzupełniający ADR-0012/0016 | S | dział CIAM (token organizacji jest współdzielony) |
| D2 | `ClockSkew` ok. 30 s w `AddAppApi` (BFF i serwisy), lokalnie także w `AddGatewayMobile`; ta sama wartość jako wymaganie wobec wspólnej bramy | `HostingExtensions.cs`, `SuperApp.Gateway/Program.cs`, wymagania (A0) | S | — |
| D3 | Usunąć `offline_access` z opcjonalnych scope `bff-web` (zgodność z ADR-0011); dla mobile decyzja należy do shella | realm | S | właściciel produktu / shell |
| D4 | Unieważnianie po wylogowaniu: **wymaganie wobec wspólnej bramy i CIAM** (P1), nie nasza implementacja: denylista po `sid` sprawdzana na brzegu i zasilana back-channel logoutem albo introspekcja dla tras wrażliwych. Po naszej stronie: krótki `ClockSkew` (D2), a po ewentualnym uruchomieniu furtki token exchange (C1) krótki czas życia tokenów wymienianych przez BFF | wymagania (A0), ADR | S | architekt + właściciel bramy + dział CIAM |

### Pakiet E: moduł w super appce i dostawca (decyzje z innymi zespołami)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| E1 | ADR „Moduł w super appce: kontrakt z shellem”, doprecyzowuje ADR-0031 i ADR-0036 w części klienta:<br>• **tożsamość:** należy do shella; moduł bierze token przez port (implementacja shell albo własny AppAuth, gdy moduł działa samodzielnie);<br>• **flagi:** widoczność modułu i wyłączniki należą do shella; flagi funkcji modułu ocenia nasz backend (`IFeatureFlags`) i zwraca BFF; bez drugiego SDK flag w module;<br>• **analityka:** zgoda pochodzi z shella; zdarzenia modułu przez mechanizm shella albo PostHog z pseudonimem | `docs/adr`, zasady architektury §4–5, przewodnik 21 | S | architekt + zespół shella + właściciel ADR-0036 |
| E2 | Kanał webview dla modułu webowego w shellu (Angular z komponentami dostawcy):<br>(A) mostek shella dokleja Bearer lub podaje token na żądanie, trzymany tylko w pamięci;<br>(B) jednorazowa wymiana tokenu shella na sesję ciasteczka na brzegu.<br>Oba warianty dotyczą shella i wspólnej bramy, więc dla nas to wymagania. Wstrzykiwanie tokenu do JS tylko jako świadomy wyjątek | `docs/adr` (wymagania) | S | architekt + zespół shella + właściciel bramy |
| E3 | ADR „Integracja z rozwiązaniem dostawcy”:<br>(1) **proxy dla komponentów UI** w naszym serwisie domenowym (albo dedykowanym serwisie integracyjnym) z listą dozwolonych operacji, wystawione przez API publiczne BFF; waliduje nasz JWT, mapuje użytkownika na identyfikator u dostawcy, egzekwuje reguły pacjenta i specjalisty, woła dostawcę **własnymi poświadczeniami** z Vault; bez tunelu `/{**rest}`;<br>(2) **integracja domenowa** (szablony planów, automatyczne ćwiczenia) przez ACL Refit + Refitter z commitowanej kopii kontraktu dostawcy;<br>(3) DPA i art. 9 RODO | `docs/adr`, serwis z szablonu | M–L | architekt + zespół; najpierw fakty od dostawcy |
| E4 | Kanał specjalisty (uruchamiany z EHR): ta sama experience z rolą specjalisty, czy osobna experience (inny klient OIDC, inna brama)? Rozróżnienie ról w scope | ADR | S | architekt |

### Pakiet F: proces i narzędzia (priorytet niski)

| # | Zmiana | Gdzie | Nakład | Decyzja |
|---|---|---|---|---|
| F1 | ADR „Notacja i źródło prawdy diagramów”: Git jako źródło prawdy (zmiany przez PR), C4 jako szkielet arc42 §3 i §5, jedna notacja tekstowa albo pliki narzędzia zespołu renderowane w CI | `docs/adr`, `.github/instructions/docs.instructions.md`, przewodnik | S | architekt (po potwierdzeniu nazwy narzędzia) |

### Elementy wcześniejszej wersji raportu, które wypadają z zakresu

- Decyzja o budowie dashboardu (wcześniej A6) i koperta wyników widgetów (część A3): to sprawa experience dashboardu. Z naszej
  strony wystarczy dobre API wewnętrzne (B1, B3) i jego dokumentacja, np. endpoint widgetu w `internal/v1`.
- Profile bramy dla wielu experience, Core/Shell BFF, kilkanaście BFF-ów organizacji: poza zakresem. Szablony nie mogą tego
  blokować (zasada nadrzędna).

### Proponowana kolejność

1. **Od razu, bez decyzji (ok. 1 dzień):**
   - D2 (`ClockSkew`);
   - D3 dla `bff-web` w lokalnym realmie.
2. **ADR-y wynikające z decyzji P1 i P2:** A0, A1, B1, B3, B4 (NetworkPolicy jako wymaganie dla działu infrastruktury). Sesja z
   architektem na pytaniach P3–P8.
3. **Po decyzjach:**
   - A2–A4 i B2 jako pilot na przykładowej experience;
   - A6, czyli przeniesienie przykładów za BFF;
   - C1 w implementacji;
   - D1, B4, C4.
4. **Gdy znane będą fakty o dostawcy i shellu:** E2, E3, E4, D4.
5. **Równolegle:** F1 i B5.

---

## 6. Pytania otwarte

**Do architekta (topologia i API):**
- ~~**P1.** Czy brama brzegowa jest nasza, czy wspólna?~~ **Rozstrzygnięte:** wspólna dla super appki, poza zakresem experience
  (A0, B2, D4, E2 jako wymagania).
- ~~**P2.** Kto konsumuje API wewnętrzne?~~ **Rozstrzygnięte:** BFF-y innych experience. Ruch do naszej domeny wyłącznie przez nasz
  BFF, gwarantowany NetworkPolicy (B4); token exchange jako otwarta furtka (C1). Kontekst wywołań: **domyślnie w imieniu użytkownika**; wywołania systemowe są teoretycznie dopuszczalne, ale tylko jako
  wyjątek (B3).
- **P3.** Kto jest źródłem prawdy uprawnień do usługi (aktywna fizjoterapia)? Osobny serwis, CIAM czy EHR? Czy nasz serwis domenowy też
  je egzekwuje? Rekomendacja: tak.
- **P4.** Czy API publiczne BFF ma być technicznie niedostępne dla innych modułów? Moduły mają wspólny token shella, więc wymagałoby
  to osobnego audience, scope albo token exchange. Druga opcja to granica organizacyjna: kontrakt i scope.

**Do architekta i działu CIAM (tokeny):**
- ~~**P5.** Własny token bramy czy token CIAM?~~ **Rozstrzygnięte:** za bramą krąży **access token wystawiony przez CIAM**
  (zakładany Keycloak). Brama nie wystawia własnych tokenów (bez phantom token). BFF i serwisy ufają jednemu wystawcy: CIAM
  (`Authentication:Authority`). Dotyczy to również tokenów client credentials. To wymaganie wobec wspólnej bramy (ADR-0037).
- **P6.** IdP produkcyjny: **najprawdopodobniej Keycloak** (decyzji jeszcze nie ma). Keycloak obsługuje token exchange (RFC 8693),
  lightweight access token, mappery per klient i back-channel logout, więc furtka C1 jest realna. Do potwierdzenia przy decyzji
  o IdP: wersja i konfiguracja po stronie działu CIAM (m.in. back-channel logout dla klientów mobilnych, D4).
- **P6a.** NetworkPolicy: **procedury do doprecyzowania, brak decyzji.** Otwarte: czy dział infrastruktury przyjmuje manifesty od
  zespołów (w chartach), czy tylko wymagania, oraz jak w klastrze identyfikuje się pody experience (etykiety, namespace per
  experience). Do tego czasu B4 przygotowujemy jako wymaganie z etykietami experience w chartach, bez manifestów `NetworkPolicy`.
- **P7.** Jaki czas życia access tokenu? Fail-open czy fail-closed przy niedostępnej denyliście?
- **P8.** Jak mapować `sub` na identyfikator pacjenta lub karty: claim od CIAM czy mapa w serwisie?

**Do zespołu shella:**
- **P9.** Jakie API mostka shella dostają moduły: token, odświeżanie, zdarzenie wylogowania, flagi, zgoda analityczna, push, deep
  linki? Czy działa ono także w webview?
- **P10.** Czy shell ma własny provider flag i analityki? Czy dopuszczamy PostHog w module?

**Do zespołu i dostawcy:**
- **P11.** W jakich kanałach działa nasz moduł: natywnie, w webview z Angularem, w webowym portalu pacjenta? Czy Angular w webview jest
  ładowany zdalnie, czy z paczki?
- **P12.** Czy komponenty React dostawcy pozwalają ustawić base URL API (warunek proxy)?
  - Jak uwierzytelnia się API dostawcy: klucz, client credentials czy token per użytkownik?
  - Kto prowadzi mapowanie pacjenta i specjalisty?
- **P13.** Czy dostawca publikuje kontrakt OpenAPI? W jakiej wersji i z jaką polityką zmian łamiących?
- **P14.** Gdzie przechowywane są dane medyczne: u dostawcy czy u nas? DPA, region, art. 9 RODO.
- **P15.** Specjalista uruchamia nas z EHR (inna aplikacja, inny IdP?): ta sama experience czy osobna? Jakie role?
- **P16.** Czy podział na część pacjencką i konfiguracyjną wynika tylko ze skalowania (wtedy drugi host Api, A5), czy z innego języka
  domeny i innego właściciela (wtedy osobne konteksty)?

**Organizacyjne:**
- **P17.** Które komponenty legacy dostaną wyjątek od zero trust i do kiedy?
- **P18.** Jak nazywa się narzędzie do modelowania z notatki (OCR: „Plan tu MLA”, prawdopodobnie PlantUML) i czy ma eksport do repo?

---

## 7. Uwagi do niedawnych zmian

- **ADR-0036 (PostHog), część klienta mobilnego:** zakłada SDK PostHog i własną inicjalizację w aplikacji. W super appce może to
  dublować flagi i zgodę shella. Część backendowa (port `IFeatureFlags`, forwarder, proxy `/ingest` dla web) pozostaje neutralna.
  Doprecyzowanie: E1. Flagi funkcji modułu docelowo zwraca nasz BFF.
- **Proxy `/ingest` i `analyticsId` w `/bff/user` (ADR-0036)** zostały zrobione w lokalnej bramie. Skoro brama jest wspólna (P1),
  to albo wymaganie wobec wspólnej bramy, albo endpointy naszego BFF (`/v1/analytics/id`). Część backendowa (forwarder, `IFeatureFlags`)
  się nie zmienia. Doprecyzowanie: A0 i E1.
- **Limit żądań dla ruchu anonimowego** w lokalnej bramie: partycja jest liczona po `RemoteIpAddress` bez `ForwardedHeaders`. Za
  Ingressem cały ruch przed zalogowaniem trafia do jednej partycji. Dla wspólnej bramy to wymaganie wobec jej właściciela; ma to
  znaczenie także, jeśli nasz BFF dostanie anonimowe endpointy (S).
- **SPA nie zna `ProjectToken` przed logowaniem** (`/bff/user` go nie zwraca). Do decyzji: konfiguracja środowiska SPA albo anonimowy
  endpoint konfiguracyjny (S).
- **Brak testów automatycznych** dla `PostHogFeatureFlags` (timeout, fallback), `PostHogProductEventSink` oraz `/ingest` i
  `/analytics/id` w `SuperApp.Gateway.Tests`. Te ścieżki sprawdzono dotąd tylko E2E (M).
- **Reguła architektury 1** dopuszcza już zależność od cudzych `Contracts`. To zgodne z modelem, w którym BFF i serwisy czytają
  zdarzenia innych domen.
