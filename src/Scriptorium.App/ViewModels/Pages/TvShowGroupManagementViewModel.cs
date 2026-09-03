using System.Collections.ObjectModel;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

/// <summary>
/// Owns the selection state and operations used to manually organize detected TV-show groups.
/// </summary>
public sealed class TvShowGroupManagementViewModel : ViewModelBase
{
    private readonly IMediaGroupingService _mediaGroupingService;
    private readonly Func<Task> _refreshLibraryData;
    private readonly Action<string> _setStatusMessage;
    private readonly AsyncRelayCommand _renameGroupCommand;
    private readonly AsyncRelayCommand _moveMediaToGroupCommand;
    private readonly AsyncRelayCommand _mergeGroupsCommand;
    private readonly AsyncRelayCommand _splitGroupCommand;
    private ManualTvShowGroupViewModel? _selectedGroup;
    private ManualTvShowGroupViewModel? _targetGroup;
    private ManualTvShowMediaViewModel? _selectedMedia;
    private string? _groupName;

    public TvShowGroupManagementViewModel(
        IMediaGroupingService mediaGroupingService,
        Func<Task> refreshLibraryData,
        Action<string> setStatusMessage)
    {
        _mediaGroupingService = mediaGroupingService;
        _refreshLibraryData = refreshLibraryData;
        _setStatusMessage = setStatusMessage;

        _renameGroupCommand = new AsyncRelayCommand(RenameSelectedGroupAsync, CanRenameSelectedGroup);
        RenameGroupCommand = _renameGroupCommand;
        _moveMediaToGroupCommand = new AsyncRelayCommand(MoveSelectedMediaToGroupAsync, CanMoveSelectedMediaToGroup);
        MoveMediaToGroupCommand = _moveMediaToGroupCommand;
        _mergeGroupsCommand = new AsyncRelayCommand(MergeSelectedGroupsAsync, CanMergeSelectedGroups);
        MergeGroupsCommand = _mergeGroupsCommand;
        _splitGroupCommand = new AsyncRelayCommand(SplitSelectedGroupAsync, CanSplitSelectedGroup);
        SplitGroupCommand = _splitGroupCommand;
    }

    public ObservableCollection<ManualTvShowGroupViewModel> Groups { get; } = [];

    public ManualTvShowGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                SelectedMedia = null;
                GroupName = value?.Title;
                NotifyCommands();
            }
        }
    }

    public ManualTvShowGroupViewModel? TargetGroup
    {
        get => _targetGroup;
        set
        {
            if (SetProperty(ref _targetGroup, value))
            {
                NotifyCommands();
            }
        }
    }

    public ManualTvShowMediaViewModel? SelectedMedia
    {
        get => _selectedMedia;
        set
        {
            if (SetProperty(ref _selectedMedia, value))
            {
                NotifyCommands();
            }
        }
    }

    public string? GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                NotifyCommands();
            }
        }
    }

    public ICommand RenameGroupCommand { get; }

    public ICommand MoveMediaToGroupCommand { get; }

    public ICommand MergeGroupsCommand { get; }

    public ICommand SplitGroupCommand { get; }

    public async Task RefreshAsync()
    {
        var selectedGroupId = SelectedGroup?.Id;
        var targetGroupId = TargetGroup?.Id;
        Groups.Clear();
        foreach (var group in await _mediaGroupingService.GetTvShowGroupsAsync())
        {
            Groups.Add(new ManualTvShowGroupViewModel(group));
        }

        SelectedGroup = selectedGroupId is { } selectedId
            ? Groups.SingleOrDefault(group => group.Id == selectedId)
            : null;
        TargetGroup = targetGroupId is { } targetId
            ? Groups.SingleOrDefault(group => group.Id == targetId)
            : null;
    }

    private bool CanRenameSelectedGroup() =>
        SelectedGroup is not null && !string.IsNullOrWhiteSpace(GroupName);

    private bool CanMoveSelectedMediaToGroup() =>
        SelectedMedia is not null &&
        SelectedGroup is not null &&
        TargetGroup is { } targetGroup &&
        targetGroup.Id != SelectedGroup.Id;

    private bool CanMergeSelectedGroups() =>
        SelectedGroup is not null &&
        TargetGroup is { } targetGroup &&
        targetGroup.Id != SelectedGroup.Id;

    private bool CanSplitSelectedGroup() =>
        SelectedGroup is not null &&
        SelectedMedia is not null &&
        !string.IsNullOrWhiteSpace(GroupName);

    private Task RenameSelectedGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.RenameTvShowGroupAsync(SelectedGroup!.Id, GroupName!),
        "Group renamed and library refreshed.");

    private Task MoveSelectedMediaToGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.MoveEpisodeAsync(SelectedMedia!.MediaItemId, TargetGroup!.Id),
        "Media moved and library refreshed.");

    private Task MergeSelectedGroupsAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.MergeTvShowGroupsAsync(SelectedGroup!.Id, TargetGroup!.Id),
        "Groups merged and library refreshed.");

    private Task SplitSelectedGroupAsync() => ApplyGroupingChangeAsync(
        () => _mediaGroupingService.SplitTvShowGroupAsync(
            SelectedGroup!.Id,
            [SelectedMedia!.MediaItemId],
            GroupName!),
        "New group created and library refreshed.");

    private async Task ApplyGroupingChangeAsync(Func<Task> change, string successMessage)
    {
        try
        {
            await change();
            SelectedGroup = null;
            TargetGroup = null;
            await _refreshLibraryData();
            _setStatusMessage(successMessage);
        }
        catch (ArgumentException exception)
        {
            _setStatusMessage(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            _setStatusMessage(exception.Message);
        }
    }

    private void NotifyCommands()
    {
        _renameGroupCommand.NotifyCanExecuteChanged();
        _moveMediaToGroupCommand.NotifyCanExecuteChanged();
        _mergeGroupsCommand.NotifyCanExecuteChanged();
        _splitGroupCommand.NotifyCanExecuteChanged();
    }
}
