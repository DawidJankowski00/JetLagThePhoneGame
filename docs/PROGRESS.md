# Postęp prac

Plik służy jako podsumowanie do kontynuacji pracy (także w nowej sesji). Specyfikacja: kamienie milowe 1–7.

## ✅ Etap 1 — Szkielet (2026-09-29)

**Działa:**
- Solution (`TransitGames.sln`) + filtr `TransitGames.Libraries.slnf` (biblioteki + testy, bez MAUI).
- Core: `IGameModule`/`GameModule<TSettings,TSession>`, `GameRegistry`, `GameSessionBase<TSettings,TState>`
  (stan jako JSON, `Mutate` → autozapis), `GameSessionManager` (start/wznowienie/koniec, forward zdarzeń do logu),
  `GameEvent` + `IGameEventLog`, `RoundTimer`, `IClock`, `IGameSyncService` + `LocalGameSyncService`,
  `ILocationTracker` (interfejs), `Station` + `IStationRepository`, SQLite (`AppDatabase`, sesje, zdarzenia, stacje).
- Moduły gier (logika): `HideAndSeekModule` (role hider/seeker, ustawienia rozmiaru/promienia/czasu),
  `TagModule` (role runner/chaser, ustawienia rundy/pingów/promienia/celów). Sesje bez logiki gry.
- UI: `TransitGames.UI` (motyw zielony, jasny/ciemny, style `BigButton`/`Card`, kolory warstw mapy,
  `IGameUiModule` + `AddGame<T>()`, `ActiveSessionViewModel` + `SessionHeaderView`, dialogi/nawigacja, `AppPreferences`).
- App: Shell z zakładkami Gry / Historia / Trasy GPS / Ustawienia, kreator nowej gry (wybór roli),
  ekrany gier (placeholder z nagłówkiem, „Zakończ”, „Wyjdź”), historia sesji, ustawienia języka i motywu.
- Lokalizacja: resx pl (neutralny) + en, klasy generowane przy buildzie.
- CI: GitHub Actions (testy na Linuksie, Android na Windows, iOS na macOS).
- Zweryfikowano na emulatorze (Pixel 7, API 36): lista gier → rola → start → ekran gry; po `force-stop`
  aplikacja wraca do trwającej gry.
- Testy: 16/16 (Core 12, HideAndSeek 2, Tag 2).

**Następny krok — Etap 2: Mapa + obszar + stacje**
- Mapsui w `TransitGames.UI` (kontrolka mapy, `IMapLayerProvider`, OSM tiles z User-Agent i cache).
- `TransitGames.Geo`: parsowanie GeoJSON (NTS), projekcja AEQD wokół środka obszaru (ProjNet), klucz obszaru,
  klient Overpass (stacje z filtrami trybów, granice miast), deduplikacja stacji ~150 m, import GeoJSON.
- Kreator: krok „obszar gry” (rysowanie poligonu / miasto z OSM / import) i „stacje” (włącz/wyłącz, dodaj własną).
- Uprawnienia Android/iOS do lokalizacji i internetu.
