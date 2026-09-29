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

## Geo (etap 2)

- **Projekcja lokalna = Transverse Mercator (WGS84) ze środkiem w centroidzie obszaru**, nie AEQD: ProjNet 2.1
  nie ma projekcji azymutalnej równoodległej. Specyfikacja podaje AEQD jako przykład; TM ze współczynnikiem 1
  w promieniu 50 km ma błąd skali < 0,004 % (< 2 m na 50 km), co testy porównują z łukiem południka i równoleżnika.
  Wszystkie obliczenia (odległości, bufory, deduplikacja) idą w metrach przez `LocalProjection`.
- **Stacje z Overpass pobierane w bbox obszaru** (`out center`), filtrowane dokładnym poligonem lokalnie
  (szybsze i pewniejsze niż filtr `poly:` z tysiącami wierzchołków).
- **Deduplikacja**: ta sama nazwa (bez rozróżniania wielkości liter i białych znaków) + łańcuch odległości ≤ 150 m
  (single linkage). Scalona stacja ma najmniejsze leksykograficznie id (stabilne między pobraniami — flagi
  włączenia przeżywają ponowne pobranie), sumę trybów i średnią pozycję.
- **Numery peronów**: przystanki typu „Centrum 07” (Warszawa i inne miasta z numeracją ZTM) — sufiks liczbowy
  jest usuwany, gdy powtarza koniec `ref`/`local_ref`; bez tego każdy peron byłby osobną stacją (na danych
  Śródmieścia: 91 → 50 stacji). Stacje kolejowe/metra (`railway=station|halt`, `public_transport=station`)
  zachowują pełną nazwę.
- **Elementy bez nazwy są pomijane** — graczom trzeba móc powiedzieć, o którą stację chodzi.
- **Granice administracyjne**: wybór przez dotknięcie mapy (`is_in` → lista granic od najmniejszej), bez
  wyszukiwania po nazwie (globalne zapytania po nazwie są wolne i niejednoznaczne). Geometria z relacji
  (`out geom`): outer − inner przez `Polygonizer`, upraszczana do 10 m (inaczej granica miasta to setki kB
  w każdym zapisie sesji).
- **Overpass**: dwa publiczne serwery (overpass-api.de, maps.mail.ru), identyfikujący User-Agent, 2 rundy
  prób z przerwą 4 s przy 429/5xx/timeout (publiczne instancje często chwilowo zwracają 504).
- **Klucz obszaru** = SHA-256 znormalizowanej geometrii zaokrąglonej do 1e-6° → cache stacji per obszar,
  ten sam plik zaimportowany dwa razy trafia w ten sam cache.
- **Sesja ma własną kopię obszaru i włączonych stacji** (`GameSettings.Area`, `GameSettings.Stations`),
  więc wznowiona gra nie zależy od późniejszych zmian w cache stacji. Obszary są też zapamiętywane
  (`IGameAreaRepository`) do ponownego użycia offline.

## Mapa

- **Kafelki OSM** (`tile.openstreetmap.org`) przez `HttpTileSource` z User-Agent aplikacji i trwałym cache
  plikowym (14 dni, `FileSystem.CacheDirectory/tiles/osm`), atrybucja wyświetlana przez Mapsui.
  Przy większym ruchu (publiczne wydanie) trzeba przejść na dostawcę kafelków z umową — polityka OSM
  nie pozwala na intensywne użycie.
- **`GameMapView`** (TransitGames.UI) to jedna kontrolka mapy dla wszystkich ekranów: warstwy obszaru, stacji,
  rysowanego poligonu i własnej pozycji jako bindowalne właściwości; gry dokładają własne warstwy
  przez `AddLayer` i `IMapLayerProvider`. Kafelki są jasne także w trybie ciemnym.

## Nagrywanie GPS (etap 3)

- **Jedna trasa na urządzenie i sesję gry.** Po wznowieniu gry (restart aplikacji, powrót do gry) nagrywanie
  dopisuje się do otwartej trasy w **nowym segmencie**. Zakończenie gry zamyka trasę. „Wyjdź” z ekranu gry
  **nie** zatrzymuje nagrywania, bo gra w terenie trwa dalej; można je zatrzymać ręcznie w zakładce „Trasy GPS”.
