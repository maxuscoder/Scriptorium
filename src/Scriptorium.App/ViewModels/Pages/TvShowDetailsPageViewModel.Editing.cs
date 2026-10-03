using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class TvShowDetailsPageViewModel
{
    private bool _isEditing;
    private bool _isSavingChanges;
    private Guid? _savingEpisodeId;
    private bool _saveSelectionChanged;
    private string _editStatus = string.Empty;

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (!SetProperty(ref _isEditing, value)) return;
            ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveChangesCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsSavingChanges
    {
        get => _isSavingChanges;
        private set
        {
            if (!SetProperty(ref _isSavingChanges, value)) return;
            OnPropertyChanged(nameof(CanEditFields));
            ((RelayCommand)CancelEditCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
        }
    }

    public bool CanEditFields => SelectedEpisode is not null && !IsSavingChanges;
    public string EditStatus { get => _editStatus; private set => SetProperty(ref _editStatus, value); }
    public ICommand EditCommand { get; private set; } = null!;
    public ICommand CancelEditCommand { get; private set; } = null!;
    public ICommand SaveChangesCommand { get; private set; } = null!;

    private void InitializeEditingCommands()
    {
        EditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = true; },
            () => SelectedEpisode is not null && !IsEditing && !IsSavingChanges);
        CancelEditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = false; }, () => !IsSavingChanges);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => SelectedEpisode is not null && IsEditing);
    }

    private void OnSelectedEpisodeChanged(bool isDifferentEpisode)
    {
        if (isDifferentEpisode && IsSavingChanges) _saveSelectionChanged = true;
        if (isDifferentEpisode) IsEditing = false;
        if (!IsSavingChanges) EditStatus = string.Empty;
        OnPropertyChanged(nameof(CanEditFields));
        ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveChangesCommand).NotifyCanExecuteChanged();
    }

    private void ResetEditDraft()
    {
        var episode = SelectedEpisode;
        EditableTitle = episode?.Title ?? string.Empty;
        EditableDescription = episode?.Description ?? string.Empty;
        EditableReleaseYear = episode?.ReleaseYear?.ToString() ?? string.Empty;
        EditableThumbnailPath = episode?.ThumbnailPath ?? string.Empty;
        EditableSeasonNumber = episode?.SeasonNumber.ToString() ?? string.Empty;
        EditableEpisodeNumber = episode?.EpisodeNumber.ToString() ?? string.Empty;
        SelectedMediaType = episode?.MediaType;
        SelectCategory(episode?.CategoryId);
        EditStatus = TitleStatus = DescriptionStatus = ReleaseYearStatus = ThumbnailStatus =
            MediaTypeStatus = SeasonNumberStatus = EpisodeNumberStatus = MetadataResetStatus = string.Empty;
    }

    private async Task SaveChangesAsync()
    {
        if (SelectedEpisode is not { } episode) return;
        EditStatus = string.Empty;
        var thumbnailChanged = EditableThumbnailPath != (episode.ThumbnailPath ?? string.Empty);
        var seasonChanged = EditableSeasonNumber != episode.SeasonNumber.ToString();
        var episodeChanged = EditableEpisodeNumber != (episode.EpisodeNumber?.ToString() ?? string.Empty);
        try
        {
            MediaTitleValidation.Normalize(EditableTitle);
            MediaDescriptionValidation.Normalize(EditableDescription);
            MediaReleaseYearValidation.Normalize(EditableReleaseYear);
            if (thumbnailChanged) MediaThumbnailValidation.Normalize(EditableThumbnailPath);
            if (seasonChanged) MediaSeasonValidation.Normalize(EditableSeasonNumber);
            if (episodeChanged) MediaEpisodeValidation.Normalize(EditableEpisodeNumber);
            if (SelectedCategory is null || SelectedMediaType is null)
                throw new ArgumentException("Choose a media type and category.");
        }
        catch (ArgumentException exception)
        {
            EditStatus = exception.Message;
            return;
        }

        // Reuse each existing persistence path. Grouping refreshes can replace episode
        // ViewModels, so identify the draft's owner by media ID throughout the save.
        var changes = new List<(Func<Task<bool>> Save, Func<string> Status)>();
        if (EditableTitle != episode.Title) changes.Add((SaveTitleAsync, () => TitleStatus));
        if (EditableDescription != (episode.Description ?? string.Empty)) changes.Add((SaveDescriptionAsync, () => DescriptionStatus));
        if (EditableReleaseYear != (episode.ReleaseYear?.ToString() ?? string.Empty)) changes.Add((SaveReleaseYearAsync, () => ReleaseYearStatus));
        if (thumbnailChanged) changes.Add((SaveThumbnailAsync, () => ThumbnailStatus));
        if (SelectedCategory?.Id != episode.CategoryId) changes.Add((SaveCategoryAsync, () => CategoryStatus));
        if (seasonChanged) changes.Add((SaveSeasonNumberAsync, () => SeasonNumberStatus));
        if (episodeChanged) changes.Add((SaveEpisodeNumberAsync, () => EpisodeNumberStatus));
        // Type changes may remove the episode from this show; save them last.
        if (SelectedMediaType != episode.MediaType) changes.Add((SaveMediaTypeAsync, () => MediaTypeStatus));

        _savingEpisodeId = episode.MediaItemId;
        _saveSelectionChanged = false;
        IsSavingChanges = true;
        try
        {
            foreach (var change in changes)
            {
                if (_saveSelectionChanged || SelectedEpisode?.MediaItemId != _savingEpisodeId) { IsEditing = false; return; }
                if (!await change.Save()) { EditStatus = change.Status(); return; }
            }
            IsEditing = false;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Could not save episode metadata");
            EditStatus = "Changes could not be fully saved. Review the fields and try again.";
        }
        finally
        {
            IsSavingChanges = false;
            if (_saveSelectionChanged || SelectedEpisode?.MediaItemId != _savingEpisodeId)
            {
                IsEditing = false;
                ResetEditDraft();
            }
            _savingEpisodeId = null;
        }
    }
}
