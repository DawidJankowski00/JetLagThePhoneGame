using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TransitGames.Games.Tag.Maui.Services;
using TransitGames.Games.Tag.Resources;
using TransitGames.Games.Tag.Targets;
using TransitGames.UI.Services;

namespace TransitGames.Games.Tag.Maui.ViewModels;

public sealed record TargetItem(
    TagTarget Target,
    string RewardText,
    string DistanceText,
    string StatusText,
    bool IsCaptured,
    bool CanCapture,
    bool IsFinal)
{
    public string Name => IsFinal ? "🏁 " + Target.Name : Target.Name;

    public string? ChallengeTitle => Target.Challenge?.LocalizedTitle;

    public string? ChallengeText => Target.Challenge?.LocalizedText;

    public bool HasChallenge => Target.Challenge is not null;
}

/// <summary>Targets of the current round: reward, distance, challenge and status; runners capture them here by hand.</summary>
public sealed partial class TagTargetsViewModel : ObservableObject
{
    private readonly TagController _game;
    private readonly IDialogService _dialogs;
    private TagSession? _observed;

    public TagTargetsViewModel(TagController game, IDialogService dialogs)
    {
        _game = game;
        _dialogs = dialogs;
    }

    public ObservableCollection<TargetItem> Targets { get; } = [];

    [ObservableProperty]
    public partial string HeaderText { get; set; } = string.Empty;

    public void OnAppearing()
    {
        Observe(_game.Session);
        Load();
    }

    public void OnDisappearing() => Observe(null);

    private void Observe(TagSession? session)
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
        Targets.Clear();
        if (_game.Session is not { } session)
        {
            return;
        }

        var now = _game.Now;
        var here = _game.LastLocation;
        var kind = session.Settings.RewardKind;
        var items = session.Settings.Targets.Select(t =>
        {
            var capture = session.CurrentRound.Captures.FirstOrDefault(c => c.TargetId == t.Id);
            return new TargetItem(
                t,
                TagTexts.Reward(t.Reward, kind),
                here is { } p ? TagTexts.Distance(t.Location.DistanceTo(p)) : string.Empty,
                capture is null ? TagStrings.TargetActive : string.Format(TagStrings.TargetCapturedFormat, capture.At.ToLocalTime()),
                capture is not null,
                session.CanCapture(t.Id, now),
                false);
        })
        .OrderBy(i => i.IsCaptured)
        .ThenBy(i => here is { } p ? i.Target.Location.DistanceTo(p) : 0)
        .ToList();

        if (session.Settings.FinalTarget is { } final)
        {
            items.Insert(0, new TargetItem(
                final,
                TagTexts.Reward(session.Settings.FinalBonus, RewardKind.Points),
                here is { } p ? TagTexts.Distance(final.Location.DistanceTo(p)) : string.Empty,
                TagStrings.FinalTargetStatus,
                false,
                false,
                true));
        }

        foreach (var item in items)
        {
            Targets.Add(item);
        }

        HeaderText = string.Format(
            session.IsRunner ? TagStrings.TargetsHeaderRunnerFormat : TagStrings.TargetsHeaderChaserFormat,
            session.CurrentRound.Captures.Count,
            session.Settings.Targets.Count,
            TagTexts.Distance(session.Settings.TargetRadiusMeters));
    }

    [RelayCommand]
    private async Task CaptureAsync(TargetItem item)
    {
        if (!item.CanCapture)
        {
            return;
        }

        var text = item.Target.Challenge is { } challenge
            ? string.Format(TagStrings.CaptureConfirmChallengeFormat, item.Target.Name, challenge.LocalizedTitle, challenge.LocalizedText)
            : string.Format(TagStrings.CaptureConfirmFormat, item.Target.Name);
        if (await _dialogs.ConfirmAsync(TagStrings.CaptureTitle, text))
        {
            await _game.CaptureAsync(item.Target.Id);
        }
    }
}
