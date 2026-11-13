using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure.Services;

namespace Scriptorium.App.Views.Controls;

public partial class NetworkDevices : UserControl
{
    private readonly IDeviceDiscovery _discovery = new UpnpDeviceDiscovery();
    private readonly ObservableCollection<DeviceRow> _devices = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? _scan;

    public NetworkDevices()
    {
        InitializeComponent();
        DeviceList.ItemsSource = _devices;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        _scan?.Cancel();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_scan is not null) return;
        using var scan = new CancellationTokenSource();
        _scan = scan;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Searching for devices…";
        try
        {
            var found = await _discovery.DiscoverAsync(scan.Token);
            if (scan.IsCancellationRequested) return;
            var previous = _devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
            _devices.Clear();
            foreach (var device in found)
            {
                previous.Remove(device.Id);
                _devices.Add(new DeviceRow(device.Id, device.Name, Describe(device, true)));
            }
            foreach (var device in previous.Values)
                _devices.Add(device with { Details = device.Details.Replace("Available", "Unavailable") });
            StatusText.Text = found.Count == 0 ? "No devices responded. Check that your devices and PC are on the same network." : $"{found.Count} device(s) available. Refreshes every 30 seconds.";
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or System.IO.IOException)
        {
            for (var i = 0; i < _devices.Count; i++)
                _devices[i] = _devices[i] with { Details = _devices[i].Details.Replace("Available", "Unavailable") };
            StatusText.Text = "Discovery could not access the network. Check your connection and try again.";
        }
        finally
        {
            _scan = null;
            RefreshButton.IsEnabled = true;
            if (IsLoaded && scan.IsCancellationRequested) _ = RefreshAsync();
        }
    }

    private static string Describe(NetworkMediaDevice device, bool available) =>
        $"{(available ? "Available" : "Unavailable")} · {device.DescriptionLocation.Host}" +
        (string.IsNullOrWhiteSpace(device.Manufacturer) ? "" : $" · {device.Manufacturer}");

    private sealed record DeviceRow(string Id, string Name, string Details);
}


