using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Coordinates category listing, creation, renaming, deletion, and media counts.
/// </summary>
public sealed class CategoriesPageViewModel : PageViewModel
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly ICreateCategoryDialog _createCategoryDialog;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _statusMessage = string.Empty;

    public CategoriesPageViewModel(
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IConfirmationDialog confirmationDialog,
        ICreateCategoryDialog createCategoryDialog,
        IMediaItemRepository mediaItemRepository)
    {
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _confirmationDialog = confirmationDialog;
        _createCategoryDialog = createCategoryDialog;
        _mediaItemRepository = mediaItemRepository;

        CreateCategoryCommand = new AsyncRelayCommand(CreateCategoryAsync);
        RenameCategoryCommand = new AsyncRelayCommand(RenameCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _categoryService.CategoriesChanged += OnCategoriesChanged;
    }

    public override string Title => "Categories";

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = [];

    public ICommand CreateCategoryCommand { get; }

    public ICommand RenameCategoryCommand { get; }

    public ICommand DeleteCategoryCommand { get; }

    public ICommand RefreshCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CategoryCountText => $"{Categories.Count} categor{(Categories.Count == 1 ? "y" : "ies")}";

    public bool HasCategories => Categories.Count != 0;

    /// <summary>Loads the current categories and their assigned-media counts.</summary>
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync();
        try
        {
            var categoriesTask = _categoryRepository.GetAllAsync();
            var mediaItemsTask = _mediaItemRepository.GetAllAsync();
            await Task.WhenAll(categoriesTask, mediaItemsTask);

            var mediaCounts = mediaItemsTask.Result
                .Where(mediaItem => mediaItem.CategoryId is not null)
                .GroupBy(mediaItem => mediaItem.CategoryId!.Value)
                .ToDictionary(group => group.Key, group => group.Count());

            Categories.Clear();
            foreach (var category in categoriesTask.Result.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase))
            {
                Categories.Add(new CategoryItemViewModel(
                    category,
                    mediaCounts.GetValueOrDefault(category.Id)));
            }

            OnPropertyChanged(nameof(CategoryCountText));
            OnPropertyChanged(nameof(HasCategories));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = "Categories could not be loaded. Try refreshing again.";
        }
        finally
        {
            _refreshGate.Release();
        }
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

    private async Task RenameCategoryAsync(object? parameter)
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

        if (CategoryNameExists(name, category.Id))
        {
            StatusMessage = "A category with that name already exists.";
            return;
        }

        try
        {
            if (await _categoryService.RenameAsync(category.Id, name))
            {
                StatusMessage = $"Category renamed to '{name}'.";
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
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(RefreshAsync);
            return;
        }

        _ = RefreshAsync();
    }
}
