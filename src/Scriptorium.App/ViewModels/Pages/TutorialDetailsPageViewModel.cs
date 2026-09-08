using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
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
public sealed class TutorialDetailsPageViewModel : PageViewModel, IDisposable
{
    private static readonly MediaCategoryOptionViewModel UncategorizedOption = new(null, "Uncategorized");
    private readonly ICourseRepository _courseRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IFavoriteService _favoriteService;
    private readonly IConfirmationDialog? _confirmationDialog;
    private readonly IPlaybackProgressService _playbackProgressService;
    private readonly INavigationService _navigationService;
    private readonly ITutorialCourseSynchronizer _tutorialCourseSynchronizer;
    private readonly VideoPlayerViewModel _player;
    private readonly ILogger<TutorialDetailsPageViewModel>? _logger;
    private readonly SemaphoreSlim _lessonOrderGate = new(1, 1);
    private PageViewModel? _returnPage;
    private Guid? _courseId;
    private int _isCourseRefreshQueued;
    private string _courseTitle = "Tutorial";
    private string _sourceFolder = string.Empty;
    private TutorialLessonViewModel? _selectedLesson;
    private MediaCategoryOptionViewModel? _selectedCategory;
    private string _categoryStatus = string.Empty;
    private string _orderStatus = string.Empty;
    private bool _isReordering;
    private bool _disposed;

