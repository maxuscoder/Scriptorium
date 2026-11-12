using System.Globalization;
using System.Windows;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views;

public partial class SaveMomentDialog : Window
{
    private readonly VideoPlayerViewModel _player;
    private readonly Guid _mediaItemId;
    private readonly long _durationSeconds;
    private bool _saving;
    public SavedMoment? MomentToReplay { get; private set; }

    public SaveMomentDialog(VideoPlayerViewModel player, (Guid MediaItemId, long PositionSeconds, long DurationSeconds) snapshot)
    {
        _player = player;
        _mediaItemId = snapshot.MediaItemId;
        _durationSeconds = snapshot.DurationSeconds;
        InitializeComponent();
        StartInput.Text = FormatTime(snapshot.PositionSeconds);
        Loaded += (_, _) => TitleInput.Focus();
    }

    private static string FormatTime(long seconds) => $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";

    private void SetCurrent(System.Windows.Controls.TextBox target)
    {
        if (_player.CaptureMomentPosition() is { } snapshot && snapshot.MediaItemId == _mediaItemId)
        {
            target.Text = FormatTime(snapshot.PositionSeconds);
            ErrorText.Text = string.Empty;
        }
        else ErrorText.Text = "The original video is no longer available.";
    }

    private void OnCurrentStart(object sender, RoutedEventArgs e) => SetCurrent(StartInput);
    private void OnCurrentEnd(object sender, RoutedEventArgs e) => SetCurrent(EndInput);
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void OnSaveAndReplay(object sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task SaveAsync(bool replay)
    {
        if (_saving) return;
        if (!MomentTimeParser.TryParse(StartInput.Text, out var start)) { ErrorText.Text = "Enter a valid start time (hours:minutes:seconds)."; return; }
        long? end = null;
        if (!string.IsNullOrWhiteSpace(EndInput.Text))
        {
            if (!MomentTimeParser.TryParse(EndInput.Text, out var parsedEnd)) { ErrorText.Text = "Enter a valid end time (hours:minutes:seconds)."; return; }
            end = parsedEnd;
            if (end <= start) { ErrorText.Text = "The end time must be after the start time."; return; }
        }
        if (replay && end is null) { ErrorText.Text = "Add an end time to replay a sequence."; return; }
        if (_durationSeconds > 0 && (start > _durationSeconds || end > _durationSeconds)) { ErrorText.Text = "Times must be within the video duration."; return; }
        var title = TitleInput.Text.Trim();
        if (title.Length == 0) { ErrorText.Text = "Enter a title."; return; }
        var tags = TagsInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => new SavedMomentTag { Name = name }).ToList();
        var moment = new SavedMoment { MediaItemId = _mediaItemId, StartSeconds = start, EndSeconds = end, Title = title,
            Notes = string.IsNullOrWhiteSpace(NotesInput.Text) ? null : NotesInput.Text.Trim(), Tags = tags };
        _saving = true;
        SaveButton.IsEnabled = false;
        ReplayButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            if (await _player.SaveMomentAsync(moment))
            {
                MomentToReplay = replay ? moment : null;
                _saving = false;
                DialogResult = true;
            }
            else ErrorText.Text = "The media item is no longer available.";
        }
        catch (Exception)
        {
            ErrorText.Text = "Could not save this moment. Please try again.";
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
            ReplayButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_saving) e.Cancel = true;
        base.OnClosing(e);
    }
}

/// <summary>Parses non-negative whole-second timestamps in hours:minutes:seconds format.</summary>
public static class MomentTimeParser
{
    public static bool TryParse(string? text, out long seconds)
    {
        seconds = 0;
        var parts = (text ?? string.Empty).Trim().Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 59 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var remainder) || remainder > 59 ||
            hours > (long.MaxValue - 3599) / 3600) return false;
        seconds = hours * 3600 + minutes * 60 + remainder;
        return true;
    }
}
