# Agresywne testy mutacyjne przepływu i diagnostyki

## Wynik

Znaleziono luki, przez które błąd jednego kursu lub renderowania nadal może
zakończyć cały proces albo pozbawić użytkownika diagnostyki. Najpilniejszy
przypadek nie wymaga wadliwych danych: poprawny wynik klastrowania bez skupisk
wywraca widok mapy. Mały zbiór po odrzuceniu błędnych kursów może właśnie dać
taki wynik (`min_samples = 5`).

Ten etap dodaje testy i dowody. Nie zmienia kodu produkcyjnego. Testy nazwane
`known gap` potwierdzają obecne awarie, a nie ich poprawną obsługę. Zielony wynik
tych testów nie oznacza, że wymienione niżej luki zostały naprawione. Po naprawie
należy zmienić je na asercje zachowania raportu i zachowania diagnostyki.

## Potwierdzone luki

| Priorytet | Minimalny bodziec | Co się dzieje | Dowód |
| --- | --- | --- | --- |
| Wysoki | Poprawna odpowiedź analytics: jeden punkt, `labels: [-1]`, `clusters: []` | `centroidLayer` przekazuje pustą sekwencję do `Chart.combine`. Renderowanie rzuca `ArgumentException`. | `known gap valid all noise response crashes because there are no centroids to combine` |
| Wysoki | Raport zawiera już błędy kursów i wynik klastrowania bez skupisk albo niespójną liczbę etykiet | `JobRegistry` renderuje HTML przed zapisaniem `RideErrors`. Wyjątek zamienia wynik w ogólny „błąd wewnętrzny”, a pobranie ZIP-a zwraca **404**, mimo wcześniej zebranych błędów. | Test HTTP/WebSocket `known gap render failure loses already captured diagnostics before publishing them` |
| Wysoki | Jeden poprawny wpis historii i kolejny: `null`, `{}`, `true`, `order_handle: null` lub błędny typ ID | Leniwe `Seq.map Json.deserializeNode` jest wymuszane przed pobieraniem szczegółów. `JsonException` wydostaje się z `scrapeRides`; poprawny kurs też nie jest pobierany. Brak wyniku zawierającego diagnostykę. | Pięć przypadków `known gap one malformed history entry throws away a valid history without diagnostics` |
| Średni | `stops[0].lat` lub `lng` równe `1e999` w JSON | Deserializator odczytuje `Infinity`; `parseOrder` zwraca `Finished`. Zapis żądania analytics do JSON rzuca `ArgumentException`. Cały raport kończy się błędem z pustym `RideErrors`. | Reprodukcja `coordinate` i audyt wszystkich pól |
| Średni | Kwota `79228162514264337593543950335 zł` wraz z inną dodatnią kwotą | Kwoty osobno pasują do `decimal`, lecz sumowanie przepełnia typ. Błąd jest już poza granicą pojedynczego kursu, więc wynik ma puste `RideErrors`. | Reprodukcje `money` i `known gap individually valid money values overflow only when histories are combined` |
| Średni | `accepted_timestamp: 253402300799` | Parser i konwersja strefy czasowej akceptują kurs. `eligibleHours` wykonuje `AddHours` poza zakresem daty i rzuca `ArgumentOutOfRangeException`. Błąd całego raportu nie wskazuje kursu. | Reprodukcja `timestamp` |
| Średni | Niepoprawny JSON całej odpowiedzi HTTP, pusty body, `null`, brak wymaganych pól koperty lub błędny `code` | Po trzech próbach pozostaje tylko `SkippedOrder` z ID i komunikatem. Oryginalny body i stack trace nie trafiają do ZIP-a. Jeśli wszystkie odpowiedzi są wadliwe, `scrapeRides` zwraca sam string błędu i nie ma danych do pobrania. | Pięć przypadków `known gap malformed HTTP body...` i przypadek wszystkich nieudanych odpowiedzi |
| Średni | Kolejne strony historii stale zwracają ten sam kursor | Brak wykrywania cyklu i brak przekazywania tokenu anulowania do pobierania historii. Test musiał zatrzymać pętlę odpowiedzią HTTP 429 przy czwartym żądaniu. Zebrane poprawne wpisy nie przechodzą dalej. | `known gap repeated cursor loops until the stub budget rejects another page` |
| Niższy | Liczba etykiet różna od liczby punktów albo etykieta/skupisko o ID `-2` | Odpowiedź przechodzi mapowanie; widok rzuca odpowiednio `ArgumentException` w `Array.zip` lub `IndexOutOfRangeException` przy indeksowaniu palety. | Dwa przypadki długości etykiet i przypadek ujemnego ID |

Pierwszy przypadek jest poprawnym wynikiem usługi. Pozostałych przypadków
nie należy przypisywać do zgłoszenia konkretnego użytkownika bez jego danych.
Kwoty graniczne i czasy krańcowe są syntetycznymi kontrprzykładami odporności.

## Audyt mutacji całego dokumentu

