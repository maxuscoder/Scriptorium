using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.App.ViewModels;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Displays the lessons contained by one tutorial collection.
/// </summary>
public sealed class TutorialDetailsPageViewModel : PageViewModel
{
    private static readonly MediaCategoryOptionViewModel UncategorizedOption = new(null, "Uncategorized");
    private readonly ICourseRepository _courseRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IFavoriteService _favoriteService;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly INavigationService _navigationService;
    private readonly ITutorialCourseSynchronizer _tutorialCourseSynchronizer;
    private PageViewModel? _returnPage;
    private Guid? _courseId;
    private int _isCourseRefreshQueued;
    private string _courseTitle = "Tutorial";
    private string _sourceFolder = string.Empty;
    private TutorialLessonViewModel? _selectedLesson;
    private MediaCategoryOptionViewModel? _selectedCategory;
    private string _categoryStatus = string.Empty;

    public TutorialDetailsPageViewModel(
        ICourseRepository courseRepository,
        INavigationService navigationService,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IFavoriteService favoriteService,
        ITutorialCourseSynchronizer tutorialCourseSynchronizer,
        IPlaybackProgressService playbackProgressService)
    {
        _courseRepository = courseRepository;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _favoriteService = favoriteService;
        _playbackProgressService = playbackProgressService;
        _navigationService = navigationService;
        _tutorialCourseSynchronizer = tutorialCourseSynchronizer;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectLessonCommand = new RelayCommand(SelectLesson, lesson => lesson is TutorialLessonViewModel);
        PreviousLessonCommand = new RelayCommand(SelectPreviousLesson, CanSelectPreviousLesson);
        NextLessonCommand = new RelayCommand(SelectNextLesson, CanSelectNextLesson);
        ToggleLessonCompletionCommand = new AsyncRelayCommand(ToggleLessonCompletionAsync, () => SelectedLesson is not null);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => SelectedLesson is not null && SelectedCategory is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedLesson is not null);
        _tutorialCourseSynchronizer.CoursesChanged += OnCoursesChanged;
    }

    public override string Title => _courseTitle;

    public string SourceFolder
    {
        get => _sourceFolder;
        private set => SetProperty(ref _sourceFolder, value);
    }

    public ObservableCollection<TutorialLessonViewModel> Lessons { get; } = [];

    public string LessonCountText => $"{Lessons.Count} lesson{(Lessons.Count == 1 ? string.Empty : "s")}";

    /// <summary>Gets whether the course contains lessons to select.</summary>
    public bool HasLessons => Lessons.Count > 0;

    /// <summary>Gets the summed duration of all lessons with a known runtime.</summary>
    public string TotalDurationText =>
        MediaRuntimeFormatter.Format(Lessons.Sum(lesson => lesson.RuntimeSeconds)) is { Length: > 0 } duration
            ? duration
            : "Unknown";

    /// <summary>Gets the number of lessons the learner has completed.</summary>
    public int CompletedLessonCount => Lessons.Count(lesson => lesson.IsCompleted);

    /// <summary>Gets the course's completion percentage based on completed lessons.</summary>
    public double CourseProgressPercentage => Lessons.Count == 0
        ? 0
        : CompletedLessonCount / (double)Lessons.Count * 100;

    /// <summary>Gets a concise summary of the learner's progress through this course.</summary>
    public string CourseProgressText => Lessons.Count == 0
        ? "No lessons available"
        : $"{CompletedLessonCount} of {Lessons.Count} lessons completed";

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
            OnPropertyChanged(nameof(FavoriteActionText));
            OnPropertyChanged(nameof(CompletionActionText));
            SelectCategory(value?.CategoryId);
            ((RelayCommand)PreviousLessonCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextLessonCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ToggleLessonCompletionCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Gets the selected lesson's position within the collection.</summary>
    public string SelectedLessonPositionText => SelectedLesson is null
        ? "No lessons available"
        : $"Lesson {Lessons.IndexOf(SelectedLesson) + 1} of {Lessons.Count}";

    public ObservableCollection<MediaCategoryOptionViewModel> CategoryOptions { get; } = [];

    public MediaCategoryOptionViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ((AsyncRelayCommand)SaveCategoryCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string CategoryStatus
    {
        get => _categoryStatus;
        private set => SetProperty(ref _categoryStatus, value);
    }

    public string FavoriteActionText => SelectedLesson?.IsFavorite == true
        ? "Remove from favorites"
        : "Add to favorites";

    public string CompletionActionText => SelectedLesson?.IsCompleted == true
        ? "Mark incomplete"
        : "Complete lesson";

    public ICommand BackCommand { get; }

    /// <summary>Gets the command that selects a lesson from the list.</summary>
    public ICommand SelectLessonCommand { get; }

    /// <summary>Gets the command that selects the preceding lesson.</summary>
    public ICommand PreviousLessonCommand { get; }

    /// <summary>Gets the command that selects the following lesson.</summary>
    public ICommand NextLessonCommand { get; }

    /// <summary>Marks the selected lesson complete or incomplete.</summary>
    public ICommand ToggleLessonCompletionCommand { get; }

    public ICommand SaveCategoryCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

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
        _courseId = course.Id;
        await PopulateCourseAsync(course, selectedLessonMediaItemId: null);
        return true;
    }

    private async Task PopulateCourseAsync(Course course, Guid? selectedLessonMediaItemId)
    {
        _courseTitle = MediaDisplayText.TitleOrFallback(course.Title, "Untitled tutorial");
        SourceFolder = course.LibraryFolder.DisplayNameOrName;
        Lessons.Clear();
        foreach (var lesson in course.Lessons
                     .OrderBy(lesson => lesson.SortOrder)
                     .ThenBy(lesson => lesson.LessonNumber.HasValue ? 0 : 1)
                     .ThenBy(lesson => lesson.LessonNumber)
                     .ThenBy(lesson => lesson.Title, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(lesson => lesson.Id))
        {
            Lessons.Add(new TutorialLessonViewModel(lesson));
        }

        await RefreshCategoryOptionsAsync();
        SelectedLesson = Lessons.FirstOrDefault(lesson => lesson.MediaItemId == selectedLessonMediaItemId)
            ?? Lessons.FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LessonCountText));
        OnPropertyChanged(nameof(HasLessons));
        OnPropertyChanged(nameof(TotalDurationText));
        RefreshCourseProgress();
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleFavoriteCommand).NotifyCanExecuteChanged();
    }

    private void OnCoursesChanged()
    {
        if (_courseId is null || Interlocked.Exchange(ref _isCourseRefreshQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshLoadedCourseAsync);
            return;
        }

        _ = RefreshLoadedCourseAsync();
    }

    private async Task RefreshLoadedCourseAsync()
    {
        try
        {
            if (_courseId is not { } courseId)
            {
                return;
            }

            var course = await _courseRepository.GetByIdAsync(courseId);
            if (course is not null)
            {
                await PopulateCourseAsync(course, SelectedLesson?.MediaItemId);
            }
        }
        finally
        {
            Volatile.Write(ref _isCourseRefreshQueued, 0);
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        var lesson = SelectedLesson;
        if (lesson is null)
        {
            return;
        }

        var isFavorite = !lesson.IsFavorite;
        var updated = isFavorite
            ? await _favoriteService.AddAsync(lesson.MediaItemId)
            : await _favoriteService.RemoveAsync(lesson.MediaItemId);
        if (updated)
        {
            lesson.SetFavorite(isFavorite);
            OnPropertyChanged(nameof(FavoriteActionText));
        }
    }

    private async Task ToggleLessonCompletionAsync()
    {
        var lesson = SelectedLesson;
        if (lesson is null)
        {
            return;
        }

        var isCompleted = !lesson.IsCompleted;
        if (!await _playbackProgressService.SetCompletionAsync(lesson.MediaItemId, isCompleted))
        {
            return;
        }

        lesson.SetCompletion(isCompleted);
        RefreshCourseProgress();
    }

    private void RefreshCourseProgress()
    {
        OnPropertyChanged(nameof(CompletedLessonCount));
        OnPropertyChanged(nameof(CourseProgressPercentage));
        OnPropertyChanged(nameof(CourseProgressText));
        OnPropertyChanged(nameof(CompletionActionText));
        ((AsyncRelayCommand)ToggleLessonCompletionCommand).NotifyCanExecuteChanged();
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

    private async Task SaveCategoryAsync()
    {
        var lesson = SelectedLesson;
        var category = SelectedCategory;
        if (lesson is null || category is null)
        {
            return;
        }

        if (lesson.CategoryId == category.Id)
        {
            CategoryStatus = "No category changes to save.";
            return;
        }

        if (!await _categoryService.AssignToMediaAsync(lesson.MediaItemId, category.Id))
        {
            CategoryStatus = "The category assignment could not be saved.";
            return;
        }

        lesson.SetCategory(category);
        CategoryStatus = category.Id is null
            ? "Category assignment removed."
            : $"Category '{category.Name}' assigned.";
    }

    private async Task RefreshCategoryOptionsAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        CategoryOptions.Clear();
        CategoryOptions.Add(UncategorizedOption);
        foreach (var category in categories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase))
        {
            CategoryOptions.Add(new MediaCategoryOptionViewModel(category.Id, category.Name, category));
        }
    }

    private void SelectCategory(Guid? categoryId)
    {
        SelectedCategory = CategoryOptions.FirstOrDefault(option => option.Id == categoryId) ?? UncategorizedOption;
        CategoryStatus = string.Empty;
    }
}

