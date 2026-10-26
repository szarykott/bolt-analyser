# Pomijanie wadliwych kursów i diagnostyka

## Zachowanie

Scraper przechowuje surową parę odpowiedzi API dla każdego pobranego kursu.
Deserializacja i konwersja odbywają się osobno dla każdego kursu w
`RideFactory.parseOrder`. Wynik ma trzy kategorie: `Finished`, `DidNotHappen`
oraz `Error`. Do analiz trafia wyłącznie pierwsza kategoria.

Wyjątek deserializacji lub konwersji zapisuje identyfikator kursu, etap,
typ wyjątku, komunikat, pełny stos oraz obie oryginalne odpowiedzi tego kursu.
Nie zapisuje całej historii konta ani tokenów. Brak odpowiedzi `data` i jawne
`data: null` są traktowane jako błędny kurs. Puste przystanki oraz niefinitywny
dystans są odrzucane przed przygotowaniem klastrowania.

Raport pokazuje liczbę przeanalizowanych, niezakończonych i błędnych kursów.
Przy błędach informuje, że analiza uwzględnia mniejszą liczbę kursów. Jeśli nie
ma żadnego poprawnego zakończonego kursu, wyświetla błąd analizy wraz z diagnostyką.
Dotychczasowe pominięcia po błędach HTTP pozostają osobno opisane w raporcie.

## Pliki diagnostyczne

`GET /diagnostics.zip` zwraca ZIP tylko dla bieżącej sesji identyfikowanej
istniejącym cookie `bolt-analysis-session`. Brak sesji lub błędnych kursów
daje 404. Odpowiedź ma `Cache-Control: no-store`.

Archiwum zawiera `README.txt` i plik `ride-<numer>-<id>.json` dla każdego błędu.
Każdy JSON ma pola:

```text
order_id, stage, exception_type, message, stack_trace,
previous_order, past_order_detail
```

`stage` wskazuje `previous_order`, `past_order_detail` albo `ride`. Odpowiedzi
API są zagnieżdżonymi wartościami JSON, bez serializowania ich do stringów.
Zachowane są oryginalne wartości i pola; formatowanie JSON przy eksporcie może
się zmienić. Jest to cały pojedynczy kurs, nie automatycznie zredukowany fragment
wyłącznie z błędnym polem: zachowuje kontekst potrzebny do reprodukcji.

Podgląd w stronie koduje treść jako tekst. ZIP powstaje na żądanie w pamięci.
Rozpoczęcie kolejnej analizy usuwa poprzednią diagnostykę; zamknięcie strony
powoduje usunięcie sesji po dotychczasowym okresie tolerancji (3 minuty).
W produkcji nowa funkcja nie dodaje zapisu diagnostyki na dysk.
Odczyt lokalnego cache korzysta z tych samych surowych plików, bez migracji ich formatu.

## Adres do zgłoszeń

Docelowy adres nie został jeszcze podany. Po ustawieniu `Diagnostics:SupportEmail`
w konfiguracji aplikacji lub `Diagnostics__SupportEmail` w środowisku pojawi się
adres z linkiem `mailto:`; adres zostanie też dodany do instrukcji wewnątrz ZIP-a.
Wiadomość i załącznik wysyła użytkownik samodzielnie. Bez ustawienia adresu strona
prosi o przekazanie pliku autorowi aplikacji.

## Weryfikacja

Testy obejmują mieszankę kursów poprawnych, anulowanych i błędnych, awarie
obu deserializatorów, brakujące pola, `null`, puste przystanki i wadliwy dystans.
Sprawdzają sumy wyłącznie poprawnych kursów i zachowanie diagnostyki także po
awarii usługi analitycznej. Testy FsCheck sprawdzają dodatkowo bezpieczną
klasyfikację dla trzech zapisanych seedów po 1000 przypadków.

Testy HTTP/WebSocket otwierają i odczytują ZIP, sprawdzają izolację sesji,
zachowanie po ponownym połączeniu, usunięcie po nowej analizie i wygaśnięciu
oraz dostępność diagnostyki przy braku poprawnych kursów. Podgląd i link `mailto:`
mają osobne testy widoków. Nie ma połączeń z prawdziwym API Bolta ani wysyłania maili.

Niskopoziomowe testy charakterystyki nadal dokumentują zachowanie dawnych
funkcji konwersji. Aktywna ścieżka raportu korzysta z bezpiecznej granicy
`parseOrder`; dotychczasowych parserów kwot nie zastąpiono nowym kontraktem formatu.

Wyniki: `dotnet test Bolt.slnx --no-restore` oraz
`dotnet test Bolt.slnx -c Release --no-restore` — **305/305**, bez pominiętych testów.
Sprawdzono również wygląd widoku z diagnostyką w Firefox przy szerokościach
390 i 1440 pikseli. `git diff --check` bez błędów.
