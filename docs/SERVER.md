# Serwer gry online

`TransitGames.Server` to ASP.NET Core + SignalR. Serwer przechowuje gry (lobby z kodem), urządzenia i wiadomości
w SQLite i przekazuje je między telefonami. Nie zna reguł gier: zna tylko role i zasady udostępniania lokalizacji
przysłane przez telefon, który założył grę, i sam je egzekwuje.

## Uruchomienie (Docker)

Na serwerze prywatnym, w katalogu repozytorium:

```bash
cd deploy
docker compose up -d --build
```

- Port: `5080` (zmienna `PORT`).
- Żeby serwer nasłuchiwał **tylko na interfejsie VPN**, ustaw `BIND_ADDRESS` na adres serwera w VPN, np. w pliku
  `deploy/.env`:

  ```
  BIND_ADDRESS=10.8.0.1
  PORT=5080
  RETENTION_DAYS=14
  ```

- Dane: wolumen `transitgames-data` (`/data/transitgames-server.db`). Gry przetrwają restart i aktualizację
  kontenera. Gry nieużywane dłużej niż `RETENTION_DAYS` dni są usuwane.
- Aktualizacja: `git pull` i ponownie `docker compose up -d --build`.
- Logi: `docker compose logs -f`.

Kontrola działania:

```bash
curl http://10.8.0.1:5080/api/info
```

```bash
curl http://10.8.0.1:5080/health
```

## Telefony

1. Włącz VPN na telefonie.
2. **Ustawienia → Gra online**: adres serwera (np. `10.8.0.1:5080`) i nazwa gracza → „Sprawdź połączenie”.
3. Drużyna zakładająca grę: kreator nowej gry → na kroku stacji włączony przełącznik „Graj online” → „Rozpocznij
   grę”. Aplikacja pokazuje **kod gry** (6 znaków; w nagłówku gry, dotknięcie udostępnia go).
4. Druga drużyna: ekran startowy → „Dołącz do gry online” → kod → wybór roli. Obszar gry, stacje i ustawienia
   przychodzą z serwera.

Każda drużyna może mieć kilka telefonów (każdy dołącza kodem z tą samą rolą).

## Bezpieczeństwo

- Granicą bezpieczeństwa jest VPN: serwer używa zwykłego HTTP (aplikacja pozwala na to: Android
  `usesCleartextTraffic`, iOS `NSAllowsArbitraryLoads`). Nie wystawiaj portu do internetu bez reverse proxy z TLS.
- Każde urządzenie dostaje przy dołączeniu losowy token (serwer trzyma tylko jego SHA-256). Połączenie SignalR
  jest uwierzytelniane tokenem; rola i nadawca wiadomości są brane z tokenu, nie z treści wiadomości.
- Wiadomości typu `location` trafiają tylko do własnej drużyny i ról dozwolonych przez `LocationSharingPolicy`
  gry (Hide and Seek: ukrywający widzą szukających; Tag: goniący widzą uciekających), niezależnie od tego, o co
  prosi klient.

## Protokół (wersja 2)

REST:
- `GET /api/info` → `ServerInfo` (nazwa, wersja, wersja protokołu)
- `POST /api/lobbies` (`CreateLobbyRequest`) → `LobbyTicket`
- `GET /api/lobbies/{code}` → `LobbyInfo`
- `POST /api/lobbies/{code}/join` (`JoinLobbyRequest`) → `JoinLobbyResult` (bilet + ustawienia gry)

SignalR `/hubs/game` (token w `access_token` lub nagłówku `Authorization: Bearer`):
- `Publish(SyncMessage)` → numer sekwencyjny; idempotentne (ponowienie tej samej wiadomości nie dubluje jej)
- `Sync(afterSequence)` → wiadomości przegapione od danego numeru
- `LatestLocations()` → ostatnie pozycje widocznych urządzeń
- serwer → klient: `Receive(SequencedMessage)`, `Peers(SyncPeer[])`
- zmiana ról: wiadomość z polem `roleChange` (`fromVersion` + mapowanie ról) przestawia role wszystkich urządzeń
  gry, jeśli `fromVersion` jest aktualna, mapowanie jest permutacją ról, a wiadomość jest do wszystkich; w innym
  wypadku pole jest usuwane. Widoczność wiadomości liczy się wg roli urządzenia w wersji z chwili wysłania.

Kontrakty: `src/TransitGames.Core/Sync/OnlineContracts.cs`.
