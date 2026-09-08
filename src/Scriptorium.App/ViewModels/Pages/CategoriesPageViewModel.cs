using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Coordinates category listing, creation, renaming, deletion, and media counts.
/// </summary>
public sealed class CategoriesPageViewModel : PageViewModel, IDisposable
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly ICreateCategoryDialog _createCategoryDialog;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly IFavoriteService _favoriteService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _statusMessage = string.Empty;
    private CategoryItemViewModel? _selectedCategory;
    private bool _disposed;
    private IReadOnlyList<MediaItem> _availableMediaItems = [];
    private bool _isRefreshing;
    private int _isCategoryRefreshQueued;
    private int _isCategoryRefreshRequested;

    public CategoriesPageViewModel(
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IConfirmationDialog confirmationDialog,
        ICreateCategoryDialog createCategoryDialog,
        IMediaItemRepository mediaItemRepository,
        IFavoriteService favoriteService)
    {
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _confirmationDialog = confirmationDialog;
        _createCategoryDialog = createCategoryDialog;
        _mediaItemRepository = mediaItemRepository;
        _favoriteService = favoriteService;

        CreateCategoryCommand = new AsyncRelayCommand(CreateCategoryAsync);
        RenameCategoryCommand = new AsyncRelayCommand(SaveCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SelectCategoryCommand = new RelayCommand(SelectCategory, parameter => parameter is CategoryItemViewModel);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync, parameter => parameter is IMediaFavoriteItem);
        _favoriteService.FavoriteChanged += OnFavoriteChanged;
        _categoryService.CategoriesChanged += OnCategoriesChanged;
    }

    public override string Title => "Categories";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _favoriteService.FavoriteChanged -= OnFavoriteChanged;
        _categoryService.CategoriesChanged -= OnCategoriesChanged;
        if (_selectedCategory is not null)
        {
            _selectedCategory.PropertyChanged -= OnSelectedCategoryPropertyChanged;
        }
    }

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = [];

    public ICommand CreateCategoryCommand { get; }

    public ICommand RenameCategoryCommand { get; }

    public ICommand DeleteCategoryCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>Gets the command that changes the category currently shown in the browser.</summary>
    public ICommand SelectCategoryCommand { get; }

    public ICommand ToggleFavoriteCommand { get; }

    /// <summary>Gets the media assigned to the selected category.</summary>
    public ObservableCollection<LibraryMediaItemViewModel> MediaItems { get; } = [];

    /// <summary>Gets the category whose media is currently displayed.</summary>
    public CategoryItemViewModel? SelectedCategory
    {
        get => _selectedCategory;
        private set
        {
            var previousCategory = _selectedCategory;
            if (!SetProperty(ref _selectedCategory, value))
            {
                return;
            }

            if (previousCategory is not null)
            {
                previousCategory.PropertyChanged -= OnSelectedCategoryPropertyChanged;
            }

            if (value is not null)
            {
                value.PropertyChanged += OnSelectedCategoryPropertyChanged;
            }

            foreach (var category in Categories)
            {
                category.IsSelected = ReferenceEquals(category, value);
            }

            RefreshSelectedCategoryMedia();
            OnPropertyChanged(nameof(HasSelectedCategory));
            OnPropertyChanged(nameof(SelectedCategoryName));
            OnPropertyChanged(nameof(SelectedCategoryMediaCountText));
            OnPropertyChanged(nameof(SelectedCategoryEmptyTitle));
            OnPropertyChanged(nameof(SelectedCategoryEmptyDescription));
        }
    }

    /// <summary>Gets whether a category is active in the browser.</summary>
    public bool HasSelectedCategory => SelectedCategory is not null;

    public string SelectedCategoryName => SelectedCategory?.Name ?? string.Empty;

    public string SelectedCategoryMediaCountText => SelectedCategory is null
        ? string.Empty
        : $"{MediaItems.Count} media item{(MediaItems.Count == 1 ? string.Empty : "s")}";

    public bool HasMediaItems => MediaItems.Count != 0;

    /// <summary>Gets whether category data is being loaded.</summary>
    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CategoryCountText => $"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}";

    public bool HasCategories => Categories.Count != 0;

    public string SelectedCategoryEmptyTitle => SelectedCategory is null
        ? "Select a category"
        : $"{SelectedCategory.Name} is empty";

    public string SelectedCategoryEmptyDescription => SelectedCategory is null
        ? "Choose a category above to browse its media."
        : "Media assigned to this category will appear here.";

    /// <summary>Loads the current categories and their assigned-media counts.</summary>
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        IsRefreshing = true;
        try
        {
            var categoriesTask = _categoryRepository.GetAllAsync();
            var mediaItemsTask = _mediaItemRepository.GetAllAsync();
            await Task.WhenAll(categoriesTask, mediaItemsTask);

            var selectedCategoryId = SelectedCategory?.Id;
            _availableMediaItems = mediaItemsTask.Result;

            Categories.Clear();
            foreach (var category in categoriesTask.Result.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase))
            {
                var assignedMedia = _availableMediaItems
                    .Where(mediaItem => mediaItem.CategoryId == category.Id)
                    .ToArray();
                Categories.Add(new CategoryItemViewModel(
                    category,
                    assignedMedia));
            }

            SelectedCategory = selectedCategoryId is { } categoryId
                ? Categories.FirstOrDefault(category => category.Id == categoryId) ?? Categories.FirstOrDefault()
                : Categories.FirstOrDefault();

            OnPropertyChanged(nameof(CategoryCountText));
            OnPropertyChanged(nameof(HasCategories));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = "Categories could not be loaded. Try refreshing again.";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private void SelectCategory(object? parameter)
    {
        if (parameter is CategoryItemViewModel category && Categories.Contains(category))
        {
            SelectedCategory = category;
        }
    }

    private async Task ToggleFavoriteAsync(object? parameter)
    {
        if (parameter is not IMediaFavoriteItem item)
        {
            return;
        }

        var isFavorite = !item.IsFavorite;
        var updated = isFavorite
            ? await _favoriteService.AddAsync(item.MediaItemId)
            : await _favoriteService.RemoveAsync(item.MediaItemId);
        if (updated)
        {
            item.SetFavorite(isFavorite);
        }
    }

    private void OnFavoriteChanged(Guid mediaItemId)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => RefreshFavoriteStateAsync(mediaItemId));
            return;
        }

        _ = RefreshFavoriteStateAsync(mediaItemId);
    }

    private async Task RefreshFavoriteStateAsync(Guid mediaItemId)
    {
        var item = MediaItems.FirstOrDefault(candidate => candidate.MediaItemId == mediaItemId);
        if (item is null)
        {
            return;
        }

        var mediaItem = await _mediaItemRepository.GetByIdAsync(mediaItemId);
        if (mediaItem is not null)
        {
            item.SetFavorite(mediaItem.IsFavorite);
        }
    }

    private void RefreshSelectedCategoryMedia()
    {
        MediaItems.Clear();

        if (SelectedCategory is not null)
        {
            foreach (var mediaItem in _availableMediaItems
                         .Where(mediaItem => mediaItem.CategoryId == SelectedCategory.Id)
                         .OrderBy(mediaItem => mediaItem.Title, StringComparer.OrdinalIgnoreCase))
            {
                MediaItems.Add(new LibraryMediaItemViewModel(mediaItem));
            }
        }

        OnPropertyChanged(nameof(HasMediaItems));
        OnPropertyChanged(nameof(SelectedCategoryMediaCountText));
    }

    private void OnSelectedCategoryPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, SelectedCategory) || eventArgs.PropertyName != nameof(CategoryItemViewModel.Name))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCategoryName));
        OnPropertyChanged(nameof(SelectedCategoryEmptyTitle));
    }

    private async Task CreateCategoryAsync()
    {
        var dialogResult = _createCategoryDialog.Show(Categories.Select(category => category.Name).ToArray());
        if (dialogResult is null)
        {
            return;
        }

        var name = dialogResult.Name.Trim();
        if (CategoryNameExists(name, null))
        {
            StatusMessage = "A category with that name already exists.";
            return;
        }

        try
        {
            await _categoryService.CreateAsync(name, dialogResult.Color.Trim());
            StatusMessage = $"Category '{name}' created.";
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "A category with that name already exists.";
        }
    }

    private async Task SaveCategoryAsync(object? parameter)
    {
        if (parameter is not CategoryItemViewModel category)
        {
            return;
        }

        var name = category.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Category names cannot be empty.";
            return;
        }

        if (!category.IsValidColor)
        {
            StatusMessage = "Enter a valid color such as #CC4B08.";
            return;
        }

        if (CategoryNameExists(name, category.Id))
        {
            StatusMessage = "A category with that name already exists.";
            return;
        }

        try
        {
            if (await _categoryService.UpdateAsync(category.Id, name, category.Color.Trim()))
            {
                StatusMessage = $"Category '{name}' saved.";
            }
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "A category with that name already exists.";
        }
    }

    private async Task DeleteCategoryAsync(object? parameter)
    {
        if (parameter is not CategoryItemViewModel category ||
            !_confirmationDialog.Confirm(
                $"Delete '{category.Name}'? Its {category.MediaCount} media assignment{(category.MediaCount == 1 ? string.Empty : "s")} will be cleared.",
                "Delete category"))
        {
            return;
        }

        var categoryName = category.Name;
        if (await _categoryService.DeleteAsync(category.Id))
        {
            StatusMessage = $"Category '{categoryName}' deleted.";
        }
    }

    private bool CategoryNameExists(string name, Guid? excludedCategoryId)
    {
        return Categories.Any(category =>
            category.Id != excludedCategoryId &&
            string.Equals(category.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private void OnCategoriesChanged()
    {
        Volatile.Write(ref _isCategoryRefreshRequested, 1);
        if (Interlocked.Exchange(ref _isCategoryRefreshQueued, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAfterCategoryChangeAsync);
            return;
        }

        _ = RefreshAfterCategoryChangeAsync();
    }

    private async Task RefreshAfterCategoryChangeAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _isCategoryRefreshRequested, 0) != 0)
            {
                await RefreshAsync();
            }
        }
        finally
        {
            Volatile.Write(ref _isCategoryRefreshQueued, 0);
            if (Volatile.Read(ref _isCategoryRefreshRequested) != 0)
            {
                OnCategoriesChanged();
            }
        }
    }
}
