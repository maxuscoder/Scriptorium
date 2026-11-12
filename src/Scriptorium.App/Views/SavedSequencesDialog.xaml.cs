using System.Windows;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Models;

namespace Scriptorium.App.Views;

public partial class SavedSequencesDialog : Window
{
    private readonly VideoPlayerViewModel _player;

    public SavedSequencesDialog(VideoPlayerViewModel player)
    {
        _player = player;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Message.Text = "Loading saved sequences...";
        try
        {
            var moments = await _player.GetSavedSequencesAsync();
            SequenceList.ItemsSource = moments.Select(moment => new SequenceItem(moment)).ToList();
            Message.Text = moments.Count == 0 ? "No saved sequences for this video." : string.Empty;
        }
        catch (Exception) { Message.Text = "Could not load saved sequences. Please try again."; }
    }

    private void OnReplay(object sender, RoutedEventArgs e)
    {
        if (SequenceList.SelectedItem is not SequenceItem item) { Message.Text = "Select a sequence to replay."; return; }
        if (!_player.ReplayMoment(item.Moment)) { Message.Text = "This sequence cannot be played for the current video."; return; }
        DialogResult = true;
    }

    private sealed class SequenceItem(SavedMoment moment)
    {
        public SavedMoment Moment { get; } = moment;
        public string Display => $"{Moment.Title}  ({Format(Moment.StartSeconds)} ? {Format(Moment.EndSeconds!.Value)})";
        private static string Format(long seconds) => $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
}
