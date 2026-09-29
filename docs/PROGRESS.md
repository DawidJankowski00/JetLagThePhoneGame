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

## ✅ Etap 3 — Nagrywanie GPS + eksport (2026-09-29)

**Działa:**
- `TransitGames.Tracking`:
  - `TrackPointFilter`: dokładność, interwał, minimalny dystans, 1 punkt/min przy postoju, nowy segment po
    przerwie w sygnale.
  - `SqliteTrackStore`: każdy punkt zapisywany od razu, statystyki trasy w tej samej transakcji.
  - `TrackRecorder`: jedna trasa na sesję; wznowienie dopisuje nowy segment.
  - `GpxWriter` (GPX 1.1 zgodny z oficjalnym XSD, waypointy ze zdarzeń gry) i `GeoJsonTrackWriter`
    (RFC 7946, czasy punktów).
  - `TrackExporter` i `TrackPlayback` (interpolacja pozycji, przerwy między segmentami).
- Android: `LocationTrackingService` (usługa pierwszoplanowa typu location z powiadomieniem) i
  `AndroidLocationTracker` (`LocationManager`, fused/GPS). Uprawnienia: `FOREGROUND_SERVICE(_LOCATION)`,
  `ACCESS_BACKGROUND_LOCATION` (tylko zadeklarowane), `POST_NOTIFICATIONS`.
- iOS: `IosLocationTracker` (`CLLocationManager` z aktualizacjami w tle) i `UIBackgroundModes: location`.
- UI:
  - `TrackingCoordinator`: start po wejściu na ekran gry, zamknięcie trasy po zakończeniu gry, ręczne
    nagrywanie.
  - Nagłówek gry: wskaźnik „● GPS” i własna pozycja na mapie.
  - `GameMapView`: właściwości `Track` i `Waypoints`.
  - Zakładka „Trasy GPS”: stan nagrywania, start i stop, lista tras, podgląd, udostępnianie GPX lub GeoJSON,
    usuwanie.
  - Podgląd trasy z suwakiem czasu i odtwarzaniem.
  - Ustawienia nagrywania: automatycznie w grze, oszczędzanie baterii, interwał, dystans, dokładność.
- Zweryfikowano na emulatorze (Pixel 7, API 36):
  - Gra wznowiona po starcie aplikacji włącza nagrywanie, a usługa działa na pierwszym planie z
    powiadomieniem.
  - Przy uśpionym ekranie zapisało się 12 wysłanych pozycji (731 m).
  - Podgląd i odtwarzanie działają.
  - Udostępnianie GPX otwiera systemowe okno Share; plik pobrany z urządzenia przechodzi walidację XSD bez
    błędów.
  - „Zakończ grę” zamyka trasę i zatrzymuje usługę.
  - Po restarcie aplikacji trasa jest kontynuowana w nowym segmencie.
- Testy: 111/111 (Core 19, Geo 61, Tracking 25, HideAndSeek 3, Tag 3).

**Otwarte pytanie do użytkownika:** czy tryb online (serwer ASP.NET Core + SignalR, Etap 7) ma powstać przed
grami (Etapy 4–5), i gdzie ma działać serwer.

**Następny krok — Etap 4: Hide and Seek**
- Logika:
  - strefy ukrycia wokół stacji;
  - pytania (radar, termometr, porównania, matching…) z liczeniem obszaru wyeliminowanego w projekcji
    metrycznej;
  - licznik „zostało X z Y stacji”.
- Mapa szukających: obszar wyeliminowany i możliwy, wykluczone stacje. Ekran ukrywającego: podpowiedź
  odpowiedzi liczona lokalnie.
- Ukrywający widzi pozycję szukających (`LocationSharingPolicy`); w trybie offline przez `LocalGameSyncService`.
- Zdarzenia z lokalizacją (pytania) trafiają do eksportu jako waypointy.
