# 🚕 Bolt ride analyser

An open source, non-profit project made by a Bolt driver for other drivers. It turns a driver's ride history into a readable report of trips, earnings and working time. The goal is to make it easier to understand where and when driving pays off. This is an independent project, not an official Bolt product.

The web app asks for the email address associated with a Bolt driver account and a sign-in link sent by Bolt. It fetches the driver's trip data and shows:

- 📊 Earnings, commission and notable rides.
- 🕒 Hourly earnings split by day/night and weekday/weekend.
- 🗺️ A map of pickup clusters.

The report can be downloaded as an HTML file. Signing in this way may log you out of the Bolt app. In the production build, account data is used to generate the report and is not stored on the server.

Trips with invalid data are skipped individually. The report shows how many were excluded and offers a preview and a ZIP containing their original JSON responses and exception details. The ZIP remains available in the current browser session, including when no trip can be analysed; download it before closing the page.

Diagnostics also include malformed history entries and HTTP responses, even when the trip ID is unavailable. The ZIP keeps the original response text, endpoint and exception stack trace; unreadable JSON is stored as text inside a valid JSON file.

The optional `Diagnostics:SupportEmail` setting (environment variable `Diagnostics__SupportEmail`) adds a contact address and a `mailto:` link to the diagnostic notice and includes that address in the ZIP instructions. Diagnostic files are sent only if the user chooses to send them.

## 📁 What's in this repository

- `src/Bolt.Web` — the browser flow, progress updates and report UI.
- `src/Bolt.Scraper` — Bolt account authentication and trip retrieval.
- `src/Bolt.ETL` — trip processing and report calculations.
- `src/Bolt.Models` — shared data models.
- `src/Bolt.Infrastructure` — storage and repository code used by development workflows.
- `src/Bolt.Reporter` — reporting utilities.
- `python-analytics` — a FastAPI analytics service used for pickup clustering; it also contains standalone statistical tools.
- `tests` — .NET tests for the F# projects; `python-analytics/tests` contains Python tests.

The web application uses .NET 10 and F#; the analytics service uses Python. `compose.yaml` runs both services together.

## 🚀 Jak uruchomić aplikację na własnym komputerze

1. Zainstaluj i uruchom [Docker Desktop](https://www.docker.com/products/docker-desktop/). To program, który uruchomi obie części aplikacji bez osobnego instalowania .NET i Pythona.
2. Pobierz kod: na stronie tego repozytorium kliknij **Code → Download ZIP**, a potem rozpakuj archiwum.
3. Otwórz terminal w rozpakowanym folderze, w którym znajduje się plik `compose.yaml`, i wpisz:

   ```sh
   docker compose up --build
   ```

4. Poczekaj, aż aplikacja się uruchomi (przy pierwszym uruchomieniu może to potrwać kilka minut), a następnie otwórz **http://localhost:8080** w przeglądarce.
5. Przeczytaj informację o dostępie do konta, podaj adres e-mail konta kierowcy Bolt i wklej link do logowania z otrzymanej wiadomości. Aplikacja przygotuje raport, który można pobrać przyciskiem **Pobierz raport**. Logowanie może wylogować Cię z aplikacji Bolt.
6. Aby zakończyć, wróć do terminala, naciśnij **Ctrl+C**, a następnie wpisz `docker compose down`.