- **Nagrywanie startuje po wejściu na ekran gry**, a nie gdy sesja staje się bieżąca. Ekran startowy też
  wczytuje niedokończoną sesję, ale tylko po to, żeby pokazać przycisk „Wznów”, i to nie może włączać GPS.
- **Filtrowanie w kodzie wspólnym** (`TrackPointFilter`), bo iOS nie ma interwału. Filtr:
  - odrzuca punkty o dokładności gorszej niż próg oraz punkty spóźnione;
  - przerzedza do zadanego interwału (z tolerancją 10%) i minimalnego dystansu;
  - przy postoju zapisuje 1 punkt na minutę, żeby oś czasu przy odtwarzaniu była prawdziwa;
  - brak dobrego sygnału dłużej niż `SegmentGap` (2 min, w oszczędzaniu baterii 3 min), np. w metrze,
    rozpoczyna nowy segment.
- **Dystans liczony wzorem haversine** i tylko wewnątrz segmentów. Przerw (metro) nie zgadujemy.
  Geometria obszarów nadal korzysta z projekcji metrycznej w `TransitGames.Geo`.
- **Każdy punkt jest zapisywany od razu** (insert punktu i aktualizacja statystyk trasy w jednej transakcji),
  więc crash aplikacji nie gubi trasy.
- **Android:**
  - `LocationManager` (provider `fused` na Androidzie 12+ z jakością zależną od oszczędzania baterii,
    wcześniej GPS) i usługa pierwszoplanowa typu `location` z powiadomieniem. Usługa nie korzysta z
    Google Play Services, więc nie wymaga dodatkowych pakietów.
  - Usługa jest `NotSticky`, bo nowsze Androidy nie pozwalają uruchomić usługi lokalizacji z tła. Jeśli system
    zabije proces, nagrywanie wraca razem z grą po otwarciu aplikacji, w nowym segmencie.
  - `ACCESS_BACKGROUND_LOCATION` jest zadeklarowane (zgodnie ze specyfikacją), ale aplikacja nie prosi o
    „zezwalaj zawsze”: usługa pierwszoplanowa uruchomiona z aplikacji wystarcza do nagrywania przy
    wygaszonym ekranie. Zweryfikowano to na emulatorze (API 36).
- **iOS:** `CLLocationManager` z `AllowsBackgroundLocationUpdates`, `PausesLocationUpdatesAutomatically = false`
  i niebieskim wskaźnikiem w tle, `UIBackgroundModes: location`. Wystarcza uprawnienie „podczas używania”.
  Kod iOS jest kompilowany tylko w CI na macOS.
- **Eksport:**
  - GPX 1.1 ma waypointy ze zdarzeń gry z lokalizacją, `trk` na urządzenie i `trkseg` na segment. Test
    sprawdza go oficjalnym XSD (`tests/.../Schemas/gpx.xsd`).
  - GeoJSON (RFC 7946) ma `LineString`/`MultiLineString` oraz `Point` dla zdarzeń. Czasy punktów są w
    `coordinateProperties.times` (konwencja togeojson i gpx.studio).
  - Liczby są zapisywane w formacie niezależnym od kultury.
- **Udostępnianie** przez systemowy Share (`FileSystem.CacheDirectory/exports`), bez własnego FileProvidera,
  bo MAUI obsługuje to sam.
- **Podgląd trasy:** suwak czasu i odtwarzanie całej trasy w 30 s. Pozycja jest interpolowana wewnątrz
  segmentu, a w przerwie między segmentami stoi na ostatnim punkcie.
- **Własna pozycja w grze** pochodzi z `ILocationTracker` tego urządzenia i jest pokazywana tylko lokalnie.
  Udostępnianie pozycji innym rolom nadal przechodzi wyłącznie przez `LocationSharingPolicy`.

## Tryb online (etap 4)

- **Kolejność zmieniona na prośbę właściciela projektu:** tryb online (w specyfikacji etap 7, opcjonalny) powstał
  przed grami, bo obie gry opierają się na wymianie pytań/pingów i udostępnianiu pozycji. Gry (etapy 5–6) od razu
  korzystają z `IGameSyncService`; tryb offline (`LocalGameSyncService`) nadal działa bez serwera.
