using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows.Input;
using Scriptorium.App.Commands;
using Scriptorium.App.Services;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Core.Services;

namespace Scriptorium.App.ViewModels.Pages;

public sealed class PrivateLibraryPageViewModel : PageViewModel, IDisposable
{
    private readonly IPrivateLibrary _library;
    private readonly IMediaItemRepository _publicMedia;
    private readonly IPrivateLibraryDialogs _dialogs;
    private readonly IConfirmationDialog _confirmation;
    private readonly IMediaFormatService _formats;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private DateTimeOffset _lastInteraction = DateTimeOffset.UtcNow;
    private bool _busy;
    private bool _configured;
    private bool _unlocked;
    private bool _encrypted;
    private int _autoLockMinutes = 15;
    private string _status = string.Empty;
    private string _query = string.Empty;
    private bool _favoritesOnly;
    private double _progress;
    private PrivateMedia? _selected;
    private string _editedTitle = string.Empty;
    private MediaItem? _selectedPublic;
    private CancellationTokenSource? _operation;
    private bool _savingProgress;
    private bool _disposed;
    public override string Title => "Private Library";
    public VideoPlayerViewModel Player { get; }
    public ObservableCollection<PrivateMedia> Items { get; } = [];
    public ObservableCollection<MediaItem> PublicItems { get; } = [];
    public ListCollectionView FilteredItems { get; }
    public bool IsConfigured { get => _configured; private set { SetProperty(ref _configured, value); OnPropertyChanged(nameof(NeedsSetup)); } }
    public bool NeedsSetup => !IsConfigured;
    public bool IsUnlocked { get => _unlocked; private set { SetProperty(ref _unlocked, value); OnPropertyChanged(nameof(IsLocked)); } }
    public bool IsLocked => !IsUnlocked;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public bool UseEncryption { get => _encrypted; set { SetProperty(ref _encrypted, value); OnPropertyChanged(nameof(UseStandardProtection)); OnPropertyChanged(nameof(ModeDescription)); OnPropertyChanged(nameof(CanRemoveOriginal)); } }
    public bool UseStandardProtection { get => !UseEncryption; set { if (value) UseEncryption = false; } }
    public string ModeDescription => UseEncryption ? "Encrypted Protection · AES-256-GCM vault" : "Standard Protection · hidden in Scriptorium";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Query { get => _query; set { SetProperty(ref _query, value); RefreshResults(); } }
    public string ResultSummary => $"{FilteredItems.Count} of {Items.Count} private item(s)";
    public bool HasNoResults => FilteredItems.IsEmpty;
    public string EmptyMessage => Items.Count == 0 ? "Your Private Library is empty. Import files or choose existing media to get started." : "No private media matches this search.";
    public int AutoLockMinutes { get => _autoLockMinutes; set => SetProperty(ref _autoLockMinutes, value); }
    public IReadOnlyList<int> AutoLockOptions { get; } = [0, 1, 5, 15, 30, 60, 120];
    public PrivateMedia? SelectedItem
    {
        get => _selected;
        set { SetProperty(ref _selected, value); EditedTitle = value?.Media.DisplayTitle ?? string.Empty; OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanRemoveOriginal)); }
    }
    public MediaItem? SelectedPublicItem { get => _selectedPublic; set => SetProperty(ref _selectedPublic, value); }
    public bool HasSelection => SelectedItem is not null;
    public bool CanRemoveOriginal => UseEncryption && SelectedItem?.OriginalRetained == true;
    public string EditedTitle { get => _editedTitle; set => SetProperty(ref _editedTitle, value); }
    public AsyncRelayCommand ImportFilesCommand { get; }
    public AsyncRelayCommand ImportFolderCommand { get; }
    public AsyncRelayCommand LoadPublicItemsCommand { get; }
    public AsyncRelayCommand MoveExistingCommand { get; }
    public AsyncRelayCommand PlayCommand { get; }
    public AsyncRelayCommand ReturnCommand { get; }
    public AsyncRelayCommand RemoveOriginalCommand { get; }
    public AsyncRelayCommand SaveMetadataCommand { get; }
    public AsyncRelayCommand SaveLockSettingsCommand { get; }
    public AsyncRelayCommand LockCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClosePlayerCommand { get; }

    public PrivateLibraryPageViewModel(IPrivateLibrary library, IMediaItemRepository publicMedia, IPrivateLibraryDialogs dialogs,
        IConfirmationDialog confirmation, IMediaFormatService formats, VideoPlayerViewModel player)
    {
        _library = library;
        _publicMedia = publicMedia;
        _dialogs = dialogs;
        _confirmation = confirmation;
        _formats = formats;
        Player = player;
        FilteredItems = new ListCollectionView(Items) { Filter = value => value is PrivateMedia item &&
        (!FavoritesOnly || item.Media.IsFavorite) && (item.Media.DisplayTitle.Contains(Query, StringComparison.CurrentCultureIgnoreCase) || item.Group.Contains(Query, StringComparison.CurrentCultureIgnoreCase)) };
        ImportFilesCommand = new AsyncRelayCommand(() => RunAsync(() => ImportPathsAsync(_dialogs.PickFiles())), () => IsUnlocked && !IsBusy);
        ImportFolderCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            var folder = _dialogs.PickFolder();
            if (folder is null) return;
            var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true,
                IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint });
            await ImportPathsAsync(files);
        }), () => IsUnlocked && !IsBusy);
        LoadPublicItemsCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            PublicItems.Clear();
            foreach (var item in await _publicMedia.GetAllAsync(_operation!.Token)) PublicItems.Add(item);
        }), () => IsUnlocked && !IsBusy);
        MoveExistingCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            if (SelectedPublicItem is { } item) await ImportPathsAsync([item.Path]);
        }), () => IsUnlocked && !IsBusy);
        PlayCommand = new AsyncRelayCommand(parameter => RunAsync(async () =>
        {
            var item = parameter as PrivateMedia ?? SelectedItem;
            if (item is null) return;
            await SaveProgressAsync();
            SelectedItem = item;
            _playingItem = item;
            OnPropertyChanged(nameof(IsPlayerVisible));
            Player.SetMedia(new("private://media", item.Media.PlaybackPositionSeconds,
                OpenPrivateStream: token => _library.OpenReadAsync(item.Id, token)));
        }), _ => IsUnlocked && !IsBusy);
        ReturnCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            if (SelectedItem is not { } item) return;
            if (!_confirmation.Confirm("Return this item to the regular library? Its metadata and playback history will become visible in public sections. Encrypted media will be exported as a readable file.", "Return to library")) return;
            var destination = UseEncryption ? _dialogs.PickExportPath(Path.GetFileName(item.Media.Path)) : null;
            if (UseEncryption && destination is null) return;
            await SaveProgressAsync();
            await Player.DeactivateAsync();
            _playingItem = null;
            OnPropertyChanged(nameof(IsPlayerVisible));
            Player.SetUnavailable("Select a private video.");
            await _library.ReturnToLibraryAsync(item.Id, destination, NewProgress(), _operation!.Token);
            await ReloadAsync();
            Status = "Returned to the regular library.";
        }), () => IsUnlocked && !IsBusy);
        SaveMetadataCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            if (SelectedItem is not { } item) return;
            if (string.IsNullOrWhiteSpace(EditedTitle)) throw new ArgumentException("Enter a title.");
            item.Media.TitleOverride = EditedTitle.Trim();
            await _library.UpdateAsync(item, _operation!.Token);
            await ReloadAsync();
            Status = "Private details saved.";
        }), () => IsUnlocked && !IsBusy);
        RemoveOriginalCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            if (!UseEncryption || SelectedItem is not { } item) return;
            if (!_confirmation.Confirm("Remove the readable original file? Scriptorium will first authenticate the entire encrypted copy and verify it matches the locked original. Removal is permanent and does not use the Recycle Bin. Keep a backup of your vault and database. Other copies, backups, and recoverable disk remnants are not erased.", "Remove verified original")) return;
            Status = "Verifying the vault against the original before removal…";
            await _library.RemoveOriginalAsync(item.Id, _operation!.Token);
            await ReloadAsync();
            Status = "The matching original was removed. The encrypted vault copy is ready for private playback.";
        }), () => IsUnlocked && !IsBusy);
        SaveLockSettingsCommand = new AsyncRelayCommand(() => RunAsync(async () =>
        {
            await _library.SetAutoLockAsync(AutoLockMinutes, _operation!.Token);
            Status = "Automatic lock setting saved.";
        }), () => IsUnlocked && !IsBusy);
        LockCommand = new AsyncRelayCommand(() => { _library.Lock(); return Task.CompletedTask; });
        CancelCommand = new RelayCommand(() => _operation?.Cancel());
        ClosePlayerCommand = new RelayCommand(() =>
        {
            _ = SaveProgressAsync();
            Player.SetUnavailable("Select a private video.");
            _playingItem = null;
            OnPropertyChanged(nameof(IsPlayerVisible));
        });
        _library.Locked += OnLocked;
        _timer.Tick += OnTimer;
        _timer.Start();
        InputManager.Current.PreProcessInput += OnInput;
    }

    private PrivateMedia? _playingItem;
    public bool IsPlayerVisible => _playingItem is not null && IsUnlocked;
    public bool FavoritesOnly { get => _favoritesOnly; set { SetProperty(ref _favoritesOnly, value); RefreshResults(); } }
    private void RefreshResults()
    {
        FilteredItems.Refresh();
        OnPropertyChanged(nameof(ResultSummary)); OnPropertyChanged(nameof(HasNoResults)); OnPropertyChanged(nameof(EmptyMessage));
    }
    private void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyEventArgs or MouseEventArgs) NotifyInteraction();
    }

    public async Task InitializeAsync()
    {
        await RunAsync(async () =>
        {
            var status = await _library.GetStatusAsync();
            IsConfigured = status.IsConfigured;
            IsUnlocked = status.IsUnlocked;
            UseEncryption = status.Mode == PrivateProtectionMode.Encrypted;
            AutoLockMinutes = status.AutoLockMinutes;
            if (IsUnlocked) await ReloadAsync();
        });
    }

    public Task AuthenticateAsync(ReadOnlyMemory<byte> password, ReadOnlyMemory<byte> confirmation) => RunAsync(async () =>
    {
        if (!IsConfigured)
        {
            if (!password.Span.SequenceEqual(confirmation.Span)) throw new ArgumentException("The passwords do not match.");
            await _library.SetupAsync(UseEncryption ? PrivateProtectionMode.Encrypted : PrivateProtectionMode.Standard, password, _operation!.Token);
        }
        else if (!await _library.UnlockAsync(password, _operation!.Token)) { Status = "The password is incorrect, or the library key is damaged."; return; }
        var status = await _library.GetStatusAsync();
        IsConfigured = status.IsConfigured;
        IsUnlocked = status.IsUnlocked;
        AutoLockMinutes = status.AutoLockMinutes;
        _lastInteraction = DateTimeOffset.UtcNow;
        await ReloadAsync();
        Status = UseEncryption ? "Unlocked. Imported originals remain readable until you use Remove verified original or remove them yourself." : "Unlocked. Standard Protection hides content only inside Scriptorium.";
    });

    public Task ChangePasswordAsync(ReadOnlyMemory<byte> current, ReadOnlyMemory<byte> next, ReadOnlyMemory<byte> confirmation) => RunAsync(async () =>
    {
        if (!next.Span.SequenceEqual(confirmation.Span)) throw new ArgumentException("The new passwords do not match.");
        await _library.ChangePasswordAsync(current, next, _operation!.Token);
        Status = "Password changed. Media files did not need to be re-encrypted.";
    });

    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var count = 0;
        foreach (var path in paths)
        {
            _operation!.Token.ThrowIfCancellationRequested();
            if (!_formats.IsSupportedExtension(Path.GetExtension(path))) continue;
            Status = "Importing private media. Your original file will be kept.";
            await _library.ImportAsync(path, NewProgress(), _operation.Token);
            count++;
            // Remove references to the moved record from the public picker immediately.
            var old = PublicItems.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (old is not null) PublicItems.Remove(old);
        }
        await ReloadAsync();
        Status = $"Imported {count} file(s)." + (UseEncryption ? " Encrypted copies are protected; retained originals remain readable outside Scriptorium. Select an item and use Remove verified original when ready." : string.Empty);
    }

    private IProgress<double> NewProgress() => new Progress<double>(value => Progress = Math.Clamp(value * 100, 0, 100));

    private async Task ReloadAsync()
    {
        var items = await _library.BrowseAsync(_operation?.Token ?? default);
        if (!IsUnlocked) return;
        var selectedId = SelectedItem?.Id;
        Items.Clear();
        foreach (var item in items.OrderBy(i => i.Media.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)) Items.Add(item);
        SelectedItem = Items.FirstOrDefault(i => i.Id == selectedId);
        if (_playingItem is { } playing) _playingItem = Items.FirstOrDefault(i => i.Id == playing.Id);
        RefreshResults();
    }

    public void NotifyInteraction() => _lastInteraction = DateTimeOffset.UtcNow;

    private async void OnTimer(object? sender, EventArgs args)
    {
        if (!IsUnlocked) return;
        if (AutoLockMinutes > 0 && DateTimeOffset.UtcNow - _lastInteraction >= TimeSpan.FromMinutes(AutoLockMinutes))
        {
            _library.Lock();
            return;
        }
        if (!IsBusy && !_savingProgress) await SaveProgressAsync();
    }

    private async Task SaveProgressAsync()
    {
        if (_savingProgress || !IsUnlocked || _playingItem is not { } item || !Player.IsReady) return;
        _savingProgress = true;
        try
        {
            item.Media.PlaybackPositionSeconds = (long)Player.PositionSeconds;
            item.Media.RuntimeSeconds = (long)Player.DurationSeconds;
            item.Media.LastPlayed = DateTimeOffset.UtcNow;
            item.Media.LastPlayedUnixTimeMilliseconds = item.Media.LastPlayed.Value.ToUnixTimeMilliseconds();
            item.Media.IsCompleted = MediaPlaybackProgress.MeetsCompletionThreshold(item.Media.PlaybackPositionSeconds, item.Media.RuntimeSeconds.Value);
            await _library.UpdateAsync(item);
        }
        catch (Exception) { /* Locking must remain possible even if history cannot be saved. */ }
        finally { _savingProgress = false; }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        _operation = new();
        Progress = 0;
        try { await action(); }
        catch (OperationCanceledException) { Status = "Operation cancelled. Committed imports and original files are safe."; }
        catch (System.Security.Cryptography.CryptographicException) { Status = "Authentication failed or private data is damaged. No files were overwritten."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            // Filesystem exception messages may contain private paths. Keep them out of notifications/logs.
            Status = ex is ArgumentException or InvalidOperationException ? ex.Message : "The private operation could not finish. Check file access and free space, then try again. Original files were kept.";
        }
        catch (Exception) { Status = "The private operation could not finish. Your originals and committed vault files were kept. Try again after restarting Scriptorium."; }
        finally { _operation.Dispose(); _operation = null; IsBusy = false; NotifyCommands(); }
    }

    private void OnLocked(object? sender, EventArgs args)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.Invoke(() => OnLocked(sender, args)); return; }
        _operation?.Cancel();
        IsUnlocked = false;
        Player.SetUnavailable("Private Library locked.");
        _playingItem = null;
        OnPropertyChanged(nameof(IsPlayerVisible));
        SelectedItem = null;
        SelectedPublicItem = null;
        Items.Clear();
        PublicItems.Clear();
        Query = string.Empty;
        Status = "Private Library locked.";
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        foreach (var command in new[] { ImportFilesCommand, ImportFolderCommand, LoadPublicItemsCommand, MoveExistingCommand,
            PlayCommand, ReturnCommand, SaveMetadataCommand, SaveLockSettingsCommand, RemoveOriginalCommand }) command.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimer;
        InputManager.Current.PreProcessInput -= OnInput;
        _library.Locked -= OnLocked;
        _operation?.Cancel();
        Player.Dispose();
        Items.Clear();
        PublicItems.Clear();
    }
}
