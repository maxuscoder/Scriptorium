using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
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
    private TutorialLessonViewModel? _selectedLesson;

    public TutorialDetailsPageViewModel(
        ICourseRepository courseRepository,
        INavigationService navigationService)
    {
        _courseRepository = courseRepository;
        _navigationService = navigationService;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectLessonCommand = new RelayCommand(SelectLesson, lesson => lesson is TutorialLessonViewModel);
        PreviousLessonCommand = new RelayCommand(SelectPreviousLesson, CanSelectPreviousLesson);
        NextLessonCommand = new RelayCommand(SelectNextLesson, CanSelectNextLesson);
    }

    public override string Title => _courseTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public ObservableCollection<TutorialLessonViewModel> Lessons { get; } = [];

    public string LessonCountText => $"{Lessons.Count} lesson{(Lessons.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets the summed duration of all lessons with a known runtime.</summary>
    public string TotalDurationText =>
        MediaRuntimeFormatter.Format(Lessons.Sum(lesson => lesson.RuntimeSeconds)) is { Length: > 0 } duration
            ? duration
            : "Unknown";

    /// <summary>Gets the lesson currently selected for sequential navigation.</summary>
    public TutorialLessonViewModel? SelectedLesson
    {
        get => _selectedLesson;
        private set
        {
            if (!SetProperty(ref _selectedLesson, value))
            {
                return;
            }

            foreach (var lesson in Lessons)
            {
                lesson.IsSelected = ReferenceEquals(lesson, value);
            }

            OnPropertyChanged(nameof(SelectedLessonPositionText));
            ((RelayCommand)PreviousLessonCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextLessonCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Gets the selected lesson's position within the collection.</summary>
    public string SelectedLessonPositionText => SelectedLesson is null
        ? "No lessons available"
        : $"Lesson {Lessons.IndexOf(SelectedLesson) + 1} of {Lessons.Count}";

    public ICommand BackCommand { get; }

    /// <summary>Gets the command that selects a lesson from the list.</summary>
    public ICommand SelectLessonCommand { get; }

    /// <summary>Gets the command that selects the preceding lesson.</summary>
    public ICommand PreviousLessonCommand { get; }

    /// <summary>Gets the command that selects the following lesson.</summary>
    public ICommand NextLessonCommand { get; }

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

        SelectedLesson = Lessons.FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LessonCountText));
        OnPropertyChanged(nameof(TotalDurationText));
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

    private void SelectLesson(object? parameter)
    {
        if (parameter is TutorialLessonViewModel lesson && Lessons.Contains(lesson))
        {
            SelectedLesson = lesson;
        }
    }

    private void SelectPreviousLesson()
    {
        var selectedIndex = SelectedLesson is null ? -1 : Lessons.IndexOf(SelectedLesson);
        if (selectedIndex > 0)
        {
            SelectedLesson = Lessons[selectedIndex - 1];
        }
    }

    private void SelectNextLesson()
    {
        var selectedIndex = SelectedLesson is null ? -1 : Lessons.IndexOf(SelectedLesson);
        if (selectedIndex >= 0 && selectedIndex < Lessons.Count - 1)
        {
            SelectedLesson = Lessons[selectedIndex + 1];
        }
    }

    private bool CanSelectPreviousLesson() =>
        SelectedLesson is not null && Lessons.IndexOf(SelectedLesson) > 0;

    private bool CanSelectNextLesson() =>
        SelectedLesson is not null && Lessons.IndexOf(SelectedLesson) < Lessons.Count - 1;
}

/// <summary>Displays one ordered tutorial lesson.</summary>
public sealed class TutorialLessonViewModel(Lesson lesson) : ViewModelBase
{
    public string Title => MediaDisplayText.TitleOrFallback(lesson.Title, "Untitled lesson");

    public string Position => lesson.LessonNumber is { } number ? $"Lesson {number}" : $"Lesson {lesson.SortOrder + 1}";

    public string Runtime => MediaRuntimeFormatter.Format(lesson.MediaItem.RuntimeSeconds);

    /// <summary>Gets the known duration in seconds, or zero when it is unavailable.</summary>
    public long RuntimeSeconds => lesson.MediaItem.RuntimeSeconds.GetValueOrDefault();

    public string FilePath => lesson.FilePath;

    public bool IsMissing => lesson.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    private bool _isSelected;

    /// <summary>Gets whether this lesson is the current position in the collection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }
}