- **Hosting (ustalenie z właścicielem):** kontener Docker na prywatnym serwerze, dostęp tylko przez VPN. Dlatego
  zwykłe HTTP (Android `usesCleartextTraffic`, iOS `NSAllowsArbitraryLoads`) i adres serwera wpisywany w
  Ustawieniach. Przed publikacją w sklepach trzeba przejść na HTTPS i zawęzić wyjątek ATS.
- **Serwer nie zna gier.** Host przysyła role, ustawienia (JSON) i reguły `LocationSharingPolicy`; serwer
  egzekwuje je dla wiadomości `location` (drużyna nadawcy + dozwoleni widzowie), resztę kieruje wg `Audience`.
  Dzięki temu nowa gra nie wymaga zmian na serwerze. Tożsamość (rola, urządzenie, nazwa) pochodzi z tokenu.
- **Uwierzytelnienie:** losowy token urządzenia (32 B) wydawany przy założeniu/dołączeniu, na serwerze tylko
  SHA-256. Bez kont i haseł — wystarczy kod gry + VPN.
- **Kolejność i niezawodność:** serwer nadaje globalny numer sekwencyjny. Klient po każdym (ponownym) połączeniu
  pobiera przegapione wiadomości (`Sync`), buforując w tym czasie wiadomości na żywo, więc każda dociera raz i w
  kolejności. Wiadomości wysyłane bez zasięgu (metro) czekają w trwałej kolejce SQLite (`sync_outbox`) i są
  wysyłane po odzyskaniu połączenia; publikacja jest idempotentna (po `SyncMessage.Id`). Ponowne łączenie
  trwa bez końca (0/2/5/10/30 s).
- **Echo lokalne:** opublikowana wiadomość od razu wraca przez `MessageReceived` (tak jak offline), a serwer nie
  odsyła jej nadawcy — gry obsługują własne i cudze wiadomości w jednym miejscu.
- **Udostępnianie pozycji:** robi to aplikacja (`OnlineSessionCoordinator`), nie gra: tylko gdy polityka gry
  pozwala komuś widzieć rolę tego telefonu, co `IGameModule.GetLocationShareInterval` (Hide and Seek ≈ na żywo,
  co 20 s; Tag co `RevealInterval`). Wymaga ciągłej pozycji, więc wtedy nagrywanie trasy startuje nawet przy
  wyłączonym „Nagrywaj trasę podczas gry”. Nagłówek gry pokazuje, kto widzi pozycję, jak często i kiedy ostatnio
  ją wysłano (albo „🔒 nie jest udostępniana”). Pozycje innych: na mapie z wiekiem pozycji; po restarcie
  aplikacji ostatnie znane pozycje przychodzą z serwera (`LatestLocations`).
- **Wspólne id sesji:** gra online ma na wszystkich telefonach to samo `SessionId` (`IGameModule.CreateSession`
  przyjmuje id). Host najpierw zakłada grę na serwerze, dopiero potem lokalnie — błąd serwera nic nie zostawia.
- **Koniec gry:** „Zakończ grę” na jednym telefonie kończy ją wszędzie (`session.finished`); drugi telefon
  pokazuje komunikat i zamyka trasę (`GameSessionManager.FinishAsync(id)` działa też dla gry, z której wyszliśmy).
  Uproszczenie: jeśli telefon kończący grę jest akurat bez zasięgu, wiadomość zostaje w kolejce, a połączenie
  zakończonej gry nie jest już wznawiane — drugi telefon trzeba wtedy zakończyć ręcznie.
- **Obecność** (kto jest połączony) jest trzymana w pamięci serwera; po restarcie kontenera wszyscy łączą się
  ponownie automatycznie.

## Hide and Seek (etap 5)

- **Jedna sesja = jedna runda ze stałymi rolami.** Zamiana ról wymagałaby zmiany roli urządzenia na serwerze
  (token jest przypisany do roli) i odwrócenia udostępniania pozycji; kolejną rundę zaczyna się jako nową grę
  z zamienionymi rolami. Wynik rundy (czas ukrycia + premie) jest w historii i na osi czasu.
