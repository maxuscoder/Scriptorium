using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private const string DefaultCategoryColor = "#CC4B08";

    private readonly ICategoryRepository _categoryRepository;
    private readonly ICategoryService _categoryService;
    private readonly IConfirmationDialog _confirmationDialog;
    private readonly IMediaItemRepository _mediaItemRepository;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private string _newCategoryColor = DefaultCategoryColor;
    private string _newCategoryName = string.Empty;
    private string _statusMessage = string.Empty;

    public CategoriesPageViewModel(
        ICategoryRepository categoryRepository,
        ICategoryService categoryService,
        IConfirmationDialog confirmationDialog,
        IMediaItemRepository mediaItemRepository)
    {
        _categoryRepository = categoryRepository;
        _categoryService = categoryService;
        _confirmationDialog = confirmationDialog;
        _mediaItemRepository = mediaItemRepository;

        CreateCategoryCommand = new AsyncRelayCommand(CreateCategoryAsync, CanCreateCategory);
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

    public string NewCategoryName
    {
        get => _newCategoryName;
        set
        {
            if (SetProperty(ref _newCategoryName, value ?? string.Empty))
            {
                ((AsyncRelayCommand)CreateCategoryCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string NewCategoryColor
    {
        get => _newCategoryColor;
        set
        {
            if (SetProperty(ref _newCategoryColor, value ?? string.Empty))
            {
                ((AsyncRelayCommand)CreateCategoryCommand).NotifyCanExecuteChanged();
            }
        }
    }

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

    private bool CanCreateCategory() =>
        !string.IsNullOrWhiteSpace(NewCategoryName) &&
        TryParseColor(NewCategoryColor, out _);

    private async Task CreateCategoryAsync()
    {
        var name = NewCategoryName.Trim();
        var color = NewCategoryColor.Trim();
        if (!CanCreateCategory() || CategoryNameExists(name, null))
        {
            StatusMessage = CategoryNameExists(name, null)
                ? "A category with that name already exists."
                : "Enter a category name and a valid hex color such as #CC4B08.";
            return;
        }

        await _categoryService.CreateAsync(name, color);
        NewCategoryName = string.Empty;
        NewCategoryColor = DefaultCategoryColor;
        StatusMessage = $"Category '{name}' created.";
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

        if (await _categoryService.RenameAsync(category.Id, name))
        {
            StatusMessage = $"Category renamed to '{name}'.";
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

        if (await _categoryService.DeleteAsync(category.Id))
        {
            StatusMessage = $"Category '{category.Name}' deleted.";
        }
    }

    private bool CategoryNameExists(string name, Guid? excludedCategoryId)
    {
        return Categories.Any(category =>
            category.Id != excludedCategoryId &&
            string.Equals(category.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (FormatException)
        {
            // Invalid user input is handled by the validation message.
        }

        return false;
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
