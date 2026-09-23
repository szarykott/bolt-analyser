# 🚕 Bolt ride analyser

An open source, non-profit project made by a Bolt driver for other drivers. It turns a driver's ride history into a readable report of trips, earnings and working time. The goal is to make it easier to understand where and when driving pays off. This is an independent project, not an official Bolt product.

The web app asks for the email address associated with a Bolt driver account and a sign-in link sent by Bolt. It fetches the driver's trip data and shows:

- 📊 Earnings, commission and notable rides.
- 🕒 Hourly earnings split by day/night and weekday/weekend.
- 🗺️ A map of pickup clusters.

The report can be downloaded as an HTML file. Signing in this way may log you out of the Bolt app. In the production build, account data is used to generate the report and is not stored on the server.

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

## Hosting na Mikrusie z darmową subdomeną

Na VPS skopiuj `compose.mikrus.yaml` i uruchom opublikowane obrazy:

```sh
docker compose -f compose.mikrus.yaml up -d
curl -g -6 'http://[::1]:8080/health'
```

Odpowiedź z `/health` powinna mieć `"status":"ok"` i `"analytics":true`. Następnie w panelu Mikrusa dodaj darmową subdomenę wskazującą na **ten VPS, port 8080, protokół HTTP**. Możesz też użyć polecenia `domena 8080` na VPS, aby otrzymać losową subdomenę. Mikrus zapewnia HTTPS i certyfikat; aplikacja nadal nasłuchuje po HTTP. Nie uruchamiaj przy tym `compose.release.yaml`, który jest przeznaczony dla własnej domeny i uruchamia Nginx oraz Certbota.

Jeżeli port 8080 jest zajęty, ustaw inny, np. `BOLT_PORT=8081 docker compose -f compose.mikrus.yaml up -d`, i wpisz ten sam port w Mikrusie. Port musi być dostępny po IPv6. Tylko aplikacja WWW jest dostępna publicznie; usługa analityczna pozostaje wewnątrz sieci Compose.

Workflow wydania w jobie `prepare_deployment` wstawia tag wydania w miejsce `BOLT_VERSION` i dołącza `compose.mikrus.yaml` do GitHub Release; job `deploy_application` wdraża przygotowany plik na VPS. Dawny ZIP wdrożeniowy jest usuwany z Release. Aby wrócić do wcześniejszej wersji lub wdrożyć inną, w GitHub Actions wybierz workflow **Publish and deploy application → Run workflow** i wpisz opublikowany tag obrazu w polu `version`. Ręczne uruchomienie przygotuje YAML i wdroży wskazane obrazy bez ponownego budowania. Na VPS obrazy są pobierane przed `docker compose down`, a następnie aplikacja startuje przez `docker compose up -d`. Ustaw w repozytorium GitHub Variable `MIKRUS_SSH_PORT` oraz GitHub Secrets `MIKRUS_HOST` (nazwa serwera lub adres SSH) i `MIKRUS_SSH_PASSWORD`.