- **Obszar możliwy = funkcja listy pytań.** `EliminationEngine` liczy wszystko od obszaru gry przy każdej zmianie
  (korekta/usunięcie odpowiedzi = przeliczenie), w tle i z pamięcią podręczną. „Możliwa kryjówka” to część obszaru
  zostawiona przez odpowiedzi **wewnątrz stref ukrycia** stacji, które wciąż są możliwe; stacja jest wykluczona,
  gdy jej strefa nie ma wspólnej części (> 1 m²) z obszarem.
- **Geometria** w metrach lokalnej projekcji, operacje przez `OverlayNGRobust`. Komórki Woronoja z NTS są
  przycinane własnym, odpornym overlayem (wbudowane przycinanie NTS rzucało `TopologyException` dla symetrycznych
  punktów). Termometr: półpłaszczyzna od symetralnej odcinka start–koniec. Mierzenie: suma kół o promieniu
  odległości szukających do ich najbliższego obiektu. Macki: koło ∩ komórka Woronoja wśród obiektów w promieniu;
  odpowiedź „poza zasięgiem” odejmuje koło.
- **Test własności:** dla losowych pozycji i pytań odpowiedź podpowiedziana z pozycji ukrywających nigdy tej pozycji
  nie wyklucza — to gwarancja spójności `SuggestAnswer` z `Apply`.
- **Miejsca do pytań** (muzea, parki, szpitale…, dzielnice) z Overpass w prostokącie obszaru powiększonym o 3 km
  (najbliższe muzeum może leżeć poza obszarem), z pamięcią podręczną w SQLite. Stacje gry też są kategorią.
  **Pytanie niesie ze sobą listę miejsc**, więc oba telefony liczą na tych samych danych, także offline, a
  historia jest odtwarzalna. Dzielnice: `admin_level` 9 (w razie braku 10, potem 8), obrysy uproszczone do 25 m.
  **Uproszczenie:** kategoria „linia metra” ze specyfikacji nie jest obsługiwana (wymaga geometrii linii i innej
  definicji „najbliższej”); dopasowanie „ta sama dzielnica” zastępuje przykład „dzielnica administracyjna”.
- **Podpowiedź odpowiedzi** liczona z GPS ukrywających (gdy brak – środek ich stacji); ukrywający zawsze
  zatwierdzają ręcznie. Wybrana stacja ukrywających nigdy nie opuszcza telefonu; wiadomość „znalezieni” wysłana
  przez ukrywających nie zawiera ich pozycji.
- **Karty:** talia w JSON (`Cards/deck.json`, własne teksty pl/en; własną można podać w `HideAndSeekSettings.DeckJson`).
  Za każdą (pierwszą) odpowiedź ukrywający dobierają N i zostawiają K kart wg `QuestionRule` typu pytania
  (konfigurowalne, domyślne wartości własne). Premie czasowe i klątwy są jawne dla obu drużyn (wiadomość `hs.card`),
  weto zagrywa się przy odpowiedzi (pytanie nic nie wyklucza i nie daje kart). Talia tasowana deterministycznie
  wg id sesji.
- **Czas:** gospodarz zapisuje moment startu w `GameSettings.StartedAt`, dołączający liczą czas ukrycia od niego
  (bez tego telefon dołączający później dostawał dodatkowy czas — znalezione na emulatorze). Pytania są dostępne
  po czasie na ukrycie; czas na odpowiedź jest pokazywany ukrywającym, ale nie jest wymuszany.
- **Wiadomości gier:** `IGameMessageHandler` + `GameMessageDispatcher` w Core stosują wiadomości do sesji także
  wtedy, gdy gra nie jest na ekranie (`GameSessionManager.MutateAsync`). Obsługa jest idempotentna (echo
  i nadrabianie po ponownym połączeniu). Korekty szukających (wpisanie/zmiana odpowiedzi, usunięcie pytania) idą
  tylko do ich drużyny.
- **Offline:** szukający wpisują odpowiedzi ręcznie (dotknięcie pytania); ukrywający dodają otrzymane pytanie tym
  samym formularzem, wskazując na mapie pozycję szukających — dostają podpowiedź i karty jak online.
- **Kreator:** nowy ostatni krok „Ustawienia gry” z widokiem dostarczanym przez grę
  (`IGameUiModule.CreateSettingsView`); przełącznik „Graj online” przeniesiony tutaj.
