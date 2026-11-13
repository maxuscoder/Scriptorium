using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Scriptorium.Infrastructure.Services;

/// <summary>Serves a single explicitly chosen file to one receiver, without converting it.</summary>
public sealed class LocalMediaStream : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IPAddress _receiver;
    private readonly string _path;
    private readonly string _mime;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _acceptLoop;
    private readonly object _gate = new();
    private readonly HashSet<Task> _requests = [];

    public LocalMediaStream(IPAddress localAddress, IPAddress receiver, string path, string mime)
    {
        if (localAddress.AddressFamily != AddressFamily.InterNetwork || receiver.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Only IPv4 streaming is supported.");
        if (string.IsNullOrWhiteSpace(mime) || mime.Contains('\r') || mime.Contains('\n'))
            throw new ArgumentException("Invalid content type.", nameof(mime));
        _path = Path.GetFullPath(path);
        using (File.OpenRead(_path)) { }
        _mime = mime;
        _receiver = receiver;
        _listener = new TcpListener(localAddress, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        Uri = new Uri($"http://{localAddress}:{endpoint.Port}/{_token}");
        _acceptLoop = AcceptAsync();
    }

    public Uri Uri { get; }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                if (((IPEndPoint)client.Client.RemoteEndPoint!).Address.Equals(_receiver))
                {
                    var task = HandleAsync(client);
                    lock (_gate) _requests.Add(task);
                    _ = task.ContinueWith(t => { lock (_gate) _requests.Remove(t); }, TaskScheduler.Default);
                }
                else client.Dispose();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = client.GetStream();
                var bytes = new List<byte>();
                var one = new byte[1];
                while (bytes.Count < 16384)
                {
                    if (await stream.ReadAsync(one, deadline.Token) == 0) return;
                    bytes.Add(one[0]);
                    var n = bytes.Count;
                    if (n >= 4 && bytes[n-4] == 13 && bytes[n-3] == 10 && bytes[n-2] == 13 && bytes[n-1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3 || (request[0] != "GET" && request[0] != "HEAD") || request[1] != "/" + _token)
                {
                    await WriteHeaders(stream, "404 Not Found", "Content-Length: 0\r\n", deadline.Token);
                    return;
                }
                await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var length = file.Length;
                var range = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
                long start = 0, end = length - 1;
                if (range is not null && !TryRange(range[6..].Trim(), length, out start, out end))
                {
                    await WriteHeaders(stream, "416 Range Not Satisfiable", $"Content-Range: bytes */{length}\r\nContent-Length: 0\r\n", deadline.Token);
                    return;
                }
                var count = end - start + 1;
                var extra = $"Content-Type: {_mime}\r\nAccept-Ranges: bytes\r\nContent-Length: {count}\r\n";
                if (range is not null) extra += $"Content-Range: bytes {start}-{end}/{length}\r\n";
                await WriteHeaders(stream, range is null ? "200 OK" : "206 Partial Content", extra, deadline.Token);
                if (request[0] == "HEAD") return;
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                file.Position = start;
                var buffer = new byte[65536];
                while (count > 0)
                {
                    var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(count, buffer.Length)), deadline.Token);
                    if (read == 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                    count -= read;
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or UnauthorizedAccessException or ObjectDisposedException) { }
        }
    }

    private static Task WriteHeaders(NetworkStream stream, string status, string headers, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n{headers}Connection: close\r\n\r\n"), token).AsTask();

    public static bool TryRange(string value, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (length <= 0 || !value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = value[6..].Split('-');
        if (parts.Length != 2) return false;
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix);
            return true;
        }
        if (!long.TryParse(parts[0], out start) || start < 0 || start >= length) return false;
        if (parts[1].Length != 0 && (!long.TryParse(parts[1], out end) || end < start)) return false;
        end = Math.Min(end, length - 1);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _acceptLoop;
        Task[] requests;
        lock (_gate) requests = _requests.ToArray();
        await Task.WhenAll(requests);
        _stop.Dispose();
    }
}
