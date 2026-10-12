# Transit Games

A mobile app (.NET MAUI, Android + iOS) for real-world public-transport games inspired by the "Jet Lag: The Game" series.
Two game modes are included: **Hide and Seek** and **Tag**. Games are modules, so a new one can be added without touching
the existing ones. The UI is Polish by default with English translations (`*.en.resx`). The detailed documentation below is in Polish.

## Features
- Map with game area and stations (OpenStreetMap data via Overpass)
- **Hide and Seek**: hiders and seekers with a location-sharing policy (hiders see seekers, never the reverse)
- **Tag**: one runner heads for a goal while chasers pursue; coins pay for transport, cards add challenges and curses
- Online mode: game server (ASP.NET Core + SignalR + SQLite) in Docker, with an offline message queue on the client
- GPS track recording with GPX / GeoJSON export
- Pluggable game modules (`GameModule` / `GameUiModule`)

## Stack
.NET 10, .NET MAUI, ASP.NET Core, SignalR, SQLite, NetTopologySuite, xUnit, Docker

## How to run
Requirements: .NET SDK 10 (see `global.json`), MAUI workloads (`dotnet workload install maui-android maui-ios`), Android SDK (API 36). Building for iOS needs macOS.

```bash
# libraries and tests (no MAUI needed)
dotnet test TransitGames.Libraries.slnf

# build and run the Android app
dotnet build src/TransitGames.App/TransitGames.App.csproj -f net10.0-android -t:Run

# online game server
cd deploy && docker compose up -d --build
```

---

## Dokumentacja (po polsku)


Aplikacja mobilna (.NET MAUI, Android + iOS) do terenowych gier komunikacyjnych inspirowanych serią
„Jet Lag: The Game”. Na start: **Chowany (Hide and Seek)** i **Berek (Tag)**. Gry są modułami —
dodanie kolejnej nie wymaga zmian w istniejących grach.

## Struktura

```
TransitGames.sln
TransitGames.Libraries.slnf          # filtr: tylko biblioteki bez MAUI + testy (szybki build/CI na Linuksie)
src/
  TransitGames.Core/                 # kontrakty gier, rejestr, sesja, zdarzenia, timery, SQLite, sync
  TransitGames.Geo/                  # NTS, projekcja metryczna, GeoJSON, Overpass, stacje, granice OSM
  TransitGames.Tracking/             # zapis tras GPS (filtr, SQLite), eksport GPX/GeoJSON, odtwarzanie
  TransitGames.Sync/                 # klient trybu online: REST + SignalR, kolejka wiadomości offline
  TransitGames.Server/               # serwer gry online (ASP.NET Core + SignalR + SQLite, Docker)
  TransitGames.Games.HideAndSeek/    # logika gry (net10.0, bez MAUI)
  TransitGames.Games.Tag/            # logika gry (net10.0, bez MAUI)
  TransitGames.UI/                   # wspólne UI (MAUI): IGameUiModule, motyw, kontrolki, usługi platformowe
  TransitGames.Games.HideAndSeek.Maui/  # strony i ViewModele gry
  TransitGames.Games.Tag.Maui/          # strony i ViewModele gry
  TransitGames.App/                  # aplikacja: Shell, ekrany wspólne, DI
tests/                               # xUnit dla Core, Geo, Tracking, Sync (serwer w pamięci) i logiki gier
deploy/docker-compose.yml            # uruchomienie serwera w Dockerze
docs/                                # decyzje, postęp, jak dodać grę
tools/resx.py                        # dopisywanie kluczy do par resx pl/en
```

## Wymagania

- .NET SDK 10.0.1xx+ (`global.json`), workloady: `dotnet workload install maui-android maui-ios`
- Android SDK (API 36) — emulator lub urządzenie
- iOS: build wymaga macOS (lokalnie lub w CI). Na Windows iOS jest domyślnie wyłączony;
  można go wymusić `-p:EnableIos=true` (wymaga sparowanego Maca).

## Uruchomienie

```bash
dotnet test TransitGames.Libraries.slnf
```

```bash
dotnet build src/TransitGames.App/TransitGames.App.csproj -f net10.0-android
```

```bash
dotnet build src/TransitGames.App/TransitGames.App.csproj -f net10.0-android -t:Run
```

## Uprawnienia

| Platforma | Uprawnienie | Po co |
|-----------|-------------|-------|
| Android | `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION` | pozycja w grze, pytania |
| Android | `ACCESS_BACKGROUND_LOCATION`, `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_LOCATION`, `POST_NOTIFICATIONS` | nagrywanie trasy przy wygaszonym ekranie (etap 3) |
| Android | `POST_NOTIFICATIONS`, `USE_EXACT_ALARM` (13+), `SCHEDULE_EXACT_ALARM` (12) | powiadomienia z gry o czasie (koniec ukrycia, przewagi, pingi) |
| Android | `INTERNET`, `ACCESS_NETWORK_STATE` | kafelki OSM, Overpass, serwer gry |
| Android | `usesCleartextTraffic` | serwer gry przez VPN pod adresem http |
| iOS | `NSLocationWhenInUseUsageDescription`, `NSLocationAlwaysAndWhenInUseUsageDescription`, `UIBackgroundModes: location` | jw. |
| iOS | `NSAppTransportSecurity/NSAllowsArbitraryLoads` | serwer gry przez VPN pod adresem http |

