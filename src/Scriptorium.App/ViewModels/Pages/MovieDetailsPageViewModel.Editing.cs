using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class MovieDetailsPageViewModel
{
    private bool _isEditing;
    private bool _isSavingChanges;
    private string _editStatus = string.Empty;

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                (EditCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (SaveChangesCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }
    public bool IsSavingChanges
    {
        get => _isSavingChanges;
        private set
        {
            if (SetProperty(ref _isSavingChanges, value))
            {
                OnPropertyChanged(nameof(CanEditFields));
                ((RelayCommand)CancelEditCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanEditFields => !IsSavingChanges;
    public string EditStatus { get => _editStatus; private set => SetProperty(ref _editStatus, value); }
    public string PlayActionText => _movie is { IsCompleted: false, PlaybackPositionSeconds: > 0 } ? "Continue" : "Play";
    public ObservableCollection<MediaDetailsMetadataItem> FileMetadataItems { get; } = [];
    public ICommand EditCommand { get; private set; } = null!;
    public ICommand CancelEditCommand { get; private set; } = null!;
    public ICommand SaveChangesCommand { get; private set; } = null!;

    private void InitializeEditingCommands()
    {
        EditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = true; }, () => _movie is not null && !IsEditing && !IsSavingChanges);
        CancelEditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = false; }, () => !IsSavingChanges);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => _movie is not null && IsEditing);
    }

    private void ResetEditDraft()
    {
        if (_movie is not { } movie) return;
        EditableTitle = movie.DisplayTitle;
        EditableDescription = movie.DisplayDescription ?? string.Empty;
        EditableReleaseYear = movie.EffectiveReleaseYear?.ToString() ?? string.Empty;
        EditableThumbnailPath = movie.ThumbnailPath ?? string.Empty;
        SelectedMediaType = movie.MediaType;
        SelectedCategory = CategoryOptions.FirstOrDefault(option => option.Id == movie.CategoryId) ?? UncategorizedOption;
        EditStatus = TitleStatus = DescriptionStatus = ReleaseYearStatus = ThumbnailStatus =
            MediaTypeStatus = CategoryStatus = MetadataResetStatus = string.Empty;
    }

    // Validate the entire draft before invoking the existing per-field persistence paths.
    // Only changed fields are saved, so opening and saving never creates unnecessary overrides.
    private async Task SaveChangesAsync()
    {
        if (_movie is not { } movie) return;
        EditStatus = string.Empty;
        var thumbnailChanged = EditableThumbnailPath != (movie.ThumbnailPath ?? string.Empty);
        try
        {
            MediaTitleValidation.Normalize(EditableTitle);
            MediaDescriptionValidation.Normalize(EditableDescription);
            MediaReleaseYearValidation.Normalize(EditableReleaseYear);
            if (thumbnailChanged) MediaThumbnailValidation.Normalize(EditableThumbnailPath);
            if (SelectedCategory is null || SelectedMediaType is null)
                throw new ArgumentException("Choose a media type and category.");
        }
        catch (ArgumentException exception)
        {
            EditStatus = exception.Message;
            return;
        }

        IsSavingChanges = true;
        try
        {
            if (EditableTitle != movie.DisplayTitle && !await SaveTitleAsync()) { EditStatus = TitleStatus; return; }
            if (EditableDescription != (movie.DisplayDescription ?? string.Empty) && !await SaveDescriptionAsync()) { EditStatus = DescriptionStatus; return; }
            if (EditableReleaseYear != (movie.EffectiveReleaseYear?.ToString() ?? string.Empty) && !await SaveReleaseYearAsync()) { EditStatus = ReleaseYearStatus; return; }
            if (thumbnailChanged && !await SaveThumbnailAsync()) { EditStatus = ThumbnailStatus; return; }
            if (SelectedCategory?.Id != movie.CategoryId && !await SaveCategoryAsync()) { EditStatus = CategoryStatus; return; }
            if (SelectedMediaType != movie.MediaType && !await SaveMediaTypeAsync()) { EditStatus = MediaTypeStatus; return; }
            IsEditing = false;
        }
        catch (Exception)
        {
            EditStatus = "Changes could not be fully saved. Review the fields and try again.";
        }
        finally
        {
            IsSavingChanges = false;
        }
    }
}
