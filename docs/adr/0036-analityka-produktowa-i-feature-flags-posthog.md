# ADR-0036: Analityka produktowa, session replay i feature flags w PostHog Cloud EU

- **Status:** Zaakceptowany (doprecyzowany przez ADR-0037: proxy `/ingest` jako wymaganie wobec wspólnej bramy; ADR-0045 (proponowany): flagi i zgoda od shella)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §2, §3, §4, §5, §9, §11, §12; ADR-0003, ADR-0006, ADR-0011, ADR-0012, ADR-0016, ADR-0020, ADR-0021,
  ADR-0022, ADR-0025, ADR-0031, ADR-0034

## Kontekst

Produkt potrzebuje czterech rzeczy, których obecna architektura nie zapewnia:

- **analityki produktowej:** lejki, retencja, użycie funkcji w Angularze i aplikacjach mobilnych;
- **session replay:** nagrania sesji do analizy problemów z używalnością;
- **zdarzeń z backendu:** faktów biznesowych potwierdzonych przez serwisy (np. opublikowany materiał, zapisany wpis snu), których
  klient nie może wiarygodnie zgłosić;
- **feature flags:** stopniowe udostępnianie funkcji, eksperymenty i wyłączniki awaryjne, spójne między frontendem a backendem.

Ograniczenia:

- **RODO.** SleepDiary przetwarza dane o zdrowiu (art. 9 RODO), a analityka i session replay w przeglądarce wymagają zgody
  użytkownika (ePrivacy).
- **Bramy.** Konfiguracja tras YARP w bazie (ADR-0022) pozwala kierować ruch wyłącznie do Service'ów w klastrze i zawsze dołącza
  token użytkownika.
- **Granice kontekstów.** Serwisy domenowe komunikują się zdarzeniami integracyjnymi i nie powinny zależeć od narzędzi produktowych.

Właściciel projektu wybrał PostHog Cloud w regionie EU (SaaS).

## Decyzja

### Dostawca i dane

- **Dostawca: PostHog Cloud EU** (`eu.i.posthog.com`, zasoby statyczne `eu-assets.i.posthog.com`), osobny projekt PostHog na
  środowisko (dev, test, prod).
  - PostHog jest zewnętrznym procesorem danych; umowę powierzenia (DPA) zawiera organizacja.
  - Wyjście z klastra do tych hostów to wymaganie dla działu infrastruktury (NetworkPolicy egress lub proxy, ADR-0016).
- **Pseudonim zamiast tożsamości.** PostHog nigdy nie dostaje `sub` z CIAM, e-maila ani nazwy.
  - Identyfikator użytkownika to `u_` + 32 znaki hex z `HMAC-SHA256(Analytics:IdKey, sub)` (`AnalyticsIdentity` w `SuperApp.Framework`).
  - Klucz jest w Vault, jest ten sam we wszystkich procesach środowiska i ma co najmniej 32 znaki.
  - Zmiana klucza dzieli historię użytkowników w PostHog, więc klucza się nie rotuje bez decyzji właściciela produktu.
- **Lista dozwolonych właściwości.**
  - Zdarzenia zawierają wyłącznie nazwy, identyfikatory obiektów katalogu i kategorie; nigdy treści wpisanej przez użytkownika ani
    danych z dziennika snu.
  - Zdarzenie `sleepdiary_entry_recorded` informuje tylko, że wpis powstał, bez daty, czasu snu i oceny.
  - Każda nowa właściwość zdarzenia przechodzi przegląd prywatności.
- **Zgoda.** Klienci web i mobile inicjalizują PostHog dopiero po zgodzie użytkownika; przed nią SDK nie zapisuje niczego w
  przeglądarce ani na urządzeniu. Wycofanie zgody wyłącza zbieranie i usuwa lokalny stan SDK.