- Powiadomienia systemowe (koniec czasu ukrycia, nowe pytanie) — w etapie dopracowania.

## Tag (etap 6) — zasady zastąpione przebudową niżej

Opis pierwszej wersji (drużyny, rundy, cele); stare gry są w historii tylko do odczytu.


- **Rundy w jednej sesji, zamiana ról przez `RoleChange`.** Wiadomość kończąca rundę (`tag.round.end`) niesie
  `RoleChange` (zamiana runner↔chaser) dla konkretnej „wersji ról”. Stosują ją wszyscy tak samo: sesja
  (`IGameSession.ApplyRoleChange`, po handlerze gry w `GameMessageDispatcher`), transport (`OnlineLink.RoleId`),
  koordynator online (udostępnianie pozycji przełącza się od razu, stare pozycje znikają z mapy) i serwer. Zmiana
  działa tylko dla wersji, dla której ją wysłano — gdy obie drużyny naciśną „Złapany” naraz, role zmieniają się
  raz. Drużyny („Drużyna 1” = zaczyna uciekać) wynikają z roli i liczby zamian, więc działa to też dla telefonu,
  który dołączył po kilku rundach (dostaje `RolesVersion` w bilecie).
- **Serwer i prywatność po zamianie.** Każda wiadomość pamięta wersję ról z chwili wysłania; urządzenie widzi ją,
  jeśli jego rola *w tamtej wersji* była odbiorcą (historia ról w tabeli `device_roles`). Po zamianie nikt nie
  nadrobi więc pozycji ani wiadomości drużynowych, których wcześniej nie mógł widzieć. Połączenia są przenoszone
  między grupami ról w chwili zamiany, a publikacje i zamiany w jednej grze są szeregowane (`SessionGate`).
  „Ostatnie pozycje” po restarcie aplikacji pochodzą tylko z bieżącej wersji ról. Wersja protokołu → 2.
- **Pozycja goniących nigdy nie trafia do uciekających** — także przy „Złapany”: wiadomość wysłana przez goniących
  nie zawiera miejsca (miejsce złapania zapisuje się tylko, gdy zgłoszą je uciekający). Dlatego **zasięg złapania
  jest zasadą dla graczy**, a nie sprawdzeniem w aplikacji; złapanie potwierdza się przyciskiem.
- **Cele** są w ustawieniach gry (te same w każdej rundzie, statusy liczone per runda) i niosą swoje wyzwanie, więc
  wszystkie telefony mają identyczne dane. Źródła: losowe stacje, losowe miejsca z OSM (`PlaceCatalog`
  przeniesiony z Hide and Seek do `TransitGames.Geo.Places`, wspólny dla gier) albo ręcznie na mapie. Losowanie
  rozrzuca cele (minimalny odstęp √(pole/liczba)/2, zmniejszany, gdy brakuje kandydatów); nagroda 5/10/15 rośnie
  z odległością od środka obszaru. Meta (opcjonalna) kończy rundę uciekających z premią.
- **Geofencing:** telefon uciekających przy każdym odczycie GPS (nagrywanie trasy startuje samo) zdobywa cele bez
  wyzwania w promieniu celu i kończy rundę na mecie. Cel z wyzwaniem tylko wyświetla wyzwanie — zdobycie
  potwierdzają gracze. Ręczne zdobycie z listy celów jest zawsze możliwe. Uproszczenie: geofencing działa, gdy
  gra jest otwarta w aplikacji (także przy zgaszonym ekranie), nie po wyjściu do ekranu startowego.
- **Wyzwania** w JSON (`Challenges/challenges.json`, własne teksty pl/en; własną listę można podać w
  `TagSettings.ChallengesJson`): wyzwania bez miejsca są rozdawane celom (nagroda się sumuje), wyzwania z
  `location` stają się osobnymi celami.
- **Czas i wynik:** runda zaczyna się przewagą (`HeadStart`, goniący czekają; „Złapany” nieaktywne), trwa do
  złapania, mety albo `RoundDuration`. Po upływie czasu rundę zamyka dowolny telefon („Następna runda”), a jej koniec
  liczy się na granicy czasu. Wynik rundy = przetrwane minuty + punkty z celów (tryb „punkty”) + premia za metę.
  Tryb „monety”: nagrody to monety na przejazdy (saldo i wydatki, wiadomość tylko w drużynie), wynik = minuty +
  meta.
