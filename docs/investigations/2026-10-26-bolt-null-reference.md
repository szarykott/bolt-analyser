# Badanie wyjątków po pobraniu danych Bolta

## Wynik i granice diagnozy

Odtworzono sześć niezależnych wejść prowadzących do `NullReferenceException`
w aktywnej ścieżce tworzenia raportu: brak płatności, zarobków, dystansu,
czasu przyjęcia, rozpoczęcia lub zakończenia kursu oznaczonego jako `finished`.
Brak pola i jawne `null` mają ten sam skutek. Deserializacja kończy się sukcesem,
a wyjątek powstaje podczas konwersji do modelu analizy.

To mechanizmy zgodne z opisanym momentem awarii. Bez stack trace lub odpowiedzi
API z rzeczywistego zgłoszenia nie można wskazać, który wystąpił u użytkownika.
Nie zmieniono zachowania produkcyjnego. Nowe testy dokumentują również błędy;
ich zielony wynik nie oznacza, że aplikacja została naprawiona.

## Przepływ i granice typów

`LowLevelApi.send` → `BoltClient` → `fetchOrderDetails` → deserializacja w
`scrapeRides` → `RideFactory.getFinishedRides` → przygotowanie klastrowania →
serwis analytics → statystyki i raport.

- Odpowiedzi szczegółów kursu są początkowo surowymi `JsonElement`.
  Zakończenie pobierania nie dowodzi poprawności typowanych danych ani ich
  przydatności do analizy. Deserializacja odbywa się dopiero po zebraniu odpowiedzi.
- Konfiguracja `FSharp.SystemTextJson` odrzuca brak, `null` i błędny typ
  wymaganych pól rekordu. Dotyczy to też tablic, tekstu oraz wartości `Money`
  w rekordach. Elementy `null` zamiast rekordów w kolekcjach są odrzucane.
- Dla pól `option` brak i `null` stają się `None`. Typy nie wyrażają zależności
  „jeśli stan to finished, płatność i czasy muszą istnieć”. Kod wymusza `.Value`.
  W tych wywołaniach `.Value` na `None` daje `NullReferenceException`.
- Typ listy lub tablicy nie gwarantuje wymaganej długości, poprawnej kolejności
  ani znaczenia elementów. `Distance` nie gwarantuje dodatniej, skończonej wartości.
- Bezpośrednia deserializacja `Money`, `Tip` lub `Distance` z JSON `null` daje NRE;
  deserializacja tablicy z JSON `null` zwraca rzeczywisty `null`. Są to luki
  generycznych funkcji, ale obecne wymagane pola rekordów blokują takie wejścia.
  Nie należy utożsamiać tego z potwierdzonym przejściem `null` przez modele API.

## Potwierdzone awarie i przekłamania

Wszystkie mutacje poniżej odnoszą się do poprawnych, syntetycznych odpowiedzi
w [BoltApiFixture.fs](../../tests/TestData/BoltApiFixture.fs). Nie zawierają danych kierowców.