- **Session replay.**
  - Maskowanie wszystkich pól i tekstów jest domyślne.
  - Ekrany dziennika snu oraz każde miejsce z danymi o zdrowiu są wyłączone z nagrywania w całości.
  - Projekt PostHog ma włączone odrzucanie adresów IP klienta.

### Klienci web i mobile

- **Angular:** `posthog-js` z `api_host: '/ingest'`. BFF wystawia **proxy `/ingest/{**path}`** do PostHog Cloud EU
  (`/ingest/static/{**path}` do hosta zasobów).
  - Dzięki temu analityka jest same-origin: nie blokują jej blokery treści i nie wymaga domeny zewnętrznej w CSP.
  - Proxy jest **zdefiniowane w kodzie bramy, nie w konfiguracji tras w bazie**. ADR-0022 i ograniczenie `CK_Destinations_ClusterAddress`
    zostają bez zmian, bo trasy z bazy prowadzą tylko do klastra i zawsze dostają token.
  - Proxy usuwa `Cookie`, `Authorization`, `X-User-*`, `X-CSRF` i nagłówki `X-Forwarded-*` / `Forwarded`, a z odpowiedzi `Set-Cookie`.
  - Jest anonimowe (analityka przed logowaniem, po zgodzie) i objęte limitem żądań.
  - Istnieje tylko wtedy, gdy analityka jest włączona.
- **Identyfikacja.** `/bff/user` zwraca `analyticsId`, a SPA wywołuje `posthog.identify(analyticsId)` po zalogowaniu i
  `posthog.reset()` przy wylogowaniu. Aplikacje mobilne (oficjalne SDK `posthog-android`, `posthog-ios`) pobierają identyfikator z
  `GET /analytics/id` bramy mobilnej. Przy wyłączonej analityce oba zwracają `null` i klient nie inicjalizuje PostHog.

### Zdarzenia z backendu

- **Osobny proces `SuperApp.AnalyticsForwarder`** (deployment `analytics-forwarder`, chart `superapp-analytics-forwarder`) subskrybuje
  zdarzenia integracyjne serwisów (`{Serwis}.Contracts`) i wysyła do PostHog zdarzenia z katalogu `ProductEventNames`.
  - Serwisy domenowe nie wysyłają zdarzeń analitycznych i nie zależą od PostHog.
  - Awaria PostHog nie wpływa na komendy.
  - Zdarzenia backendowe opierają się na faktach już zatwierdzonych i dostarczonych przez outbox.
- **Forwarder nie ma bazy, domeny ani inboxu.** Ponowne dostarczenie wiadomości może powtórzyć zdarzenie analityczne; dla
  analityki to akceptowalne, a jego konsumenci nie zmieniają stanu. To uzasadniony wyjątek od zasady „konsument deleguje do komendy
  MediatR” (§9): forwarder nie ma komend, tylko mapowanie.
- **Nazwy zdarzeń:** `{serwis}_{obiekt}_{czasownik w czasie przeszłym}` w snake case (`knowledge_material_published`).
  - Nazwy są kontraktem z osobami budującymi analizy: nowe się dodaje, istniejących się nie zmienia.
  - Zdarzenia systemowe mają identyfikator `system` i `$process_person_profile = false`.
- **Dostarczanie** jest „best effort”. Klient PostHog kolejkuje zdarzenia w pamięci, wysyła je partiami z ponowieniami i opróżnia
  kolejkę przy zatrzymaniu procesu. Rzadka utrata zdarzenia analitycznego jest akceptowana, dlatego logika biznesowa nigdy od
  analityki nie zależy.

### Feature flags

- **Port `IFeatureFlags`** (`SuperApp.Framework.Application`) z flagami deklarowanymi w kodzie jako `FeatureFlag(Key, DefaultValue)` w klasie
  `{Serwis}FeatureFlags` obok `{Serwis}Scopes`. Klucz ma prefiks serwisu, np. `knowledge_material_ratings`.
