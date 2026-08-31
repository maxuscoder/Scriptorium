using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Displays the lessons contained by one tutorial collection.
/// </summary>
public sealed class TutorialDetailsPageViewModel : PageViewModel
{
    private readonly ICourseRepository _courseRepository;
    private readonly INavigationService _navigationService;
    private PageViewModel? _returnPage;
    private string _courseTitle = "Tutorial";
    private string _sourceFolder = string.Empty;

    public TutorialDetailsPageViewModel(
        ICourseRepository courseRepository,
        INavigationService navigationService)
    {
        _courseRepository = courseRepository;
        _navigationService = navigationService;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
    }

    public override string Title => _courseTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public ObservableCollection<TutorialLessonViewModel> Lessons { get; } = [];

    public string LessonCountText => $"{Lessons.Count} lesson{(Lessons.Count == 1 ? string.Empty : "s")}";

    public ICommand BackCommand { get; }

    /// <summary>Loads a tutorial collection before it becomes the current page.</summary>
    public async Task<bool> LoadAsync(Guid courseId, PageViewModel returnPage)
    {
        ArgumentNullException.ThrowIfNull(returnPage);

        var course = await _courseRepository.GetByIdAsync(courseId);
        if (course is null)
        {
            return false;
        }

        _returnPage = returnPage;
        _courseTitle = MediaDisplayText.TitleOrFallback(course.Title, "Untitled tutorial");
        SourceFolder = course.LibraryFolder.DisplayNameOrName;
        Lessons.Clear();
        foreach (var lesson in course.Lessons.OrderBy(lesson => lesson.SortOrder))
        {
            Lessons.Add(new TutorialLessonViewModel(lesson));
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LessonCountText));
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        return true;
    }

    private void GoBack()
    {
        if (_returnPage is not null)
        {
            _navigationService.NavigateTo(_returnPage);
        }
    }
}

/// <summary>Displays one ordered tutorial lesson.</summary>
public sealed class TutorialLessonViewModel(Lesson lesson)
{
    public string Title => MediaDisplayText.TitleOrFallback(lesson.Title, "Untitled lesson");

    public string Position => lesson.LessonNumber is { } number ? $"Lesson {number}" : $"Lesson {lesson.SortOrder + 1}";

    public string Runtime => MediaRuntimeFormatter.Format(lesson.MediaItem.RuntimeSeconds);

    public string FilePath => lesson.FilePath;

    public bool IsMissing => lesson.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";
}