- **Pingi:** online automatycznie co `RevealInterval` (koordynator online). Offline uciekający dostają
  przypomnienie i wysyłają pozycję systemowym „Udostępnij” (tekst + link OSM), a goniący wpisują ją dotknięciem
  mapy. Offline każdy telefon zna tylko zdobycze własnej drużyny — tabela wyników mówi o tym wprost.
- Na mapie Tag nie ma przystanków (zasłaniały cele) ani emoji w etykietach (Mapsui ich nie rysuje).
- Powiadomienia systemowe (ping, koniec przewagi) — zrobione w etapie 7.

## Dopracowanie (etap 7)

- **Powiadomienia opisują gry, nie aplikacja.** Moduł gry planuje przypomnienia (`PlanNotifications`: koniec
  ukrywania i 5 min przed nim, koniec przewagi, koniec czasu rundy, pingi offline) i mówi, o czym powiadomić, gdy
  aplikacja jest w tle (`NotificationFor`: pytanie dla ukrywających, odpowiedź i karta dla szukających, zdobyty cel,
  koniec i początek rundy; `NotificationForLocation`: ping uciekających u goniących online). Koordynator
  (`GameNotificationCoordinator`) planuje od nowa po każdej zmianie sesji i przez `NotificationSchedule` wysyła do
  systemu tylko różnice (np. runda skończyła się wcześniej → stare przypomnienia są anulowane).
- **Nowości tylko w tle.** Co dzieje się na ekranie, gra pokazuje sama; powiadomienie pojawia się, gdy aplikacja
  nie jest na pierwszym planie (w tym gra zakończona na innym telefonie). Ping online co 20 s w Hide and Seek nie
  jest powiadamiany (zbyt częsty).
- **Android:** przypomnienia to alarmy `AlarmManager` z odbiornikiem, który pokazuje powiadomienie także przy
  zamkniętej aplikacji. Alarmy dokładne (`USE_EXACT_ALARM` od Androida 13, `SCHEDULE_EXACT_ALARM` na 12) — timery
  są sednem gier; bez uprawnienia wracamy do alarmów niedokładnych (na Androidzie 16 potrafiły się spóźniać o
  kilka minut, a przy dłuższym czasie nawet o godzinę). Uwaga na przyszłość: Google Play wymaga uzasadnienia
  `USE_EXACT_ALARM`; przy publikacji w sklepie trzeba je obronić albo przejść na `SCHEDULE_EXACT_ALARM` z prośbą do
  użytkownika. System kasuje alarmy przy wymuszonym zatrzymaniu, aktualizacji i restarcie telefonu — zapisany
  harmonogram jest planowany ponownie przy starcie aplikacji (po restarcie telefonu dopiero po jej otwarciu).
- **iOS:** `UNUserNotificationCenter` z wyzwalaczem czasowym; delegat pokazuje powiadomienia także przy otwartej
  aplikacji. Nie zweryfikowane na urządzeniu (build w CI).
- Przypomnienia planowane są dla gry na ekranie (bieżącej sesji). Gdy gracz wyjdzie z gry („Wyjdź”), już
  zaplanowane przypomnienia zostają, ale zmiany z drugiego telefonu nie przesuwają ich do czasu powrotu do gry.
- **Historia:** szczegóły gry = wynik z modułu (`DescribeResult`), oś czasu zdarzeń (ikony z `IGameUiModule.EventIcon`)
  i trasa. Sesja zapisuje czas zakończenia (`FinishedAt`), więc wynik nie „rośnie” po końcu gry; starsze gry
  bez tego pola używają czasu ostatniego zapisu. Runda Tag przerwana końcem gry ma opis „przerwana końcem gry”.
  Usunięcie gry z historii kasuje sesję i jej zdarzenia, ale nie trasę (ta ma własną zakładkę).
- **Podgląd trasy** gry pokazuje obszar gry i jej zdarzenia: ostatnie zdarzenie w chwili suwaka oraz przeskoki do
  poprzedniego/następnego. Zdarzenia o tym samym czasie (koniec rundy i start następnej) zachowują kolejność zapisu.
