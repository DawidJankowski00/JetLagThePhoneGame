using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Games.HideAndSeek.Cards;
using TransitGames.Games.HideAndSeek.Maui.Services;
using TransitGames.Games.HideAndSeek.Questions;
using TransitGames.Games.HideAndSeek.Resources;
using TransitGames.UI.Services;

namespace TransitGames.Games.HideAndSeek.Maui.ViewModels;

public sealed record QuestionItem(AskedQuestion Question, string Title, string Text, string AnswerText, string TimeText, string? Hint)
{
    public bool IsPending => !Question.IsAnswered;

    public bool HasHint => Hint is not null;
}

public sealed record PlayedCardItem(string Title, string Text, string TimeText);

/// <summary>
/// Question history. Hiders answer here (with a hint computed from their GPS; they confirm by hand), possibly with a
/// veto, then choose the cards they earned. Seekers can enter or correct answers (offline play) and remove questions;
/// the map is recomputed from the list.
/// </summary>
public sealed partial class QuestionsViewModel : ObservableObject
{
    private readonly HideAndSeekController _game;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private HideAndSeekSession? _observed;

    public QuestionsViewModel(HideAndSeekController game, IDialogService dialogs, INavigationService navigation)
    {
        _game = game;
        _dialogs = dialogs;
        _navigation = navigation;
    }

    public ObservableCollection<QuestionItem> Items { get; } = [];

    public ObservableCollection<PlayedCardItem> PlayedCards { get; } = [];

