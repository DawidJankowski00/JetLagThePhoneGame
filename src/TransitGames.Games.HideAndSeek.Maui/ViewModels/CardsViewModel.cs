using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Games.HideAndSeek.Cards;
using TransitGames.Games.HideAndSeek.Maui.Services;
using TransitGames.Games.HideAndSeek.Resources;
using TransitGames.UI.Services;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

public sealed record CardItem(int HandIndex, CardDefinition Card)
{
    public string Title => Card.LocalizedTitle;

    public string Text => Card.LocalizedText;

    public string KindText => Card.Kind switch
    {
        CardKind.TimeBonus => HideAndSeekStrings.CardKindBonus,
        CardKind.Curse => HideAndSeekStrings.CardKindCurse,
        _ => HideAndSeekStrings.CardKindPowerUp,
    };

    public bool IsVeto => Card.Effect == CardEffects.Veto;
}

/// <summary>Hiders' cards: choose earned cards, play cards from the hand (curses and bonuses go to the seekers).</summary>
public sealed partial class CardsViewModel : ObservableObject
{
    private readonly HideAndSeekController _game;
    private readonly IDialogService _dialogs;
    private HideAndSeekSession? _observed;

    public CardsViewModel(HideAndSeekController game, IDialogService dialogs)
    {
        _game = game;
        _dialogs = dialogs;
    }

    public ObservableCollection<CardItem> Hand { get; } = [];

    [ObservableProperty]
    public partial string DeckText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDraws))]
    public partial string PendingText { get; set; } = string.Empty;

    public bool HasPendingDraws => !string.IsNullOrEmpty(PendingText);

    [ObservableProperty]
    public partial bool IsHandEmpty { get; set; }

    public void OnAppearing()
    {
        Observe(_game.Session);
        Load();
    }

    public void OnDisappearing() => Observe(null);

    private void Observe(HideAndSeekSession? session)
    {
        if (_observed is not null)
        {
            _observed.StateChanged -= OnStateChanged;
        }

        _observed = session;
        if (session is not null)
        {
            session.StateChanged += OnStateChanged;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(Load);

    private void Load()
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        Hand.Clear();
        for (var i = 0; i < session.Hand.Count; i++)
        {
            if (session.Deck.Find(session.Hand[i]) is { } card)
            {
                Hand.Add(new CardItem(i, card));
            }
        }

        IsHandEmpty = Hand.Count == 0;
        DeckText = string.Format(HideAndSeekStrings.DeckLeftFormat, session.CardsLeftInDeck);
        var pending = session.PendingDraws.Sum(d => d.Keep);
        PendingText = pending > 0 ? string.Format(HideAndSeekStrings.PendingCardsFormat, pending) : string.Empty;
    }

    [RelayCommand]
    private async Task ChoosePendingAsync()
    {
        if (_game.Session is { } session)
        {
            await CardPicker.ChooseEarnedCardsAsync(session, _dialogs);
        }
    }

    [RelayCommand]
    private async Task PlayAsync(CardItem item)
    {
        if (item.IsVeto)
        {
            await _dialogs.AlertAsync(item.Title, HideAndSeekStrings.VetoHowTo);
            return;
        }

        var message = item.Card.Kind == CardKind.Curse ? $"{item.Text}\n\n{HideAndSeekStrings.CurseWarning}" : item.Text;
        if (await _dialogs.ConfirmAsync(item.Title, message, HideAndSeekStrings.PlayCard))
        {
            await _game.PlayCardAsync(item.HandIndex);
        }
    }
}
