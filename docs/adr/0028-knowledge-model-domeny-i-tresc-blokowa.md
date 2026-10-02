# ADR-0028: Serwis Knowledge: model domeny i treść blokowa w tabelach relacyjnych

- **Status:** Zaakceptowany
- **Data:** 2026-09-30
- **Decydenci:** właściciel projektu
- **Powiązane:** zasady architektury §6, §7; ADR-0002, ADR-0012, ADR-0015, ADR-0021, ADR-0024

## Kontekst

Serwis Knowledge udostępnia materiały edukacyjne (artykuły, wideo, podcasty), grupowane
w kolekcje i przypisywane do kategorii. Użytkownicy dodają elementy do ulubionych i oznaczają
materiały jako przeczytane. Treść materiału składa się z bloków (nagłówki, akapity, cytaty,
listy, media itd.) z zagnieżdżaniem.

## Decyzja

### Agregaty

| Agregat | Zawartość | Zmienia |
|---|---|---|
| `Category` | nazwa, slug (unikalny); kategorie płaskie | redaktor |
| `Material` | typ (`Article`, `Video`, `Podcast`), tytuł, opis, główne medium (wideo/podcast: URL, czas trwania), kategorie, treść blokowa, status (`Draft`, `Published`, `Archived`), czas czytania, czysty tekst treści | redaktor |
| `Collection` | tytuł, opis, uporządkowana lista materiałów, kategorie, status | redaktor |
| `Favorite` | użytkownik + element (materiał lub kolekcja); unikalne | użytkownik |
| `MaterialCompletion` | użytkownik + materiał, data oznaczenia; unikalne | użytkownik |

- Użytkownik identyfikowany claimem `sub` (`UserId`); serwis nie przechowuje innych danych osobowych.
- Media przechowywane wyłącznie jako linki `https`.
- Scope: `knowledge.catalog.read`, `knowledge.catalog.write` (redaktor), `knowledge.library.read`,
  `knowledge.library.write` (ADR-0012).

### Treść blokowa

Katalog bloków: `Heading` (1–4), `Paragraph`, `Quote`, `List` → `ListItem` (zagnieżdżanie list do
3 poziomów), `Checklist` → `ChecklistItem`, `Callout` (`Info`, `Tip`, `Warning`, `Important`),
`Toggle` (do 2 poziomów), `KeyTakeaways` → `TakeawayItem`, `Divider`, `Image`, `Gallery` (2–12
obrazów), `Video`, `Audio`, `Embed` (dozwoleni dostawcy), `LinkCard`, `Code`, `Table` → `TableRow`
→ `TableCell` (do 20 × 10), `Timestamp`, `Transcript` → `TranscriptSegment`.

Tekst sformatowany: fragmenty ze znacznikami `Bold`, `Italic`, `Underline`, `Strikethrough`,
`Code`, `Highlight` oraz `Link` (adres).

Reguły (walidacja w agregacie, błędy jako `Result`): adresy wyłącznie `https` (linki w tekście
także `mailto`), `Embed` tylko od dozwolonych dostawców, `Image` wymaga tekstu alternatywnego,
maksymalnie 500 bloków, dozwolone typy dzieci per typ bloku, limity zagnieżdżenia, publikacja
wymaga treści, a `Video` i `Podcast` także głównego medium.

### Przechowywanie treści: tabele relacyjne

- `ContentBlocks`: jedna tabela dla wszystkich typów bloków (`MaterialId`, `ParentBlockId`,
  `Position`, `Type` oraz kolumny zależne od typu); ograniczenia `CHECK` per typ.
- `ContentTextSpans`: fragmenty tekstu sformatowanego (`BlockId`, `Position`, `Text`, `Marks`,
  `LinkHref`).
- Elementy złożone (pozycje list, wiersze i komórki tabel, obrazy galerii, segmenty transkrypcji)
  są węzłami potomnymi tego samego drzewa.
- Treść zapisywana w całości (`Material.ReplaceContent`); drzewo budowane z węzłów przy odczycie.
- API przesyła treść jako dokument JSON (transport HTTP), opisany w OpenAPI 3.0 przez `anyOf`
  z `discriminator` (pole `type`), generowane przez `Microsoft.AspNetCore.OpenApi`.

### Spójność między agregatami

Archiwizacja materiału publikuje `MaterialArchivedV1` (outbox); `Knowledge.Worker` konsumuje
zdarzenie i usuwa ulubione wskazujące archiwizowany materiał. **Świadomy wyjątek** od zasady
„jedna transakcja modyfikuje jeden agregat”: usunięcie wszystkich `Favorite` danego elementu
odbywa się jedną operacją, bo nie narusza niezmienników żadnego z nich.

## Konsekwencje

- Zapytania o treść ładują bloki i fragmenty dwoma zapytaniami i składają drzewo w handlerze zapytania.
- Nowy typ bloku = nowa wartość typu, ewentualne kolumny i `CHECK` w migracji, walidacja w domenie.