Mutator odwiedza każdy węzeł obu odpowiedzi kursu, włącznie z korzeniem,
obiektami, tablicami i ich elementami. Wykonuje usunięcie lub zastąpienie przez:
`null`, `true`, `0`, pusty string, pustą tablicę, pusty obiekt, `1e999`, skrajny
timestamp i tekst `Decimal.MaxValue`. Daje to **760 pojedynczych mutacji**.

Sam `parseOrder`:

| Wynik | Liczba |
| --- | ---: |
| `Error`, z zachowanym etapem, typem, komunikatem, stosami i oboma oryginalnymi JSON-ami | 669 |
| `Finished` | 89 |
| `DidNotHappen` | 2 |
| Wyjątek poza parserem | 0 |

Te same mutacje przetestowano w mieszaninie z jednym poprawnym kursem:
przygotowanie danych → zapis żądania analytics do JSON → obliczenia → widok → ZIP.
Usługa jest zastąpiona lokalną funkcją. Zwraca spójny, niepusty wynik skupisk,
aby osobno zbadany defekt pustych skupisk nie maskował innych problemów.

| Wynik | Liczba |
| --- | ---: |
| Wadliwy kurs wykluczony, poprawny kurs przeanalizowany, JSON diagnostyki zachowany | 669 |
| Mutacja zaakceptowana; raport i widok powstają | 83 |
| Cały raport przerwany, `RideErrors` puste | **6** |
| Nieuruchomione obliczenia o skrajnej liczbie godzin | 2 |

Sześć awarii to: maksymalny czas przyjęcia, nieskończona szerokość lub długość
geograficzna oraz maksymalna kwota w tekście płatności, pozycji przychodu lub
pozycji prowizji. Dodatkowa reprodukcja pokazuje, że dwie historie, które
przechodzą osobno, mogą przepełnić sumę po połączeniu.

Nie wykonano obliczeń dla `accepted_timestamp: 0` i
`ride_end: 253402300799`: przy bazowym kursie dawałyby wieloletnie okresy.
`eligibleHours` materializuje kolejne godziny bez ograniczenia. To potwierdzone
przyjęcie nieograniczonego obciążenia, nie pomiar rzeczywistego zawieszenia
procesu — długotrwałego obliczenia świadomie nie uruchamiano.

## Sekwencje mutacji i odtwarzanie

FsCheck wykonuje **15 000 dodatkowych scenariuszy**, po 5000 dla seedów
`101`, `2027`, `104729` (gamma `11400714819323198485`). Każdy scenariusz zawiera
od 1 do 6 mutacji, a kolejne ścieżki są losowane z aktualnego dokumentu.
Pozwala to mutować również dokument po usunięciu rodzica lub zastąpieniu
korzenia. Shrinker redukuje listę operacji. Nie wystąpił wyjątek poza parserem;
około 97% scenariuszy dało pełną diagnostykę, pozostałe zachowały kategorię kursu.

To potwierdza odporność obecnego `parseOrder` na te mutacje. Nie dowodzi
odporności granic HTTP, historii, obliczeń i widoku — wymienione luki odtworzono
osobnymi testami pełniejszego przepływu.

Pliki:

- `tests/TestData/ApiMutations.fs` — deterministyczny mutator dokumentów.
- `tests/Bolt.ETL.Tests/ApiMutation.Tests.fs` — audyt parsera i sekwencje FsCheck.
- `tests/Bolt.Web.Tests/ReportMutation.Tests.fs` — obliczenia, widoki i audyt pełnego raportu.
- `tests/Bolt.Scraper.Tests/ScrapePipeline.Tests.fs` — historia, koperty HTTP i paginacja.
- `tests/Bolt.Web.Tests/WebSocket.Tests.fs` — dowód utraty już zebranej diagnostyki z HTTP 404.

Wyniki audytu są wypisywane do wyjścia testów; można je zachować przez
`dotnet test Bolt.slnx --logger trx --results-directory /tmp/bolt-audit`.
Testy nie łączą się z prawdziwym API Bolta ani analytics i nie wysyłają maili.

Weryfikacja końcowa: `dotnet test Bolt.slnx --no-restore` i
`dotnet test Bolt.slnx -c Release --no-restore` — **332/332** (ETL 195,
Scraper 44, Web 65, Infrastructure 10, Reporter 18), bez pominiętych testów.
Dodano 27 przypadków do poprzednich 305. Asercje awarii sprawdzają mechanizm
bez zakładania obecności konkretnej ramki stosu, którą optymalizator może usunąć.
`git diff --check` bez błędów; brak zmian w `src/`.

## Kolejność napraw wynikająca z dowodów

1. Obsłużyć poprawny brak skupisk i zachować diagnostykę niezależnie od sukcesu renderowania.
2. Izolować wadliwe wpisy historii oraz zachowywać odpowiedź i wyjątek także przed typowaniem danych kursu.
3. Weryfikować współrzędne i bezpieczeństwo obliczeń; obsłużyć również przepełnienia agregatów wielu poprawnych kursów.
4. Ograniczyć lub zmienić enumerację godzin oraz wykrywać cykle paginacji z działającym anulowaniem.

Nie należy naprawiać nieskończonych współrzędnych przez zezwolenie na `Infinity`
w JSON ani przepełnień przez zastępowanie kwot zerami — utraciłoby to wiarygodność wyników.
