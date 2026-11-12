using System.Windows;
using Scriptorium.App.ViewModels.Pages;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views;

public partial class EditMomentDialog : Window
{
    private readonly SavedMomentsPageViewModel _page;
    private readonly SavedMoment _moment;
    private bool _saving;

    public EditMomentDialog(SavedMomentsPageViewModel page, SavedMoment moment)
    {
        _page = page;
        _moment = moment;
        InitializeComponent();
        TitleInput.Text = moment.Title;
        StartInput.Text = Format(moment.StartSeconds);
        EndInput.Text = moment.EndSeconds is { } end ? Format(end) : string.Empty;
        NotesInput.Text = moment.Notes ?? string.Empty;
        TagsInput.Text = string.Join(", ", moment.Tags.Select(tag => tag.Name));
        Loaded += (_, _) => TitleInput.Focus();
    }

    private static string Format(long seconds) => $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        var title = TitleInput.Text.Trim();
        if (title.Length == 0) { ErrorText.Text = "Enter a title."; return; }
        if (!MomentTimeParser.TryParse(StartInput.Text, out var start)) { ErrorText.Text = "Enter a valid start time (hours:minutes:seconds)."; return; }
        long? end = null;
        if (!string.IsNullOrWhiteSpace(EndInput.Text))
        {
            if (!MomentTimeParser.TryParse(EndInput.Text, out var parsedEnd)) { ErrorText.Text = "Enter a valid end time (hours:minutes:seconds)."; return; }
            if (parsedEnd <= start) { ErrorText.Text = "The end time must be after the start time."; return; }
            end = parsedEnd;
        }
        if (_moment.MediaItem.RuntimeSeconds is > 0 and var duration && (start > duration || end > duration))
        { ErrorText.Text = "Times must be within the video duration."; return; }
        var edited = new SavedMoment
        {
            Id = _moment.Id, MediaItemId = _moment.MediaItemId, Title = title, StartSeconds = start, EndSeconds = end,
            Notes = string.IsNullOrWhiteSpace(NotesInput.Text) ? null : NotesInput.Text.Trim(),
            Tags = TagsInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => new SavedMomentTag { Name = name }).ToList()
        };
        _saving = true;
        SaveButton.IsEnabled = CancelButton.IsEnabled = false;
        try
        {
            if (await _page.UpdateMomentAsync(edited)) { _saving = false; DialogResult = true; }
            else ErrorText.Text = _page.Status;
        }
        finally { _saving = false; SaveButton.IsEnabled = CancelButton.IsEnabled = true; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_saving) e.Cancel = true;
        base.OnClosing(e);
    }
}