- **Tłumaczenia:** test `TranslationTests` pilnuje, żeby każdy `*.resx` miał angielską parę z tymi samymi kluczami,
  bez pustych tekstów i z tymi samymi `{n}`. Język wybrany w Ustawieniach ustawia też format dat i liczb („System” =
  ustawienia telefonu). Tytuły zdarzeń na osi czasu są zapisywane w języku z chwili gry (to zapis przebiegu), więc
  po zmianie języka stare gry pokazują je w poprzednim języku.
- **Tryb ciemny:** przejrzane ekrany; poprawione przełączniki (włączony był szary) i akcent natywnych okien wyboru
  (`values-night/colors.xml`). Legenda stref ukrycia ma własny kolor `MapHidingZoneLegend` (kafelki mapy są zawsze
  jasne). Własna ikona i ekran startowy zamiast domyślnych „.NET”.

## Tag — przebudowa zasad (jeden uciekinier, monety, karty)

- **Tylko online.** Salda, cele i losowania każdego gracza muszą być identyczne na wszystkich telefonach, a
  przepisywanie ich ręcznie przez komunikator by się rozjeżdżało — decyzja użytkownika: Tag bez trybu offline
  (`IGameModule.RequiresOnline`, przełącznik w kreatorze zablokowany).
- **Stan = wiadomości.** Każdy telefon buduje ten sam stan z tych samych wiadomości `tag.*` (wszystkie „do
  wszystkich”), stosowanych idempotentnie (klucze: numer tury, id przejazdu/karty/transakcji). Losowania (pierwszy
  uciekinier, następny, karta, cele ze stacji) wykonuje telefon wysyłający i wysyła wynik. Źródłem prawdy dla
  tury jest telefon uciekiniera (przejazdy, karty, złapanie, dotarcie, pozycja na koniec), dla gry — host (start,
  korekty, awaryjne złapanie i rozstrzygnięcie).
- **Gracze = urządzenia.** Gracz to urządzenie w grze online (`SyncPeer.DeviceId`); sesja zna swoje
  `LocalDeviceId` i `IsHost` (nowe kolumny w `sessions`). Wszyscy dołączają jako goniący (`InitialRoleId`), skład
  ustala się przy starcie — kto dołączy później, tylko ogląda.
- **Role pojedynczych graczy — protokół 3.** `RoleChange.Devices` (id urządzenia → rola): start robi wylosowanego
  uciekinierem, złapanie zamienia dwie osoby. Serwer przenosi tylko te urządzenia między grupami ról, więc
  prywatność (goniący widzą pingi uciekiniera, nigdy odwrotnie) nadal egzekwuje serwer. Wersja zmiany ról wynika z
  numeru tury (start = 0, złapanie w turze k = k + 1), więc spóźnione drugie potwierdzenie tego samego złapania nie
  zmieni ról drugi raz.
- **Kto potwierdza złapanie.** Telefony stosują własne wiadomości od razu (przed serwerem), więc dwa zgłoszenia naraz
  mogłyby wylosować różnych następców. Dlatego złapanie potwierdza tylko uciekinier (goniący wysyła prośbę), a host
  może je wymusić, gdy telefon uciekiniera milczy. Uproszczenie: jednoczesne wymuszenie przez hosta i potwierdzenie
  przez uciekiniera to jedyny pozostały wyścig — UI ostrzega, by wymuszać tylko przy milczącym telefonie.
- **Pozycja uciekiniera nie trafia do wiadomości** poza miejscem złapania (goniący tam są) i końcem gry (rozstrzyga
  wynik). Zdarzenia z miejscem na telefonie uciekiniera (przejazdy, karty) biorą je z lokalnego GPS i nie są
  wysyłane — u innych te wpisy są bez miejsca.
- **Opłaty.** Stawka za km: pierwszy km z góry (zaliczka, zaokrąglenie w górę), po przejeździe koszt z długości
  trasy GPS między początkiem a końcem przejazdu (min. 1 moneta; nadpłata wraca), dopłata może dać saldo ujemne.
  Brak trasy → minimum. Mnożniki klątw liczą się z chwili rozpoczęcia przejazdu i mnożą się między sobą. Przejazd
  otwarty w chwili złapania kończy się bez dopłaty. Goniący nie płacą, pieszo nic nie kosztuje.