- **Implementacja na PostHog** (`SuperApp.Framework.Infrastructure`, rejestrowana przez `AddAppFeatureFlags`):
  - flagi są liczone dla pseudonimu bieżącego użytkownika (bez użytkownika: `system`), jednym zapytaniem na żądanie lub wiadomość;
  - wartości są spójne w obrębie zakresu DI;
  - czas oczekiwania jest ograniczony (`Analytics:FeatureFlagsTimeout`, domyślnie 1 s);
  - z kluczem `Analytics:FeatureFlagsKey` (Vault) ewaluacja odbywa się lokalnie w procesie.
- **Flaga nigdy nie psuje żądania.** Brak odpowiedzi, przekroczenie czasu lub nieznana flaga oznacza wartość domyślną z kodu, ostrzeżenie
  w logu (EventId 400–401) i wzrost metryki `superapp.feature_flags.fallbacks`. Wartość domyślna ma być bezpieczna do trwałego działania.
- **Bez analityki** (lokalnie, w testach) flagi pochodzą z konfiguracji `FeatureFlags:{klucz}` z wartością domyślną jako fallback.
- **Flaga to nie uprawnienie.** O dostępie decydują scope i reguły zasobu (ADR-0012). Zachowanie serwera zależne od flagi jest
  sprawdzane w backendzie, a flaga w UI służy tylko wyglądowi.

### Konfiguracja i egzekwowanie

- **Sekcja `Analytics`:**
  - `ProjectToken` – włącza analitykę; jawny, ale ustawiany per środowisko;
  - `IdKey` – sekret;
  - `FeatureFlagsKey` – sekret, opcjonalny;
  - `Host`, `AssetsHost` – tylko `https://*.posthog.com`;
  - `FeatureFlagsTimeout`.
- **Walidacja przy starcie.** Gdy analityka jest włączona, brak `IdKey` lub host spoza `posthog.com` zatrzymuje proces. Bez
  `ProjectToken` analityka jest wyłączona i nic nie jest wysyłane; tak jest domyślnie lokalnie (ADR-0034).
- **Testy architektury** (ADR-0025):
  - reguła 10: forwarder zależy od serwisów wyłącznie przez ich `Contracts`;
  - reguła 11: SDK PostHog używają tylko `SuperApp.Framework.Infrastructure` i `SuperApp.AnalyticsForwarder`;
  - reguła 1 dopuszcza zależność od cudzych `Contracts` (published language), a zabrania zależności od pozostałych warstw
    innych serwisów.
- **Pakiet:** `PostHog` 2.15.7 (MIT) w Central Package Management. Pakiety klientów (`posthog-js`, `posthog-android`, `posthog-ios`)
  dodaje się razem z kodem klientów.

## Konsekwencje

- **Plusy:**
  - jedno narzędzie do analityki, replay i flag, spójne między web, mobile i backendem dzięki wspólnemu pseudonimowi;
  - serwisy domenowe są nietknięte: flagi przez port, zdarzenia przez istniejące zdarzenia integracyjne;
  - konfiguracja tras bramy nie traci gwarancji z ADR-0022.
- **Minusy i ryzyka:**
  - zależność od zewnętrznego SaaS i transfer danych pseudonimowych poza organizację; wymaga DPA, zgody użytkownika i dyscypliny w
    liście dozwolonych właściwości;
  - session replay to najwyższe ryzyko prywatności, więc maskowanie i wyłączenia ekranów są obowiązkowe przed włączeniem;
  - nowy proces do utrzymania (forwarder) i nowe sekrety w Vault;
  - utrata pojedynczych zdarzeń backendowych jest możliwa; analizy nie mogą zastępować danych biznesowych z serwisów.
- **Do zrobienia poza repozytorium:**
  - DPA z PostHog i projekty PostHog na środowiska;
  - wymagania dla działu infrastruktury (egress, sekrety `Analytics__*` w Vault dla bram, serwisów i forwardera);
  - aktualizacja polityki prywatności i mechanizmu zgody w aplikacjach.
