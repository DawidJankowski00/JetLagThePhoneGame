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

## ✅ Etap 4 — Tryb online: serwer SignalR w Dockerze (2026-09-29)

Zmiana kolejności (decyzja właściciela): serwer działa w Dockerze na prywatnym serwerze, gra tylko przez VPN.
Tryb online powstał przed grami, żeby Hide and Seek i Tag od razu z niego korzystały. Nowa numeracja:
**5 Hide and Seek, 6 Tag, 7 dopracowanie**.

**Działa:**
- `TransitGames.Server` (ASP.NET Core + SignalR + SQLite):
  - lobby z 6-znakowym kodem (bez mylących znaków), dołączanie z wyborem roli, ustawienia gry dla dołączających;
  - tokeny urządzeń (SHA-256 na serwerze), tożsamość nadawcy z tokenu;
  - routing wiadomości wg roli, egzekwowanie `LocationSharingPolicy` dla pozycji;
  - numery sekwencyjne, `Sync` (nadrabianie), `LatestLocations`, obecność graczy;
  - sprzątanie starych gier, `/health`, `/api/info`.
- Docker: `src/TransitGames.Server/Dockerfile`, `deploy/docker-compose.yml` (wolumen na bazę, `BIND_ADDRESS`
  do nasłuchu tylko na VPN), opis w `docs/SERVER.md`. Obraz zbudowany i uruchomiony lokalnie; gra przetrwała
  restart kontenera.
- `TransitGames.Sync` (klient): `OnlineServerClient` (REST, kody błędów), `SignalRGameSyncService`
  (ponowne łączenie bez końca, nadrabianie w kolejności, trwała kolejka wysyłki, echo lokalne),
  `OnlineSessionStore`.
- Core: `GameSyncRouter` (gry używają `IGameSyncService`, który przełącza się offline/online),
  kontrakty protokołu, wspólne id sesji, `IGameModule.Serialize/DeserializeSettings`,
  `GetLocationShareInterval` (Tag: `RevealInterval`), `GameSessionManager.FinishAsync(id)` i `SessionFinished`.
- UI/App:
  - Ustawienia → „Gra online”: adres serwera, nazwa gracza, „Sprawdź połączenie”.
  - Kreator: przełącznik „Graj online” → kod gry po starcie.
  - Ekran startowy → „Dołącz do gry online”: kod → gra i gracze → wybór roli.
  - Nagłówek gry: stan połączenia i liczba graczy, kod (dotknięcie = udostępnij), kto widzi moją pozycję, jak
    często i kiedy ostatnio.
  - Mapa: pozycje innych graczy (drużyna / przeciwnicy) z wiekiem pozycji.
  - Koniec gry na jednym telefonie kończy ją na wszystkich.
- Zweryfikowano na emulatorze z serwerem w Dockerze i drugim „telefonem” (bot na bibliotece Sync):
  - Tag: emulator dołączył kodem (wpisanym małymi literami z myślnikiem) jako uciekający; goniący dostał jego
    pozycję; nagłówek: „Twoją pozycję widzą: Goniący · co 15 min · ostatnio 10:13”; kolejna pozycja nie została
    wysłana przed upływem interwału; „Zakończ grę” dotarło do goniącego.
  - Hide and Seek: emulator założył grę jako ukrywający; szukający wysyłał pozycję co 5 s → marker
    „Bartek · teraz” na mapie ukrywającego; szukający **nie dostał żadnej** pozycji ukrywającego; koniec gry
    ogłoszony przez szukającego zamknął grę i nagrywanie na emulatorze.
- Testy: 137/137 (Core 25, Geo 61, Tracking 25, Sync 19 — w tym serwer w pamięci: prywatność pozycji,
  podszywanie się pod rolę, nadrabianie po restarcie, kolejka offline; HideAndSeek 3, Tag 4).

## ✅ Etap 5 — Hide and Seek (2026-09-29)

**Działa:**
- Logika (`TransitGames.Games.HideAndSeek`, bez MAUI):
  - pytania `IQuestion` (`Apply` + `SuggestAnswer`): radar, termometr, dopasowanie (Woronoj; dzielnice przez
    zawieranie), mierzenie (suma kół), macki (koło ∩ Woronoj kandydatów), zdjęcie;
  - `EliminationEngine`: obszar = funkcja listy pytań, strefy ukrycia, „zostało X z Y stacji”;
  - `PlaceCatalog`: miejsca z OSM (10 kategorii + stacje gry + dzielnice) z pamięcią podręczną;
  - sesja: fazy (ukrywanie/szukanie/znalezieni), wynik z premiami, stacja ukrywających, termometr;
  - karty: talia JSON (premie, klątwy, weto, „dobierz dwie”), dobieranie N/zostaw K za odpowiedź;
  - wiadomości `hs.*` i `HideAndSeekMessageHandler` (idempotentny).
- Core: `GameSessionManager.MutateAsync`, `IGameMessageHandler` + `GameMessageDispatcher`,
  `GameSettings.StartedAt` (wspólny czas startu gry online).
- UI:
  - kreator: krok „Ustawienia gry” (rozmiar, czas na ukrycie, dostępne pytania z nagrodami, karty, online);
  - mapa: strefy możliwe, obszar wykluczony, stacje wykluczone na szaro, legenda, strefa ukrywających;
  - szukający: zadawanie pytań (GPS z korektą na mapie, pobieranie miejsc, termometr ze startem), historia
    z wpisywaniem/korektą odpowiedzi i usuwaniem, klątwy na ekranie, „znalezieni”;
  - ukrywający: odliczanie, wybór stacji (mapa/losowanie), pytania z podpowiedzią z GPS i wetem, wybór kart,
    zagrywanie kart, „dodaj otrzymane pytanie” offline.
- Zweryfikowano na emulatorze (online, drugi telefon = bot-ukrywający na bibliotece gry):
  - dołączenie kodem jako szukający; odliczanie i blokada pytań w czasie ukrywania;
  - radar 1 km → bot odpowiedział z podpowiedzi „Nie” → strefa środkowa wycięta;
  - dopasowanie „stacja” → „Nie, inne” → stacja Centrum wykluczona (7 z 8), bot zagrał premię +5 min;
  - mierzenie „muzeum” → muzea pobrane z OSM, koła 127 m wycięte ze stref;
  - „znalezieni” → koniec rundy na obu telefonach.
  - offline jako ukrywający: kreator z ustawieniami, wybór stacji z mapy, dodanie otrzymanego pytania, poprawna
    podpowiedź z GPS, odpowiedź → „zostaw 1 z 2 kart” → karta na ręce.
  - Znaleziony i poprawiony błąd: telefon dołączający liczył czas ukrycia od chwili dołączenia.
- Testy: 166/166 (Core 27, Geo 61, Tracking 25, Sync 19, HideAndSeek 29 — w tym test własności podpowiedzi
  na 60 losowych rundach, Tag 4).

**Następny krok — Etap 6: Tag**
- Cele na mapie (ręcznie / losowane ze stacji / POI OSM), zdobywanie ręczne i automatyczne (geofencing),
  nagrody, cel końcowy.
- Pingi pozycji uciekających goniącym co `RevealInterval` (online automatycznie — gotowe w `OnlineSessionCoordinator`;
  offline ręczny ping z wpisaniem pozycji).
- „Złapany”: czas i miejsce, zamiana ról i kolejna runda (online: zmiana roli urządzenia na serwerze), tabela wyników.
- Wyzwania w JSON.
