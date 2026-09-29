# Transit Games

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
| Android | `INTERNET`, `ACCESS_NETWORK_STATE` | kafelki OSM, Overpass, serwer gry |
| Android | `usesCleartextTraffic` | serwer gry przez VPN pod adresem http |
| iOS | `NSLocationWhenInUseUsageDescription`, `NSLocationAlwaysAndWhenInUseUsageDescription`, `UIBackgroundModes: location` | jw. |
| iOS | `NSAppTransportSecurity/NSAllowsArbitraryLoads` | serwer gry przez VPN pod adresem http |

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

- [docs/PROGRESS.md](docs/PROGRESS.md) — stan prac po każdym etapie
- [docs/DECISIONS.md](docs/DECISIONS.md) — decyzje i uproszczenia
- [docs/ADDING_A_GAME.md](docs/ADDING_A_GAME.md) — dodawanie gier
- [docs/SERVER.md](docs/SERVER.md) — serwer gry online (Docker, VPN, protokół)
