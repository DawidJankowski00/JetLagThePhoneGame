# Dodawanie nowej gry

Gra składa się z dwóch projektów:

| Projekt | TFM | Zawiera | Zależy od |
|---------|-----|---------|-----------|
| `TransitGames.Games.X` | `net10.0` | logika, ustawienia, stan sesji, zasoby tekstowe | Core, Geo |
| `TransitGames.Games.X.Maui` | `$(MauiTargetFrameworks)` | strony XAML, ViewModele, trasy Shell | UI, `TransitGames.Games.X` |

Logika nie może zależeć od MAUI — dzięki temu testy xUnit działają na zwykłym `net10.0` (także w CI na Linuksie).

## 1. Logika (`TransitGames.Games.X`)

```csharp
public sealed class XSettings : GameSettings        // serializowane System.Text.Json
{
    public TimeSpan RoundDuration { get; set; } = TimeSpan.FromHours(1);
}

public sealed class XState                          // cały zmienny stan sesji, serializowany
{
    public int Score { get; set; }
}

public sealed class XSession : GameSessionBase<XSettings, XState>
{
    public XSession(Guid id, XSettings s, string role) : base(id, XModule.GameId, s, role) { }
    public XSession(GameSessionSnapshot snap, XSettings s) : base(snap, s) { }

    // Każda zmiana stanu przez Mutate(...) → automatyczny zapis do SQLite.
    public void Score() => Mutate(st => st.Score++);
}

public sealed class XModule : GameModule<XSettings, XSession>
{
    public const string GameId = "x";               // nie zmieniać po wydaniu (jest w zapisanych sesjach)
    public override string Id => GameId;
    public override string DisplayName => XStrings.GameName;
    public override string Description => XStrings.GameDescription;
    public override string Icon => "🎲";
    public override int MinPlayers => 2;
    public override IReadOnlyList<GameRole> Roles { get; } = [ new("a", "...", "..."), new("b", "...", "...") ];

    // Kto widzi czyją lokalizację (jednokierunkowo). Domyślnie: nikt nikogo.
    public override LocationSharingPolicy LocationSharing { get; } = new([new LocationSharingRule("a", "b")]);

    // Online: jak często rola, której pozycję widzą inni, ją wysyła (domyślnie ~na żywo, co 20 s).
    // protected override TimeSpan GetLocationShareInterval(XSettings s) => s.PingInterval;

    // sessionId: w grze online wspólny dla wszystkich telefonów.
    protected override XSession CreateSession(XSettings s, string role, Guid sessionId) => new(sessionId, s, role);
    protected override XSession RestoreSession(GameSessionSnapshot snap, XSettings s) => new(snap, s);

    // Wiadomości gry między telefonami: handler stosuje je do sesji (idempotentnie).
    public override void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<IGameMessageHandler, XMessageHandler>();
}
```

Teksty: `Resources/XStrings.resx` (polski) + `Resources/XStrings.en.resx`. Klasa `XStrings` generuje się sama
(`Directory.Build.targets`). Do dopisywania kluczy: `python tools/resx.py src/.../Resources/XStrings keys.json`.

Zdarzenia na osi czasu: w sesji wywołaj `Raise(GameEvent.Create(...))` — trafią do `IGameEventLog`
i (jeśli mają lokalizację) do eksportu GPX/GeoJSON jako waypointy.

Ustawienia gry w kreatorze: nadpisz `CreateSettingsView(GameSettings)` w `XUiModule` i zwróć widok, który
edytuje obiekt ustawień w miejscu (przykład: `HideAndSeekSettingsView`).

Komunikacja między telefonami: wstrzyknij `IGameSyncService` i publikuj `SyncMessage` z własnym `Type`
(np. `"x.question"`) i ładunkiem JSON. Ta sama usługa działa offline (wiadomość wraca lokalnie) i online (serwer
przekazuje ją drużynom zgodnie z `Audience`). Własne wiadomości też przychodzą przez `MessageReceived`, więc stan
gry zmieniaj w jednym miejscu — w `IGameMessageHandler.Handle` (idempotentnie). Dyspozytor wywołuje go także dla
sesji, która nie jest na ekranie. Przykład kompletnej gry: `TransitGames.Games.HideAndSeek` (`Sync/HideAndSeekMessages.cs`).
Pozycji nie wysyłaj samodzielnie: robi to aplikacja zgodnie z `LocationSharing` i `GetLocationShareInterval`,
a pozycje innych graczy są w `Header.Players`.

## 2. UI (`TransitGames.Games.X.Maui`)

```csharp
public sealed class XUiModule : GameUiModule<XModule>
{
    public const string MainRoute = "x-game";       // bez ukośników
    public override string GameId => XModule.GameId;
    public override IReadOnlyList<GameRoute> Routes { get; } = [ new(MainRoute, typeof(XGamePage)) ];
    public override string GetGameRoute(string roleId) => MainRoute;

    protected override void RegisterUiServices(IServiceCollection services)
        => services.AddTransient<XGameViewModel>();
}
```

Strony są rozwiązywane z DI (rejestrowane automatycznie na podstawie `Routes`). Do dyspozycji:
`ActiveSessionViewModel` + `SessionHeaderView` (nagłówek gry, a także `Area` i `Stations` bieżącej sesji),
`GameMapView` (mapa z obszarem, stacjami, własną pozycją `Header.OwnLocation`, pozycjami innych graczy `Header.Players`, trasą `Track` i punktami `Waypoints`), `IMapLayerProvider` (własne warstwy:
`CreatePolygonLayer`/`CreatePointLayer` + `SetPolygon`/`SetPoints`, dodawane przez `GameMapView.AddLayer`),
`IDialogService`, `INavigationService`, style `BigButton`, `Card`, `ToolButton`, kolory warstw mapy (`Map*`).

```xml
<controls:GameMapView x:Name="Map" Area="{Binding Header.Area}" Stations="{Binding Header.Stations}"
                      OwnLocation="{Binding Header.OwnLocation}" Players="{Binding Header.Players}" />
```

Obszar gry i stacje wybiera wspólny kreator (rola → obszar → stacje) — trafiają do `GameSettings.Area`
i `GameSettings.Stations`. Obliczenia geometrii rób w metrach: `GameArea.FromDefinition(settings.Area)`
daje `LocalGeometry` i `Projection` (TransitGames.Geo).

## 3. Rejestracja

W `src/TransitGames.App/TransitGames.App.csproj` dodaj `ProjectReference` do `TransitGames.Games.X.Maui`,
a w `MauiProgram.cs`:

```csharp
services.AddGame<XUiModule>();
```

Ekran startowy, kreator nowej gry, historia i wznawianie sesji po zabiciu aplikacji działają automatycznie.

## 4. Testy

`tests/TransitGames.Games.X.Tests` (xUnit, `net10.0`) + wpis w `TransitGames.Libraries.slnf`.