- **Klątwy.** Utrudnienie trwa tyle, co klątwa: `untilEndOfTurn` do złapania, `wholeGame` do końca gry (także w
  kolejnych turach tego gracza jako uciekiniera), `minutes:N` od wylosowania. Egzekwowane: `banTransport`,
  `onlyTransport`, `fareMultiplier`, `noDraw`; bez `restriction` — honorowe. Losowanie preferuje karty jeszcze
  niewylosowane; wyzwanie po limicie czasu przepada i liczy się jak rezygnacja (przerwa w losowaniu).
- **Talia JSON** (`Cards/cards.json`, własne teksty pl/en): pola `type`, `title`, `description`, `reward`,
  `timeLimitMinutes`, `duration`, `restriction`; tekst może być też zwykłym stringiem. Lista wyzwań z pierwszej
  wersji (`{"challenges": [...]}`) wczytuje się jako karty Challenge (miejsca są pomijane).
- **Koniec po czasie.** Gra kończy się na granicy czasu (nawet gdy wiadomość przyszła później). Pozycję wysyła
  telefon uciekiniera sam; gdy milczy, host rozstrzyga wg ostatniego pingu albo punktu na mapie (źródło zapisane
  i pokazane w wynikach). Odległość geodezyjna, porównywana w pełnych metrach — równe odległości = remis.
- **Cele jawne / ukryte.** „Ukryte” to zasada wyświetlania: dane celów są w wiadomościach dla wszystkich (jak inne
  dane gry między znajomymi), aplikacja pokazuje cudze dopiero na koniec.
- **Stare gry.** Migracja `tag.v2-rules` (tabela `schema_migrations`, uruchamiana raz przez magazyn sesji)
  przelicza wynik starej gry starym sposobem, zapisuje go w stanie i zamyka niezakończone gry; ustawienia (obszar,
  stacje) zostają, więc historia pokazuje mapę. Wiadomości do takiej gry są ignorowane.
- **Eksport.** Punkty zdarzeń z miejscem jak dotąd; odcinki trasy dzielą się tam, gdzie zdarzenie niesie
  `GameEventData.LocalRole` (tury Tag) — GPX: `tg:role` w `extensions` segmentu (zgodne z XSD GPX 1.1), GeoJSON:
  osobny obiekt na okres roli.
- **Uproszczenia:** brak powiadomienia o prośbie o złapanie, gdy aplikacja uciekiniera jest zamknięta dłużej niż
  pozwala system (powiadomienie przychodzi, gdy wiadomość dotrze); geofencing celu działa, gdy gra jest otwarta
  (także przy zgaszonym ekranie).

## Build

- **TFM-y MAUI** ustawiane centralnie (`MauiTargetFrameworks` w `Directory.Build.props`): Android zawsze,
  iOS na macOS albo z `-p:EnableIos=true`. Windows/MacCatalyst usunięte (nie są celem projektu).
  iOS weryfikowany w CI (`macos-latest`, build bez podpisu na symulator).
- **Central Package Management** + transitive pinning. SQLite: `sqlite-net-pcl` 1.11 sam dostarcza
  SQLitePCLRaw 3.x i natywną bibliotekę (`SourceGear.sqlite3` 3.53); dodatkowy `SQLitePCLRaw.bundle_green` 2.1.x
  z etapu 1 był zbędny, dublował `libe_sqlite3.so` w APK i wnosił podatną wersję (GHSA-2m69-gcr7-jv3q) — usunięty
  w etapie 2.
- **Silnie typowane zasoby bez plików Designer**: `Directory.Build.targets` generuje klasy z `Resources/*.resx`
  przy buildzie. Kultury inne niż `en` trzeba dopisać w tym pliku.
- `Microsoft.Maui.Controls` przypięty jawnie (10.0.110) w `Directory.Packages.props`.

## UI

- Język wybrany w Ustawieniach działa po restarcie aplikacji (zasoby czytane przez `x:Static` przy tworzeniu stron).
  Zasoby neutralne = polski, więc urządzenie w innym języku niż angielski dostaje polski.
- Trasy Shell gier bez ukośników (`hideandseek-game`), żeby uniknąć niejednoznaczności routingu kontekstowego.