    public TutorialDetailsPageViewModel(
        ICourseRepository courseRepository,
        INavigationService navigationService,
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IFavoriteService favoriteService,
        ITutorialCourseSynchronizer tutorialCourseSynchronizer,
        IPlaybackProgressService playbackProgressService,
        VideoPlayerViewModel player,
        IConfirmationDialog? confirmationDialog = null,
        ILogger<TutorialDetailsPageViewModel>? logger = null)
    {
        _courseRepository = courseRepository;
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _favoriteService = favoriteService;
        _confirmationDialog = confirmationDialog;
        _playbackProgressService = playbackProgressService;
        _navigationService = navigationService;
        _tutorialCourseSynchronizer = tutorialCourseSynchronizer;
        _player = player;
        _logger = logger;
        BackCommand = new RelayCommand(GoBack, () => _returnPage is not null);
        SelectLessonCommand = new RelayCommand(SelectLesson, lesson => lesson is TutorialLessonViewModel);
        ContinueLearningCommand = new RelayCommand(ContinueLearning, CanContinueLearning);
        MoveLessonUpCommand = new AsyncRelayCommand(MoveLessonUpAsync, CanMoveLessonUp);
        MoveLessonDownCommand = new AsyncRelayCommand(MoveLessonDownAsync, CanMoveLessonDown);
        PreviousLessonCommand = new RelayCommand(SelectPreviousLesson, CanSelectPreviousLesson);
        NextLessonCommand = new RelayCommand(SelectNextLesson, CanSelectNextLesson);
        ToggleLessonCompletionCommand = new AsyncRelayCommand(ToggleLessonCompletionAsync, () => SelectedLesson is not null);
        SaveCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync, () => SelectedLesson is not null && SelectedCategory is not null);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, () => SelectedLesson is not null);
        _tutorialCourseSynchronizer.CoursesChanged += OnCoursesChanged;
        _player.PlaybackProgressPersisted += OnPlaybackProgressPersisted;
        _player.PlaybackCompleted += OnPlaybackCompleted;
    }

    public override string Title => _courseTitle;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tutorialCourseSynchronizer.CoursesChanged -= OnCoursesChanged;
        _player.PlaybackProgressPersisted -= OnPlaybackProgressPersisted;
        _player.PlaybackCompleted -= OnPlaybackCompleted;
        _player.Dispose();
    }

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

    /// <summary>Gets the summed duration of lessons that have not been completed.</summary>
    public string RemainingDurationText
    {
        get
        {
            if (!HasIncompleteLessons)
            {
                return "0m";
            }

            return MediaRuntimeFormatter.Format(
                       Lessons.Where(lesson => !lesson.IsCompleted).Sum(lesson => lesson.RuntimeSeconds))
                   is { Length: > 0 } duration
                ? duration
                : "Unknown";
        }
    }

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

    /// <summary>Gets whether every lesson in the course has been completed.</summary>
    public bool IsCourseCompleted => Lessons.Count > 0 && CompletedLessonCount == Lessons.Count;

    /// <summary>Gets whether the course has a lesson that can be resumed.</summary>
    public bool HasIncompleteLessons => Lessons.Any(lesson => !lesson.IsCompleted);

    public string ContinueLearningText => IsCourseCompleted
        ? "Course completed"
        : HasIncompleteLessons
            ? "Continue learning"
            : "No lessons available";

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
            OpenSelectedLesson();
            SelectCategory(value?.CategoryId);
            ((RelayCommand)PreviousLessonCommand).NotifyCanExecuteChanged();
            ((RelayCommand)NextLessonCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ToggleLessonCompletionCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ContinueLearningCommand).NotifyCanExecuteChanged();
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

    public string OrderStatus
    {
        get => _orderStatus;
        private set => SetProperty(ref _orderStatus, value);
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

    /// <summary>Moves a lesson one position earlier in the course.</summary>
    public ICommand MoveLessonUpCommand { get; }

    /// <summary>Moves a lesson one position later in the course.</summary>
    public ICommand MoveLessonDownCommand { get; }

    /// <summary>Selects the first lesson that has not been completed.</summary>
    public ICommand ContinueLearningCommand { get; }

    /// <summary>Gets the player for the currently selected lesson.</summary>
    public VideoPlayerViewModel Player => _player;

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
            ?? Lessons.FirstOrDefault(lesson => !lesson.IsCompleted)
            ?? Lessons.FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(LessonCountText));
        OnPropertyChanged(nameof(HasLessons));
        OnPropertyChanged(nameof(TotalDurationText));
        OnPropertyChanged(nameof(IsCourseCompleted));
        OnPropertyChanged(nameof(HasIncompleteLessons));
        OnPropertyChanged(nameof(ContinueLearningText));
        RefreshCourseProgress();
        ((RelayCommand)BackCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ToggleFavoriteCommand).NotifyCanExecuteChanged();
        NotifyLessonOrderCommands();
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

        await _player.FlushPendingProgressSaveAsync();
        var isCompleted = !lesson.IsCompleted;
        if (!await _playbackProgressService.SetCompletionAsync(lesson.MediaItemId, isCompleted))
        {
            return;
        }

        lesson.SetCompletion(isCompleted);
        _player.SynchronizeCompletion(lesson.MediaItemId, isCompleted);
        RefreshCourseProgress();
    }

    private void ContinueLearning()
    {
        var nextLesson = Lessons.FirstOrDefault(lesson => !lesson.IsCompleted);
        if (nextLesson is not null)
        {
            SelectedLesson = nextLesson;
        }
    }

    private bool CanContinueLearning() => HasIncompleteLessons;

    private async Task MoveLessonUpAsync(object? parameter) => await MoveLessonAsync(parameter, -1);

    private async Task MoveLessonDownAsync(object? parameter) => await MoveLessonAsync(parameter, 1);

    private bool CanMoveLessonUp(object? parameter) =>
        !_isReordering && parameter is TutorialLessonViewModel lesson && Lessons.IndexOf(lesson) > 0;

    private bool CanMoveLessonDown(object? parameter) =>
        !_isReordering && parameter is TutorialLessonViewModel lesson &&
        Lessons.IndexOf(lesson) >= 0 &&
        Lessons.IndexOf(lesson) < Lessons.Count - 1;

    private async Task MoveLessonAsync(object? parameter, int offset)
    {
        if (parameter is not TutorialLessonViewModel lesson)
        {
            return;
        }

        await _lessonOrderGate.WaitAsync();
        _isReordering = true;
        NotifyLessonOrderCommands();
        try
        {
            if (_courseId is not { } courseId)
            {
                return;
            }

            var currentIndex = Lessons.IndexOf(lesson);
            var newIndex = currentIndex + offset;
            if (currentIndex < 0 || newIndex < 0 || newIndex >= Lessons.Count)
            {
                return;
            }

            var orderedLessonIds = Lessons.Select(candidate => candidate.LessonId).ToList();
            (orderedLessonIds[currentIndex], orderedLessonIds[newIndex]) =
                (orderedLessonIds[newIndex], orderedLessonIds[currentIndex]);
            if (!await _courseRepository.UpdateLessonOrderAsync(courseId, orderedLessonIds))
            {
                OrderStatus = "The lesson order could not be saved.";
                return;
            }

            Lessons.Move(currentIndex, newIndex);
            for (var index = 0; index < Lessons.Count; index++)
            {
                Lessons[index].SetSortOrder(index);
            }

            OrderStatus = "Lesson order saved.";
            OnPropertyChanged(nameof(SelectedLessonPositionText));
        }
        finally
        {
            _isReordering = false;
            _lessonOrderGate.Release();
            NotifyLessonOrderCommands();
        }
    }

    private void NotifyLessonOrderCommands()
    {
        ((AsyncRelayCommand)MoveLessonUpCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)MoveLessonDownCommand).NotifyCanExecuteChanged();
    }

    private void OpenSelectedLesson()
    {
        var lesson = SelectedLesson;
        if (lesson is null)
        {
            return;
        }

        _player.SetMedia(new MediaPlaybackRequest(
            lesson.FilePath,
            lesson.IsCompleted ? 0 : lesson.PlaybackPositionSeconds,
            lesson.MediaItemId,
            lesson.RuntimeSeconds));
    }

    private void OnPlaybackProgressPersisted(object? sender, PlaybackProgressSavedEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() =>
            {
                if (Lessons.FirstOrDefault(candidate => candidate.MediaItemId == args.MediaItemId) is { } lesson)
                {
                    lesson.SetPlaybackProgress(args);
                    RefreshCourseProgress();
                }
            });
            return;
        }

        if (Lessons.FirstOrDefault(candidate => candidate.MediaItemId == args.MediaItemId) is { } currentLesson)
        {
            currentLesson.SetPlaybackProgress(args);
            RefreshCourseProgress();
        }
    }

    private async void OnPlaybackCompleted(object? sender, PlaybackCompletedEventArgs args)
    {
        try
        {
            await _player.FlushPendingProgressSaveAsync();
            var lesson = Lessons.FirstOrDefault(candidate => candidate.MediaItemId == args.MediaItemId);
            if (lesson is null)
            {
                return;
            }

            if (!lesson.IsCompleted)
            {
                if (!await _playbackProgressService.SetCompletionAsync(args.MediaItemId, true))
                {
                    return;
                }

                lesson.SetCompletion(true);
            }

            _player.SynchronizeCompletion(args.MediaItemId, true);
            RefreshCourseProgress();

            var nextLesson = FindNextIncompleteLesson(lesson);
            if (nextLesson is null ||
                _confirmationDialog is not null &&
                !_confirmationDialog.Confirm(
                    $"Continue to the next lesson, \"{nextLesson.Title}\"?",
                    "Continue learning"))
            {
                return;
            }

            SelectedLesson = nextLesson;
        }
        catch (Exception exception)
        {
            // Playback completion must not take down the UI if the media is removed while playing.
            _logger?.LogWarning(
                exception,
                "Playback completion could not be synchronized for tutorial media item {MediaItemId}.",
                args.MediaItemId);
        }
    }

    private TutorialLessonViewModel? FindNextIncompleteLesson(TutorialLessonViewModel completedLesson)
    {
        var completedIndex = Lessons.IndexOf(completedLesson);
        if (completedIndex < 0)
        {
            return null;
        }

        return Lessons
            .Skip(completedIndex + 1)
            .FirstOrDefault(lesson => !lesson.IsCompleted);
    }

    private void RefreshCourseProgress()
    {
        OnPropertyChanged(nameof(CompletedLessonCount));
        OnPropertyChanged(nameof(CourseProgressPercentage));
        OnPropertyChanged(nameof(CourseProgressText));
        OnPropertyChanged(nameof(RemainingDurationText));
        OnPropertyChanged(nameof(IsCourseCompleted));
        OnPropertyChanged(nameof(HasIncompleteLessons));
        OnPropertyChanged(nameof(ContinueLearningText));
        OnPropertyChanged(nameof(CompletionActionText));
        ((AsyncRelayCommand)ToggleLessonCompletionCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ContinueLearningCommand).NotifyCanExecuteChanged();
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
    public Guid LessonId => lesson.Id;

    public string Title => MediaDisplayText.TitleOrFallback(lesson.Title, "Untitled lesson");

    public string Position => lesson.LessonNumber is { } number ? $"Lesson {number}" : $"Lesson {lesson.SortOrder + 1}";

    public string Runtime => MediaRuntimeFormatter.Format(lesson.MediaItem.RuntimeSeconds);

    /// <summary>Gets the known duration in seconds, or zero when it is unavailable.</summary>
    public long RuntimeSeconds => lesson.MediaItem.RuntimeSeconds.GetValueOrDefault();

    /// <summary>Gets the saved playback position in seconds.</summary>
    public long PlaybackPositionSeconds => lesson.MediaItem.PlaybackPositionSeconds;

    public string FilePath => lesson.FilePath;

    public Guid MediaItemId => lesson.MediaItemId;

    public bool IsFavorite => lesson.MediaItem.IsFavorite;

    /// <summary>Gets whether the learner has completed this lesson.</summary>
    public bool IsCompleted => lesson.MediaItem.IsCompleted;

    public string CompletionStatus => IsCompleted
        ? "Completed"
        : MediaPlaybackProgress.HasPartialProgress(lesson.MediaItem)
            ? MediaPlaybackProgress.DisplayText(lesson.MediaItem)
            : "Not started";

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

    internal void SetPlaybackProgress(PlaybackProgressSavedEventArgs args)
    {
        lesson.MediaItem.PlaybackPositionSeconds = args.PositionSeconds;
        lesson.MediaItem.RuntimeSeconds = args.DurationSeconds;
        lesson.MediaItem.IsCompleted = MediaPlaybackProgress.MeetsCompletionThreshold(
            args.PositionSeconds,
            args.DurationSeconds);
        lesson.MediaItem.LastPlayed = args.LastWatched;
        OnPropertyChanged(nameof(PlaybackPositionSeconds));
        OnPropertyChanged(nameof(RuntimeSeconds));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CompletionStatus));
    }

    internal void SetSortOrder(int sortOrder)
    {
        if (lesson.SortOrder == sortOrder)
        {
            return;
        }

        lesson.SortOrder = sortOrder;
        OnPropertyChanged(nameof(Position));
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
