# Decyzje i uproszczenia

## Architektura

- **Gra = 2 projekty** (logika `net10.0` + UI MAUI). Specyfikacja wymagała jednocześnie `IGameModule.GetRoutes()`
  (strony Shell) i braku zależności modułów gier od MAUI. Rozwiązanie (uzgodnione): `IGameModule` w Core bez tras,
  trasy/strony w `IGameUiModule` (projekt `TransitGames.UI`), rejestracja jedną linią `services.AddGame<XUiModule>()`.
- **Dodatkowy projekt `TransitGames.UI`** (MAUI class library): wspólny motyw, kontrolki, `IGameUiModule`,
  dialogi/nawigacja, później mapa (`IMapLayerProvider`) i usługi platformowe (GPS). Dzięki niemu projekty UI gier
  nie zależą od `TransitGames.App`.
- **SQLite w Core** (`sqlite-net-pcl`): jedna współdzielona baza `AppDatabase`, tabele tworzone leniwie przez
  repozytoria. Core nadal nie zależy od MAUI.
- **Stan sesji jako jeden obiekt JSON** (`GameSessionBase<TSettings,TState>`), zapisywany po każdej mutacji
  (`Mutate`). Wznawianie po zabiciu aplikacji: `GameSessionManager.TryResumeAsync()` przy starcie ekranu głównego
  automatycznie przechodzi do ekranu gry.
- **Obszar gry w Core jako GeoJSON (tekst)** — `GameAreaDefinition`; parsowanie geometrii w `TransitGames.Geo`.
- **Timery jako znaczniki czasu** (`RoundTimer`), nie jako działający licznik — przeżywają restart aplikacji.
- **Prywatność lokalizacji (ustalenie z właścicielem projektu, poza specyfikacją dla Hide and Seek)**:
  udostępnianie jest jednokierunkowe i zadeklarowane w module (`IGameModule.LocationSharing`,
  `LocationSharingPolicy`). Hide and Seek: ukrywający widzą szukających, nigdy odwrotnie. Tag: goniący widzą
  uciekających, nigdy odwrotnie (przy zamianie ról reguła podąża za rolą). Domyślnie wszystko jest prywatne;
  każde wysłanie/pokazanie pozycji innej roli musi przejść przez `CanSee`/`EnsureMayShare`.
- **`LocalGameSyncService`**: tryb offline; każda publikowana wiadomość wraca lokalnie i trafia do `Outbox`
  (UI może pokazać, co przekazać drugiej drużynie komunikatorem).

## Build

- **TFM-y MAUI** ustawiane centralnie (`MauiTargetFrameworks` w `Directory.Build.props`): Android zawsze,
  iOS na macOS albo z `-p:EnableIos=true`. Windows/MacCatalyst usunięte (nie są celem projektu).
  iOS weryfikowany w CI (`macos-latest`, build bez podpisu na symulator).
- **Central Package Management** + transitive pinning: przypięty natywny `SQLitePCLRaw.lib.e_sqlite3(.android)`
  2.1.13 z powodu podatności GHSA-2m69-gcr7-jv3q w 2.1.11 (zależność `sqlite-net-pcl`).
- **Silnie typowane zasoby bez plików Designer**: `Directory.Build.targets` generuje klasy z `Resources/*.resx`
  przy buildzie. Kultury inne niż `en` trzeba dopisać w tym pliku.
- `Microsoft.Maui.Controls` przypięty jawnie (10.0.110) w `Directory.Packages.props`.

## UI

- Język wybrany w Ustawieniach działa po restarcie aplikacji (zasoby czytane przez `x:Static` przy tworzeniu stron).
  Zasoby neutralne = polski, więc urządzenie w innym języku niż angielski dostaje polski.
- Trasy Shell gier bez ukośników (`hideandseek-game`), żeby uniknąć niejednoznaczności routingu kontekstowego.
