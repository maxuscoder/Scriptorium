using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Scriptorium.App.Commands;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed partial class TutorialDetailsPageViewModel
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
                ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)SaveChangesCommand).NotifyCanExecuteChanged();
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

    public bool CanEditFields => SelectedLesson is not null && !IsSavingChanges;
    public string EditStatus { get => _editStatus; private set => SetProperty(ref _editStatus, value); }
    public ICommand EditCommand { get; private set; } = null!;
    public ICommand CancelEditCommand { get; private set; } = null!;
    public ICommand SaveChangesCommand { get; private set; } = null!;

    private void InitializeEditingCommands()
    {
        EditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = true; },
            () => SelectedLesson is not null && !IsEditing && !IsSavingChanges);
        CancelEditCommand = new RelayCommand(() => { ResetEditDraft(); IsEditing = false; }, () => !IsSavingChanges);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync,
            () => SelectedLesson is not null && IsEditing);
    }

    private void OnSelectedLessonChanged(bool isDifferentLesson)
    {
        // A draft always belongs to one lesson, including when playback advances automatically.
        if (isDifferentLesson && !IsSavingChanges) IsEditing = false;
        EditStatus = string.Empty;
        OnPropertyChanged(nameof(CanEditFields));
        ((RelayCommand)EditCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveChangesCommand).NotifyCanExecuteChanged();
    }

    private void ResetEditDraft()
    {
        var lesson = SelectedLesson;
        EditableTitle = lesson?.Title ?? string.Empty;
        EditableDescription = lesson?.Description ?? string.Empty;
        EditableReleaseYear = lesson?.ReleaseYear?.ToString() ?? string.Empty;
        EditableThumbnailPath = lesson?.ThumbnailPath ?? string.Empty;
        SelectedMediaType = lesson?.MediaType;
        SelectCategory(lesson?.CategoryId);
        EditStatus = TitleStatus = DescriptionStatus = ReleaseYearStatus = ThumbnailStatus =
            MediaTypeStatus = MetadataResetStatus = string.Empty;
    }

    private async Task SaveChangesAsync()
    {
        if (SelectedLesson is not { } lesson) return;
        EditStatus = string.Empty;
        var thumbnailChanged = EditableThumbnailPath != (lesson.ThumbnailPath ?? string.Empty);
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

        // Preserve the existing field persistence paths and avoid creating unchanged overrides.
        var changes = new List<(Func<Task<bool>> Save, Func<string> Status)>();
        if (EditableTitle != lesson.Title) changes.Add((SaveTitleAsync, () => TitleStatus));
        if (EditableDescription != (lesson.Description ?? string.Empty)) changes.Add((SaveDescriptionAsync, () => DescriptionStatus));
        if (EditableReleaseYear != (lesson.ReleaseYear?.ToString() ?? string.Empty)) changes.Add((SaveReleaseYearAsync, () => ReleaseYearStatus));
        if (thumbnailChanged) changes.Add((SaveThumbnailAsync, () => ThumbnailStatus));
        if (SelectedCategory?.Id != lesson.CategoryId) changes.Add((SaveCategoryAsync, () => CategoryStatus));
        // Type changes can rebuild course membership, so they are persisted last.
        if (SelectedMediaType != lesson.MediaType) changes.Add((SaveMediaTypeAsync, () => MediaTypeStatus));

        IsSavingChanges = true;
        try
        {
            foreach (var change in changes)
            {
                if (!ReferenceEquals(SelectedLesson, lesson))
                {
                    IsEditing = false;
                    return;
                }
                if (!await change.Save())
                {
                    EditStatus = change.Status();
                    return;
                }
            }
            IsEditing = false;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Could not save lesson metadata");
            EditStatus = "Changes could not be fully saved. Review the fields and try again.";
        }
        finally
        {
            IsSavingChanges = false;
            if (!ReferenceEquals(SelectedLesson, lesson))
            {
                IsEditing = false;
                ResetEditDraft();
            }
        }
    }
}
