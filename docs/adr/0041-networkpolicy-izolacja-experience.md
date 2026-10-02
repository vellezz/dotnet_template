# ADR-0041: NetworkPolicy jako gwarancja izolacji experience

- **Status:** Zaakceptowany (doprecyzowuje ADR-0016 w zakresie wymagań sieciowych; procedura wdrożenia do uzgodnienia)
- **Data:** 2026-10-01
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §5, §12; ADR-0012, ADR-0016, ADR-0037, ADR-0038, ADR-0039, ADR-0040

## Kontekst

Zasada „ruch do domeny experience tylko przez jej BFF” (ADR-0038, ADR-0039) nie wynika z tokenów. Token użytkownika ma audience
wszystkich serwisów (ADR-0012) i zgodnie z ADR-0040 przechodzi bez zmian. Właściciel projektu zdecydował, że **dostęp gwarantuje
konfiguracja sieci**.

Procedury działu infrastruktury dla NetworkPolicy (kto tworzy manifesty, jak identyfikować pody experience) **nie są jeszcze
ustalone**.

## Decyzja

- **Wymagane reguły ruchu przychodzącego (ingress)** dla każdej experience:

  | Cel | Dozwolone źródła |
  |---|---|
  | API publiczne BFF | wyłącznie wspólna brama brzegowa (ADR-0037) |
  | API wewnętrzne BFF (`/internal/...`) | BFF-y innych experience |
  | serwisy domenowe experience | wyłącznie pody BFF i serwisów **tej samej** experience |
  | porty sond (`/health/*`) | kubelet / monitoring (wg działu infrastruktury) |

  Rozróżnienie API publicznego i wewnętrznego BFF tylko po ścieżce nie jest możliwe na poziomie NetworkPolicy (warstwa L3/L4). Dwie
  opcje do uzgodnienia z działem infrastruktury:
  - osobny port (`Service` z dwoma portami) dla API wewnętrznego;
  - reguła w bramie: brama nie kieruje ruchu na `/internal` (ADR-0039) i polityka per ścieżka w BFF (scope `{experience}.internal.*`,
    ADR-0040).

  Do czasu decyzji obowiązuje druga opcja.
- **Identyfikacja podów.** Każdy zasób experience w chartach Helm dostaje etykiety:
  - `app.kubernetes.io/part-of: {experience}`;
  - `superapp.example/experience-role: bff | domain-service | worker`.

  Na nich opierają się selektory polityk.
- **Do czasu ustalenia procedur** przekazujemy reguły powyżej jako **wymaganie** wobec działu infrastruktury (ADR-0016), a charty
  zawierają etykiety, bez manifestów `NetworkPolicy`. Gdy dział zdecyduje, że przyjmuje manifesty od zespołów, dodajemy je do chartów
  (opcjonalnie włączane wartością).
- **Kontrola:**
  - przegląd zmian polityk;
  - test renderowanych manifestów w pipeline'ie wdrożeniowym, gdy manifesty będą w chartach;
  - alert na odrzucone połączenia (wymaganie wobec monitoringu).
- **Lokalnie** docker compose nie odwzorowuje NetworkPolicy. Przewodnik zaznacza, że lokalnie izolacja nie jest egzekwowana.

## Konsekwencje

- **Plusy:**
  - prosty, sprawdzony mechanizm niezależny od funkcji IdP;
  - nie zmienia kodu serwisów ani tokenów.
- **Ryzyko rezydualne (zaakceptowane):**
  - błąd konfiguracji sieci albo przejęty pod w dopuszczonym miejscu daje dostęp do API serwisu z dowolnym ważnym tokenem
    użytkownika. Dane chronią wtedy nadal walidacja JWT i reguły zasobu w serwisie (ADR-0040);
  - przy wyższych wymaganiach wprowadzamy token exchange (furtka z ADR-0040).
- **Zależność od innego działu:** skuteczność zależy od wdrożenia polityk przez dział infrastruktury, a procedura jest otwarta (P6a w
  raporcie).
- **Do zrobienia:**
  - etykiety experience w chartach (`superapp-service`, przyszły `superapp-bff`);
  - wymagania przekazane działowi infrastruktury;
  - wpis w przewodniku (rozdziały 09 i 13).
