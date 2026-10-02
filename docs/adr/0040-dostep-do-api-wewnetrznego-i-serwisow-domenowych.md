# ADR-0040: Dostęp do API wewnętrznego i do serwisów domenowych; przekazywanie tokenu użytkownika

- **Status:** Zaakceptowany (doprecyzowuje ADR-0012 i ADR-0014 w zakresie wywołań w kontekście użytkownika; uzupełniony przez ADR-0042: scope wywołań systemowych)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §3, §5; ADR-0007, ADR-0011, ADR-0012, ADR-0014, ADR-0017, ADR-0038, ADR-0039, ADR-0041

## Kontekst

W modelu experience (ADR-0038) występują trzy rodzaje wywołań wewnątrz klastra:

1. nasz BFF → nasze serwisy domenowe;
2. BFF innej experience → API wewnętrzne naszego BFF (ADR-0039);
3. nasz serwis → nasz serwis.

Dotychczasowe zasady (ADR-0014, przewodnik 9.8) przewidują wywołania serwis↔serwis z tokenem client credentials i identyfikatorem
w payloadzie. Notatka ze spotkania wskazuje problem: komponent z takim tokenem może pytać o dane dowolnej osoby (brak kontroli
dostępu do danych między komponentami).

Decyzje właściciela projektu:

- **Ruch do naszej domeny tylko przez nasz BFF.** Gwarancją jest konfiguracja sieci (ADR-0041), a nie mechanizm tokenów.
- **Token exchange (RFC 8693) to otwarta furtka.** Nie wdrażamy go teraz. IdP najprawdopodobniej będzie Keycloak, który go
  obsługuje; decyzji o IdP jeszcze nie ma.
- **API wewnętrzne wołamy domyślnie w kontekście użytkownika.** Wywołania systemowe są dopuszczalne tylko jako wyjątek.

## Decyzja

- **Przekazywanie tokenu użytkownika** to domyślny sposób wywołań w kontekście użytkownika (rodzaje 1–3). Token idzie bez zmian w
  nagłówku `Authorization`. To zmienia zasadę „serwis ↔ serwis zawsze client credentials” w obrębie experience i dla API wewnętrznego.
  - Token ma audience naszego BFF i naszych serwisów (ADR-0012 bez zmian).
  - Każdy odbiorca waliduje JWT (ADR-0007), sprawdza scope (`[RequiresScope]`) i reguły zasobu (właściciel z `sub`, cudze = 404).
    Odbiorca nie ufa identyfikatorowi użytkownika przesłanemu w payloadzie.
  - Implementacja: `DelegatingHandler` w `SuperApp.Framework.Infrastructure/Http`, który przepisuje token z bieżącego żądania, dla klientów
    Refit oznaczonych jako wywołania w kontekście użytkownika.
- **API wewnętrzne naszego BFF** wymaga scope `{experience}.internal.*`. Wywołania systemowe wymagają scope
  `{experience}.internal.system.*`.
- **Wywołania systemowe** (bez użytkownika) są wyjątkiem:
  - token client credentials (ADR-0014);
  - tylko do jawnie oznaczonych operacji (dane ogólne, zbiorcze, procesy w tle);
  - nigdy z danymi konkretnej osoby wskazanej wyłącznie identyfikatorem z payloadu;
  - audyt wołającego (`azp`) w logach;
  - uzasadnienie w dokumentacji operacji w kontrakcie.
- **Uprawnienia do usług** (np. aktywna usługa w danej domenie) to fakty domeny z właścicielem. Serwis domenowy je egzekwuje, a BFF
  może sprawdzić je wcześniej dla UX. Nie umieszczamy ich jako claimów w tokenie, bo zmieniają się w trakcie sesji.
- **Otwarta furtka: token exchange.** Handler przekazujący token projektujemy tak, by przejście na wymianę tokenu było zmianą tylko
  implementacji handlera i konfiguracji, bez zmian w BFF, serwisach i kontraktach. Wymiana zawęża audience do celu i dodaje tożsamość
  pośrednika. Wprowadzamy ją nowym ADR, gdy wymagania bezpieczeństwa wzrosną lub sieć okaże się niewystarczająca.
- Token użytkownika nadal **nie opuszcza granicy zaufania** aplikacji (ADR-0012). Systemy zewnętrzne, np. dostawca, wołamy własnymi
  poświadczeniami przez ACL.

## Konsekwencje

- **Plusy:**
  - jedna, prosta ścieżka;
  - dane osobowe są chronione regułami zasobu w serwisie niezależnie od tego, kto woła;
  - brak zależności od funkcji IdP, która nie jest jeszcze wybrana.
- **Ryzyka:**
  - token z szerokim audience przechodzi przez kilka komponentów. Przechwycony token pozwala wołać nasze serwisy z miejsc, które
    dopuszcza sieć. Środki zaradcze:
    - NetworkPolicy (ADR-0041);
    - krótki czas życia tokenu i `ClockSkew` 30 s;
    - zakaz logowania tokenów (ADR-0008);
    - furtka token exchange;
  - wywołanie w łańcuchu może trafić na token wygasający w trakcie. BFF nie odświeża tokenu (to rola bramy), więc długie łańcuchy
    projektujemy jako asynchroniczne.
- **Do zrobienia:**
  - handler przekazujący token w `SuperApp.Framework`;
  - aktualizacja przewodnika 9.8 i zasad architektury §3/§5;
  - przepis „wywołanie API wewnętrznego innej experience”.
