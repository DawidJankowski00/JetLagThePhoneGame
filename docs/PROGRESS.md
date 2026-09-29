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

**Po etapie 1 (ustalenie):** `LocationSharingPolicy` — Hide and Seek: ukrywający widzą szukających;
Tag: goniący widzą uciekających; nigdy odwrotnie. Etapy 4/5/7 muszą z tego korzystać przy pingach i mapach.
Testy: 24/24.

## ✅ Etap 2 — Mapa + obszar + stacje (2026-09-29)

**Działa:**
- `TransitGames.Geo`: `LocalProjection` (metry, ProjNet), `GameArea` (WGS84 + lokalna geometria, klucz,
  `Contains`, uproszczenie), `GeoJson` (odczyt Feature/FeatureCollection/geometrii, suma poligonów),
  `OverpassClient` (User-Agent, fallback serwerów, ponowienie), parsery stacji i granic,
  `StationDeduplicator` (nazwa + 150 m, numery peronów), `GeoJsonStationImporter`, `StationCatalog`
  (pobranie → filtr obszaru → deduplikacja → cache SQLite; offline z cache), `BoundaryService`.
- Core: `GameSettings.Stations` (kopia stacji w sesji), `IGameAreaRepository` (zapisane obszary).
- UI: `IMapLayerProvider`/`MapLayerProvider` (kafelki OSM z cache, warstwy poligonów/punktów/stacji),
  `GameMapView`, `IDeviceLocationService`, `ITextFilePicker`, style `ToolButton`/`HintBanner`.
- Kreator: rola → **obszar** (rysowanie / miasto z OSM przez dotknięcie mapy / GeoJSON / zapisane) →
  **stacje** (tryby, pobranie, włącz/wyłącz na mapie i liście, własne stacje, import GeoJSON) → start.
  Ekrany gier pokazują mapę z obszarem i stacjami.
- Uprawnienia: lokalizacja (Android + iOS `NSLocationWhenInUseUsageDescription`), internet.
- Zweryfikowano na emulatorze z prawdziwymi danymi: Śródmieście (granica OSM, 15,6 km²) → 50 stacji
  (91 elementów, 41 scalonych) → wyłączenie stacji tapnięciem → start gry Tag → po `force-stop` gra wraca
  z mapą, obszarem i 49 stacjami. Rysowanie poligonu („Mój obszar · 6,4 km²”).
- Porządek: usunięty zbędny `SQLitePCLRaw.bundle_green` (duplikat natywnego SQLite w APK).
- Testy: 86/86 (Core 19, Geo 61, HideAndSeek 3, Tag 3).

**Następny krok — Etap 3: Nagrywanie GPS + eksport**
- `TransitGames.Tracking`: zapis punktów do SQLite na bieżąco (interwał, min. dystans, odrzucanie słabej
  dokładności, tryb oszczędzania baterii), segmenty przy przerwach.
- Eksport GPX 1.1 (track per urządzenie, segmenty, waypointy ze zdarzeń gry) i GeoJSON (LineString + Pointy);
  testy walidujące strukturę plików.
- Android: foreground service z powiadomieniem (`FOREGROUND_SERVICE_LOCATION`, `ACCESS_BACKGROUND_LOCATION`,
  `POST_NOTIFICATIONS`); iOS: `UIBackgroundModes: location`, `AllowsBackgroundLocationUpdates`.
- `ILocationTracker` (MAUI) → własna pozycja na `GameMapView`; lista tras (zakładka „Trasy GPS”), Share,
  prosty podgląd trasy.