| Minimalna zmiana wejścia | Etap i wynik | Miejsce |
| --- | --- | --- |
| Usunięcie `payment` lub `payment: null` | Konwersja: NRE | `RideFactory.fs:33` |
| Usunięcie `earnings` lub `earnings: null` | Konwersja: NRE | `RideFactory.fs:37` |
| Usunięcie/null `ride_distance_str` | Konwersja: NRE | `RideFactory.fs:47` |
| Usunięcie/null `accepted_timestamp`, `ride_start` lub `ride_end` | Konwersja: NRE, osobno dla każdego pola | `RideFactory.fs:52–54` |
| `earnings.accordion_items` o długości 0 lub 1 | Konwersja: `InvalidOperationException` | `RideFactory.fs:19` |
| `stops: []` | Konwersja udana, klastrowanie: `IndexOutOfRangeException` | `RideClustering.fs:44` |
| Tekst kwoty `""`, napiwek `"Napiwek"`, dystans `"1"` | Deserializacja: `ArgumentOutOfRangeException` z `Substring` | `Serialization.fs:45,60,71` |
| `payment.items[0].trailing_component.text: ""` | Deserializacja udana, konwersja: `ArgumentOutOfRangeException` | `RideFactory.fs:14` |
| Kwota `"20.00 zł"`, `"x zł"` | `FormatException`; etap zależy od pola | Konwertery lub `RideFactory.toMoney` |
| Kwota większa niż `Decimal.MaxValue` | `OverflowException` | Konwerter lub `RideFactory.toMoney` |
| Data `"2026.02.30 12:00"`, timestamp `253402300800` | Odpowiednio `FormatException`, `ArgumentOutOfRangeException` | Konwertery dat |
| Kwota `"20,00xyz"` | Przyjęta jako 20 PLN; sufiks nie jest sprawdzany | Oba parsery kwot |
| Napiwek `"XXXXXXXX5,00xyz"` | Przyjęty jako 5 PLN; prefiks i sufiks nie są sprawdzane | `TipConverter` |
| Dystans `"2.5mi"` | Przyjęty jako 2,5 km | `DistanceConverter` |
| Dystans `"2,5km"` | Przyjęty jako **25 km**, przecinek działa jak separator tysięcy | `DistanceConverter` |
| `"NaNkm"`, `"Infinitykm"`, `"1e999km"` | Wartości niefinitywne trafiają do modelu i sumy dystansu | Konwerter → statystyki |
| Zamiana kolejności dwóch elementów zarobków | W przykładzie zarobek rośnie z 16 do 20 PLN, prowizja znika | `RideFactory.toEarnings` |
| Puste pozycje płatności i przychodu | Przyjęte niepełne sumy: 0 PLN zapłaty i −4 PLN zarobku | Konwersja → statystyki |
| Nieznany stan `"finished_v2"` | Kurs zostaje pominięty | `OrderState.isFinished` |
| Poprawne, lecz odwrócone graniczne czasy | Przyjęty kurs z końcem przed przyjęciem | Konwersja |
| Tablice zamówień i szczegółów o różnych długościach | `ArgumentException`; zabezpieczenie wyłącznie przez `Array.zip` | `RideFactory.fs:78` |
| Jeden niekompletny kurs w poprawnej historii | NRE przerywa przetwarzanie całej historii | `RideFactory.getFinishedRides` |
| Odpowiedź klastrowania z jednym punktem i pustymi etykietami | Mapowanie akceptuje niespójne długości | `RideClustering.fromAnalyticsResponse` |

W aktywnym raporcie nie ma parsowania kwot regexem. Znaleziony regex znajduje się
w `Tokens.fs:72` i obsługuje link logowania, przed pobieraniem danych. Tam również
istnieją niechronione operacje na stringu i indeksowanie par zapytania; nie są
wyjaśnieniem awarii po scrapingu i nie były rozszerzane w tym badaniu.

Poza aktywną ścieżką raportu potwierdzono też NRE dla pustego zbioru pogody
w `PerRide1`, `PerRide2` i `PerHour`: `Weather.getNearestDataPoint` zwraca `None`,
a przygotowanie danych wymusza `.Value`. Dystans `0km` przechodzi konwersję,
lecz `PerRide1` dzieli przez zero podczas liczenia ceny za kilometr.

## Dowód od pobrania do komunikatu

Testy z atrapą `HttpMessageHandler` wykonują rzeczywiste `scrapeRides`:

- Brak `payment` i pusty tekst płatności kończą scraping wynikiem `Ok`, bez
  pominiętych kursów i bez ponawiania żądań. Następne testy odtwarzają awarie
  konwersji takich samych odpowiedzi.
- Niepoprawny tekst dystansu lub brak wymaganych przystanków daje błąd dopiero
  po zdarzeniu `ScrapingOrderDetails(1,1)`. Ten etap nie jest ponawiany;
  jeden wadliwy rekord kończy cały scraping błędem parsowania.
- `data: null` w odpowiedzi szczegółów jest odrzucane już na granicy koperty;
  następują trzy próby i pominięcie kursu. Brak/null listy historii kończy się
  błędem deserializacji przed pobieraniem szczegółów. Pusta historia jest poprawna.
- `Report.run` dla każdego z sześciu brakujących pól zwraca tylko
  `Analiza nie powiodła się: Object reference not set to an instance of an object.`
  Testy te nie wykonują połączenia z analytics; wyjątek powstaje wcześniej.

Przykład minimalnej reprodukcji w kontekście testów:

```fsharp
let previous, detail =
    deserialize
        (previousOrder ())
        (pastOrderDetail () |> mutate "payment" None)

RideFactory.getRide previous detail
```

Istotny fragment uzyskanego stosu:

```text
System.NullReferenceException
  at Bolt.ETL.RideFactory.getPayment(...) in RideFactory.fs:line 33
  at Bolt.ETL.RideFactory.getRide(...) in RideFactory.fs:line 59
```

## Testy właściwości i odtwarzalność

Dodano `FsCheck.Xunit` 3.6.0 wyłącznie w projekcie testowym ETL.
Pięć rodzin właściwości wykonuje po 1000 przypadków dla każdego seeda
`101`, `2027`, `104729` (15 000 przypadków):

