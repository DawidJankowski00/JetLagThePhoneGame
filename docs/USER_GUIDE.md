# Transit Games — instrukcja dla graczy

Transit Games to gry terenowe po mieście z użyciem komunikacji miejskiej, inspirowane serią „Jet Lag: The Game”.
Każda drużyna gra na jednym telefonie (albo na kilku, jeśli gracie online). Aplikacja pilnuje czasu, mapy,
pytań, celów i wyników — zasady fair play zostają po stronie graczy.

## Zanim zaczniecie

- **Lokalizacja:** zezwól aplikacji na dostęp do lokalizacji. Trasa GPS nagrywa się także przy wygaszonym ekranie
  (na Androidzie widać wtedy stałe powiadomienie „Nagrywanie trasy GPS”).
- **Powiadomienia:** przy pierwszym wejściu do gry aplikacja poprosi o zgodę. Dzięki niej dostaniesz przypomnienia
  (koniec czasu ukrycia, koniec przewagi, czas na ping) i nowości od drugiej drużyny, gdy telefon jest w kieszeni.
  Można je wyłączyć w *Ustawienia → Powiadomienia z gry*.
- **Gra online (opcjonalnie):** w *Ustawienia → Gra online* wpisz adres serwera i swoją nazwę, włącz VPN do serwera
  i naciśnij „Sprawdź połączenie”. Bez serwera gra działa offline — drużyny przekazują sobie informacje same
  (np. komunikatorem), a aplikacja pomaga je wpisać.
- **Bateria:** nagrywanie trasy zużywa baterię; w *Ustawienia → Nagrywanie trasy GPS* jest tryb oszczędzania.
  Weźcie powerbank.

## Nowa gra

1. Na ekranie *Gry* wybierz grę i **swoją rolę**.
2. **Obszar gry:** narysuj go na mapie, dotknij miasta lub dzielnicy (granica z OpenStreetMap), wczytaj plik GeoJSON
   albo wybierz zapisany obszar.
3. **Stacje:** wybierz rodzaje (kolej, metro, tramwaj, autobus) i naciśnij „Pobierz”. Stację wyłączasz dotknięciem na
   mapie lub na liście; możesz dodać własne. Stacje zapisują się w telefonie, więc później działają bez internetu.
4. **Ustawienia gry** (np. czas ukrycia, rundy, cele) i przełącznik **„Graj online”**.
5. „Rozpocznij grę”. W grze online pojawi się **kod gry** — druga drużyna na ekranie startowym wybiera
   „Dołącz do gry online”, wpisuje kod i swoją rolę.

Gra zapisuje się na bieżąco. Jeśli telefon zamknie aplikację, po jej otwarciu wrócisz do gry. „Wyjdź” zostawia grę
otwartą (trwa dalej w świecie), „Zakończ” kończy ją — w grze online na wszystkich telefonach.

## Chowany (Hide and Seek)

**Ukrywający** mają czas na dojazd i ukrycie się w strefie wokół wybranej stacji. **Szukający** zadają pytania,
a aplikacja na ich podstawie zaciemnia na mapie miejsca, w których ukrywających być nie może.

- **Ukrywający:** wybierzcie stację (z mapy albo losowo) przed końcem czasu ukrycia — 5 minut wcześniej przyjdzie
  przypomnienie. Na pytania odpowiadacie w aplikacji; podpowiedź z GPS mówi, jaka jest prawdziwa odpowiedź.
  Za odpowiedzi dobieracie karty (premie czasowe, klątwy, weto). Weto zamiast odpowiedzi też jest kartą.
- **Szukający:** „Zadaj pytanie” → rodzaj pytania (radar, termometr, dopasowanie, mierzenie, macki, zdjęcie). Pozycję
  bierzemy z GPS; można ją poprawić dotknięciem mapy. Po odpowiedzi mapa się zawęża, a licznik pokazuje, ile stacji
  zostało. Odpowiedź można poprawić, a pytanie usunąć — mapa przelicza się sama.
- Gdy znajdziecie ukrywających: „Znalezieni”. Wynik ukrywających to czas od końca ukrywania do znalezienia
  plus premie z kart.
- **Offline:** szukający czytają odpowiedź drugiej drużyny i wpisują ją; ukrywający dodają otrzymane pytanie przez
  „Dodaj otrzymane pytanie”.