    [ObservableProperty]
    public partial bool IsHider { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasPlayedCards { get; set; }

    [ObservableProperty]
    public partial bool CanAddReceived { get; set; }

    public async Task OnAppearingAsync()
    {
        if (_observed is not null)
        {
            _observed.StateChanged -= OnStateChanged;
        }

        _observed = _game.Session;
        if (_observed is not null)
        {
            _observed.StateChanged += OnStateChanged;
        }

        await LoadAsync();
    }

    public void OnDisappearing()
    {
        if (_observed is not null)
        {
            _observed.StateChanged -= OnStateChanged;
            _observed = null;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());

    private async Task LoadAsync()
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        IsHider = session.IsHider;
        CanAddReceived = session.IsHider && !_game.IsOnline;
        var hider = session.IsHider && session.Questions.Any(q => !q.IsAnswered) ? await _game.HiderLocationAsync() : null;
        var projection = _game.Area?.Projection;

        Items.Clear();
        foreach (var question in session.Questions.OrderByDescending(q => q.AskedAt))
        {
            string? hint = null;
            if (hider is { } position && projection is not null && !question.IsAnswered)
            {
                var suggested = QuestionFactory.Create(question.Spec, projection).SuggestAnswer(position);
                hint = string.Format(HideAndSeekStrings.HintFormat, QuestionTexts.DescribeAnswer(question.Spec, suggested));
            }

            var time = question.AskedAt.ToLocalTime().ToString("HH:mm");
            if (question.AnsweredAt is { } answeredAt)
            {
                time += " → " + answeredAt.ToLocalTime().ToString("HH:mm");
            }

            Items.Add(new QuestionItem(
                question,
                QuestionTexts.Title(question.Spec),
                QuestionTexts.Describe(question.Spec),
                question.Vetoed ? HideAndSeekStrings.AnswerVetoed : QuestionTexts.DescribeAnswer(question.Spec, question.Answer),
                time,
                hint));
        }

        PlayedCards.Clear();
        foreach (var play in session.CardsPlayed.OrderByDescending(p => p.PlayedAt))
        {
            var card = session.Deck.Find(play.CardId);
            PlayedCards.Add(new PlayedCardItem(card?.LocalizedTitle ?? play.CardId, card?.LocalizedText ?? string.Empty, play.PlayedAt.ToLocalTime().ToString("HH:mm")));
        }

        IsEmpty = Items.Count == 0;
        HasPlayedCards = PlayedCards.Count > 0;
    }

    [RelayCommand]
    private async Task OpenAsync(QuestionItem item)
    {
        if (_game.Session is not { } session)
        {
            return;
        }

        if (session.IsHider)
        {
            await AnswerAsHidersAsync(session, item);
        }
        else
        {
            await CorrectAsSeekersAsync(item);
        }
    }

    private async Task AnswerAsHidersAsync(HideAndSeekSession session, QuestionItem item)
    {
        if (item.Question.IsAnswered)
        {
            return;
        }

        var options = QuestionTexts.Options(item.Question.Spec);
        var labels = options.Select(o => o.Label).ToList();
        if (session.HasVeto)
        {
            labels.Add(HideAndSeekStrings.AnswerWithVeto);
        }

        var message = item.Hint is null ? item.Text : $"{item.Text}\n{item.Hint}";
        var choice = await _dialogs.ChooseAsync(message, labels);
        if (choice is null)
        {
            return;
        }

        if (choice == HideAndSeekStrings.AnswerWithVeto)
        {
            await _game.AnswerAsync(item.Question.Id, null, veto: true);
            return;
        }

        var answer = options.First(o => o.Label == choice).Answer;
        if (item.Question.Spec is PhotoSpec)
        {
            answer = new QuestionAnswer(Text: await _dialogs.PromptAsync(item.Title, HideAndSeekStrings.PhotoAnswerPrompt) ?? HideAndSeekStrings.AnswerPhotoSent);
        }

        await _game.AnswerAsync(item.Question.Id, answer);
        await CardPicker.ChooseEarnedCardsAsync(session, _dialogs);
    }

    private async Task CorrectAsSeekersAsync(QuestionItem item)
    {
        var options = QuestionTexts.Options(item.Question.Spec);
        var labels = options.Select(o => string.Format(HideAndSeekStrings.EnterAnswerFormat, o.Label)).ToList();
        labels.Add(HideAndSeekStrings.RemoveQuestion);
        var choice = await _dialogs.ChooseAsync(item.Text, labels);
        if (choice is null)
        {
            return;
        }

        if (choice == HideAndSeekStrings.RemoveQuestion)
        {
            if (await _dialogs.ConfirmAsync(item.Title, HideAndSeekStrings.RemoveQuestionConfirm))
            {
                await _game.RemoveAsync(item.Question.Id);
            }

            return;
        }

        var answer = options[labels.IndexOf(choice)].Answer;
        await _game.AnswerAsync(item.Question.Id, answer, correction: true);
    }

    /// <summary>Offline hiders: enter a question received by message to get the answer hint and the cards.</summary>
    [RelayCommand]
    private Task AddReceivedAsync() => _navigation.GoToAsync(HideAndSeekUiModule.AskRoute);
}

/// <summary>Lets the hiders choose which of the drawn cards to keep.</summary>
internal static class CardPicker
{
    public static async Task ChooseEarnedCardsAsync(HideAndSeekSession session, IDialogService dialogs)
    {
        foreach (var draw in session.PendingDraws.ToList())
        {
            var remaining = Enumerable.Range(0, draw.Cards.Count).ToList();
            var kept = new List<int>();
            while (kept.Count < draw.Keep)
            {
                var labels = remaining.Select(i => Describe(session.Deck, draw.Cards[i])).ToList();
                var title = string.Format(HideAndSeekStrings.KeepCardsFormat, draw.Keep - kept.Count, draw.Cards.Count);
                var choice = await dialogs.ChooseAsync(title, labels, HideAndSeekStrings.Later);
                if (choice is null)
                {
                    return; // choose later on the cards screen
                }

                var index = remaining[labels.IndexOf(choice)];
                kept.Add(index);
                remaining.Remove(index);
            }

            session.KeepCards(draw.QuestionId, kept);
        }
    }

    public static string Describe(CardDeck deck, string cardId) =>
        deck.Find(cardId) is { } card ? $"{card.LocalizedTitle}: {card.LocalizedText}" : cardId;
}