## Berek (Tag) — zasady

Gra tylko online (każdy gracz ma własny telefon). Jeden gracz **ucieka** do swojego celu, pozostali **gonią**.

- **Przygotowanie:** host zakłada grę, pozostali dołączają kodem (wszyscy jako goniący). Każdy ma **własny cel** —
  wybiera go sam na mapie, ustawia host albo cele losują się ze stacji przy starcie (z min./maks. odległością od
  startu i min. odstępem). Host wybiera **punkt startowy** (GPS lub mapa) i naciska Start: aplikacja **losuje
  uciekiniera**, każdy dostaje **X monet** na start.
- **Tura:** uciekinier rusza od razu, goniący czekają **przewagę** (domyślnie 30 min; odliczanie, pościg
  zablokowany). Pozycja uciekiniera trafia do goniących w pingach co ustawiony czas; goniących nie widzi nikt.
- **Monety:** uciekinier **płaci za przejazdy** (goniący i piesi — nie). Model opłat: stawka za km z trasy GPS
  (pierwszy km z góry jako zaliczka, reszta po przejeździe; saldo może zejść poniżej zera), stała opłata za przejazd
  albo kwota wpisywana ręcznie. Bez monet na zaliczkę nie da się zacząć płatnego przejazdu. Każda zmiana salda to
  wpis w historii (start, bonus tury, przejazd, wyzwanie, klątwa, korekta hosta z powodem).
- **Karty:** uciekinier losuje z talii (JSON). **Wyzwanie** daje monety po wykonaniu (jedno naraz; rezygnacja lub
  przekroczony limit czasu = przerwa w losowaniu). **Klątwa** daje monety od razu i nakłada utrudnienie na czas
  `untilEndOfTurn` / `wholeGame` / `minutes:N`; zakaz środka transportu, mnożnik opłat i zakaz losowania aplikacja
  egzekwuje, pozostałe utrudnienia są honorowe. Format talii: `src/TransitGames.Games.Tag/Cards/cards.json`.
- **Złapanie:** goniący zgłasza „Złapałem!”, uciekinier potwierdza „Złapany” i wybiera, kto złapał (host może
  wymusić złapanie, gdy telefon uciekiniera milczy). **Nowy uciekinier** to losowy goniący albo ten, kto złapał
  (ustawienie); tura startuje z miejsca złapania, goniący znów czekają przewagę. Monety zostają, nowy uciekinier
  dostaje **bonus tury** (stały albo dopełnienie do minimum).
- **Koniec:** uciekinier, który dotrze do swojego celu (promień, domyślnie 100 m, + potwierdzenie), wygrywa. Po
  upływie czasu gry wygrywa gracz, którego cel jest **najbliżej obecnego uciekiniera** (linia prosta; remis
  możliwy); pozycję wysyła telefon uciekiniera, a gdy milczy — host podaje ostatni ping albo punkt na mapie.
- **Wyniki:** zwycięzca, ranking odległości celów, historia tur (kto uciekał, jak długo, wydane i zarobione
  monety, kto złapał), salda. Eksport trasy zawiera punkty zdarzeń (start, tury, złapania, przejazdy, karty,
  dotarcie, koniec) i odcinki oznaczone rolą.
- Gry Tag zapisane przed przebudową zasad otwierają się w historii tylko do odczytu (ze starym wynikiem drużyn).

## Gra online

Serwer działa w kontenerze Docker na prywatnym serwerze, telefony łączą się z nim przez VPN:

```bash
cd deploy && docker compose up -d --build
```

W aplikacji: Ustawienia → Gra online → adres serwera. Szczegóły: [docs/SERVER.md](docs/SERVER.md).

## Jak dodać nową grę

Skrót (szczegóły: [docs/ADDING_A_GAME.md](docs/ADDING_A_GAME.md)):

1. Projekt `TransitGames.Games.X` (net10.0): `XModule : GameModule<XSettings, XSession>`.
2. Projekt `TransitGames.Games.X.Maui` (MAUI lib): `XUiModule : GameUiModule<XModule>` + strony.
3. W `MauiProgram`: `services.AddGame<XUiModule>();`

## Lokalizacja

UI domyślnie po polsku (zasoby neutralne), angielski w plikach `*.en.resx`. Klasy zasobów są generowane
przy buildzie (`Directory.Build.targets`), bez plików Designer. Język można wymusić w Ustawieniach.

## Dokumentacja

- [docs/USER_GUIDE.md](docs/USER_GUIDE.md) — instrukcja dla graczy
- [docs/PROGRESS.md](docs/PROGRESS.md) — stan prac po każdym etapie
- [docs/DECISIONS.md](docs/DECISIONS.md) — decyzje i uproszczenia
- [docs/ADDING_A_GAME.md](docs/ADDING_A_GAME.md) — dodawanie gier
- [docs/SERVER.md](docs/SERVER.md) — serwer gry online (Docker, VPN, protokół)