1. Round trip pieniędzy, napiwków i dystansu w precyzji formatu API.
2. Poprawne generowane odpowiedzi: zachowanie sum, dystansu i punktu odbioru.
3. Kombinacje braków/null pól opcjonalnych i stanów kursu.
4. Kształty kolekcji: przystanki, akordeon zarobków i elementy `null`.
5. Fuzzing tekstów: pole, etap oraz klasa wyjątku lub akceptacja wartości.

Dodatkowa właściwość FsCheck.Xunit wykonuje 1000 przypadków równoważności
braku pola i jawnego `null`. Generatory kombinacji braków i tekstów mają
shrinking; ten dodatkowy test korzysta ze standardowego shrinkingu list.

W eksploracyjnym przebiegu właściwość „konwersja nie rzuca wyjątków” została
sfalsyfikowana dla wszystkich trzech seedów. Redukcja listy usuniętych pól dała:

| Seed (`Rnd`, gamma = `11400714819323198485`) | Pierwotne indeksy pól | Redukcje | Minimalny kontrprzykład |
| --- | --- | --- | --- |
| 101 | `[4;4;0;0;4;4]` | 5 | `[4]`: usunięte `payment`, NRE w linii 33 |
| 2027 | `[0;5]` | 1 | `[5]`: usunięte `earnings`, NRE w linii 37 |
| 104729 | `[2;1;0;3;4;4]` | 5 | `[4]`: usunięte `payment`, NRE w linii 33 |

Indeksy: 0 = przyjęcie, 1 = start, 2 = koniec, 3 = dystans, 4 = płatność,
5 = zarobki. W każdym przebiegu falsyfikacja nastąpiła przy pierwszym przypadku.
Generator losował długość przez `Gen.choose(0, 6)`, następnie indeksy przez
`Gen.listOfLength count (Gen.choose(0, 5))`; shrinker zwracał `List.tail` niepustej
listy. Właściwość usuwała wskazane pola z poprawnego kursu `finished`, wykonywała
deserializację i `RideFactory.getRide`, oczekując braku wyjątku. Konfiguracja:
`Config.QuickThrowOnFailure.WithMaxTest(1000).WithReplay(Some { Rnd = Rnd(seed); Size = None })`.
Minimalne kontrprzykłady utrwalono w deterministycznych testach charakterystyki.
Eksploracyjnego, celowo fałszywego wymagania nie pozostawiono jako czerwonego testu CI.

Pliki testów:

- [ApiInput.Tests.fs](../../tests/Bolt.ETL.Tests/ApiInput.Tests.fs): macierz mutacji, typy wyjątków i ciche przekłamania.
- [ApiInputProperties.Tests.fs](../../tests/Bolt.ETL.Tests/ApiInputProperties.Tests.fs): generatory i zapisane seedy.
- [ScrapePipeline.Tests.fs](../../tests/Bolt.Scraper.Tests/ScrapePipeline.Tests.fs): atrapy pełnego pobierania.
- [Report.Tests.fs](../../tests/Bolt.Web.Tests/Report.Tests.fs): przejście od poprawnie zdeserializowanych danych do błędu analizy.

Nie uruchamiano prawdziwego API Bolta ani usługi Python. Mapowanie odpowiedzi
klastrowania testowano lokalnie; nie jest to test jej rzeczywistego kontraktu HTTP.

Weryfikacja końcowa: `dotnet test Bolt.slnx` — **271/271**, bez pominiętych testów
(ETL 170, Scraper 31, Web 42, Infrastructure 10, Reporter 18). Punkt odniesienia
przed zmianami: 128/128; dodano 143 przypadki testowe, w tym przebiegi właściwości
wykonujące łącznie 16 000 wygenerowanych scenariuszy. `git diff --check` bez błędów.

## Proponowane następne naprawy

1. Najpierw zweryfikować wymagania kursu `finished` przy konwersji i zwracać błąd
   wskazujący identyfikator kursu oraz brakujące pole. Nie zastępować braku kwoty,
   dystansu ani czasu zerem. Wybór między pominięciem kursu a odrzuceniem analizy
   wymaga ustalenia polityki produktu.
2. Parsować teksty po sprawdzeniu prefiksu, jednostki, długości i separatorów;
   jawnie odrzucać niefinitywne dystanse. Ujednolicić oba parsery kwot przy naprawie.
3. Zweryfikować kształt zarobków i dostępność przystanku przed analizą.
   Ustalić kontrakt pozycji zarobków przed zastąpieniem zależności od kolejności.
4. Zachować typ i pełny stos wyjątku w diagnostyce serwera, bez logowania surowych
   danych kierowcy; użytkownikowi zwracać komunikat wskazujący etap i brak danych.

Po wdrożeniu napraw odpowiednie testy charakterystyki trzeba zmienić na asercje
nowego, uzgodnionego zachowania, zachowując te same minimalne wejścia.