- **Prywatność:** ukrywający widzą pozycję szukających (online), szukający nigdy nie widzą ukrywających.

## Berek (Tag)

Tylko online — każdy gracz potrzebuje telefonu z aplikacją. Zasady: [README](../README.md#berek-tag--zasady).

1. **Host:** Gry → Berek → (rola „Goniący” jest ustawiona) → obszar → stacje → ustawienia → „Rozpocznij grę”.
   Podaj innym kod gry.
2. **Pozostali:** „Dołącz do gry online” → kod → „Dołącz jako: Goniący”.
3. **Cele:** „🎯 Mój cel” → dotknij mapy (przy stacji wybiera się stacja) → „Zatwierdź”. W trybie „host ustawia”
   host używa „🎯 Cele graczy”; w trybie losowania cele pojawią się po starcie.
4. **Start (host):** „🚦 Start” → punkt startowy z GPS albo z mapy → potwierdź. Aplikacja losuje uciekiniera.
5. **Uciekinier:** „🚌 Przejazd” (wybór środka transportu z ceną; 🚫 = zakazany przez klątwę) i „🛑 Koniec
   przejazdu”; „🃏 Losuj kartę”, przy wyzwaniu „✅ Wyzwanie wykonane” / „✖️ Rezygnuję”; aktywne klątwy są na liście
   z czasem wygaśnięcia. Przy celu aplikacja sama zapyta „Jesteś na miejscu?”. Złapany naciska „🙌 Złapany” i
   wybiera, kto złapał.
6. **Goniący:** po przewadze „✋ Złapałem!” wysyła prośbę do uciekiniera. Host ma „⚠️ Wymuś złapanie”,
   „🪙 Korekta monet” i — gdy po czasie gry telefon uciekiniera milczy — „📍 Wg ostatniego pingu” / „🗺️ Zaznacz na
   mapie”.
7. **Wyniki** („🏆 Wyniki”): zwycięzca, ranking celów, tury, salda i twoja historia monet.

## Historia gier

Zakładka *Historia* pokazuje rozegrane gry z wynikiem. Po dotknięciu gry zobaczysz:

- wynik (np. drużyny i rundy w Berku, czas ukrycia w Chowanym),
- **przebieg gry** — oś czasu: pytania i odpowiedzi, karty, zdobyte cele, pingi, złapania, początek i koniec gry,
- nagraną **trasę** — „Podgląd” otwiera ją na mapie.

Grę zakończoną można usunąć z historii (trasa zostaje w zakładce *Trasy GPS*).

## Trasy GPS

Każda gra nagrywa trasę telefonu; można też nagrać trasę ręcznie („Nagrywaj trasę”). W zakładce *Trasy GPS*:

- **Podgląd:** trasa na mapie z suwakiem czasu i odtwarzaniem. Trasa gry pokazuje też obszar gry i jej zdarzenia —
  przyciski „◀ Zdarzenie / Zdarzenie ▶” przeskakują między nimi.
- **Udostępnianie:** GPX (np. do Stravy, OsmAnd, Google Earth) lub GeoJSON (np. geojson.io, QGIS). Zdarzenia gry
  z miejscem trafiają do pliku jako punkty.

## Ustawienia

- **Język:** systemowy, polski lub angielski (zmiana działa po ponownym uruchomieniu aplikacji).
- **Motyw:** systemowy, jasny lub ciemny.
- **Powiadomienia z gry**, **Gra online**, **Nagrywanie trasy GPS** — opisane wyżej.

## Gdy coś nie działa

- **Brak pozycji / trasa się nie nagrywa:** sprawdź zgodę na lokalizację i czy GPS jest włączony.
- **Nie przychodzą powiadomienia:** sprawdź zgodę na powiadomienia w ustawieniach systemu i przełącznik w aplikacji.
  Po restarcie telefonu otwórz aplikację raz, żeby przypomnienia zaplanowały się ponownie.
- **„Brak połączenia” w grze online:** włącz VPN do serwera. Wiadomości wysłane bez połączenia czekają w kolejce
  i dotrą, gdy połączenie wróci.
- **Brak stacji po pobraniu:** Overpass (serwer danych OSM) bywa przeciążony — spróbuj ponownie za chwilę.