/// <summary>Displays one ordered tutorial lesson.</summary>
public sealed class TutorialLessonViewModel(Lesson lesson) : ViewModelBase, IMediaFavoriteItem
{
    public string Title => MediaDisplayText.TitleOrFallback(lesson.Title, "Untitled lesson");

    public string Position => lesson.LessonNumber is { } number ? $"Lesson {number}" : $"Lesson {lesson.SortOrder + 1}";

    public string Runtime => MediaRuntimeFormatter.Format(lesson.MediaItem.RuntimeSeconds);

    /// <summary>Gets the known duration in seconds, or zero when it is unavailable.</summary>
    public long RuntimeSeconds => lesson.MediaItem.RuntimeSeconds.GetValueOrDefault();

    public string FilePath => lesson.FilePath;

    public Guid MediaItemId => lesson.MediaItemId;

    public bool IsFavorite => lesson.MediaItem.IsFavorite;

    /// <summary>Gets whether the learner has completed this lesson.</summary>
    public bool IsCompleted => lesson.MediaItem.IsCompleted;

    public string CompletionStatus => IsCompleted ? "Completed" : "Not started";

    public void SetFavorite(bool isFavorite)
    {
        if (lesson.MediaItem.IsFavorite == isFavorite)
        {
            return;
        }

        lesson.MediaItem.IsFavorite = isFavorite;
        OnPropertyChanged(nameof(IsFavorite));
    }

    internal void SetCompletion(bool isCompleted)
    {
        if (lesson.MediaItem.IsCompleted == isCompleted)
        {
            return;
        }

        lesson.MediaItem.IsCompleted = isCompleted;
        lesson.MediaItem.PlaybackPositionSeconds = isCompleted
            ? lesson.MediaItem.RuntimeSeconds.GetValueOrDefault()
            : 0;
        lesson.MediaItem.LastPlayed = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CompletionStatus));
    }

    public Guid? CategoryId => lesson.MediaItem.CategoryId;

    public bool IsMissing => lesson.MediaItem.IsMissing;

    public string Availability => IsMissing ? "File unavailable" : "Available";

    private bool _isSelected;

    /// <summary>Gets whether this lesson is the current position in the collection.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    internal void SetCategory(MediaCategoryOptionViewModel category)
    {
        lesson.MediaItem.CategoryId = category.Id;
        lesson.MediaItem.Category = category.Category;
        OnPropertyChanged(nameof(CategoryId));
    }
}
