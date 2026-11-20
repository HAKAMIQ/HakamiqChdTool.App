using Serilog;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Startup;

/// <summary>
/// Hands a <see cref="LaunchRequest"/> from a second launch to the running instance over a named pipe.
/// Both ends use <see cref="PipeOptions.CurrentUserOnly"/>, so only the same Windows account can connect,
/// and the pipe name carries the user SID and session, like the session-local single-instance mutex.
/// Messages are bounded and validated; paths are still untrusted and go through normal queue intake.
/// </summary>
internal sealed class InstanceChannel : IDisposable
{
    public const int MaxMessageBytes = 1 << 20;
    private const int FormatVersion = 1;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly Action<LaunchRequest> _onRequest;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private InstanceChannel(string pipeName, Action<LaunchRequest> onRequest)
    {
        _pipeName = pipeName;
        _onRequest = onRequest;
        _loop = Task.Run(() => ListenAsync(_cts.Token));
    }

    public static string DefaultPipeName
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? "unknown";
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return $"HakamiqChdTool.App.Launch.{sid}.{process.SessionId}";
        }
    }

    public static InstanceChannel Start(string pipeName, Action<LaunchRequest> onRequest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onRequest);
        return new InstanceChannel(pipeName, onRequest);
    }

    /// <summary>
    /// Sends the request to the running instance. Waits up to <paramref name="timeout"/> for its pipe, which
    /// covers an instance that is still starting. Large requests are split into several messages.
    /// </summary>
    public static bool TrySend(string pipeName, LaunchRequest request, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(request);

        DateTime deadline = DateTime.UtcNow + timeout;
        try
        {
            foreach (byte[] message in Encode(request))
            {
                int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(remaining);
                AllowServerToTakeForeground(client);
                client.Write(message, 0, message.Length);
                client.Flush();
                client.WaitForPipeDrain();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warning(ex, "Could not hand the launch request to the running CHD Tool instance.");
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    internal static IEnumerable<byte[]> Encode(LaunchRequest request)
    {
        var batch = new List<string>();
        int batchBytes = 0;
        const int envelopeBytes = 128;

        foreach (string path in request.Paths)
        {
            int pathBytes = Encoding.UTF8.GetByteCount(path) * 2 + 8;
            if (batch.Count > 0 && (batchBytes + pathBytes + envelopeBytes > MaxMessageBytes || batch.Count >= LaunchArgs.MaxPaths))
            {
                yield return Serialize(request.Mode, batch);
                batch = [];
                batchBytes = 0;
            }

            batch.Add(path);
            batchBytes += pathBytes;
        }

        if (batch.Count > 0 || request.Paths.Count == 0)
        {
            yield return Serialize(request.Mode, batch);
        }
    }

    internal static bool TryDecode(ReadOnlySpan<byte> payload, out LaunchRequest request)
    {
        request = LaunchRequest.Empty;
        if (payload.Length == 0 || payload.Length > MaxMessageBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int v)
                || v != FormatVersion
                || !root.TryGetProperty("mode", out JsonElement modeElement)
                || modeElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("paths", out JsonElement pathsElement)
                || pathsElement.ValueKind != JsonValueKind.Array
                || pathsElement.GetArrayLength() > LaunchArgs.MaxPaths)
            {
                return false;
            }

            string modeName = modeElement.GetString() ?? string.Empty;
            LaunchMode mode = LaunchMode.None;
            if (modeName.Length > 0 && !LaunchArgs.TryParseMode(modeName, out mode))
            {
                return false;
            }

            var paths = new List<string>();
            foreach (JsonElement item in pathsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !LaunchArgs.TryNormalizePath(item.GetString(), out string fullPath)
                    || !string.Equals(fullPath, item.GetString(), StringComparison.Ordinal))
                {
                    return false;
                }

                paths.Add(fullPath);
            }

            request = new LaunchRequest(mode, paths);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static byte[] Serialize(LaunchMode mode, IReadOnlyList<string> paths)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["v"] = FormatVersion,
            ["mode"] = LaunchArgs.ModeName(mode),
            ["paths"] = paths
        });
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Launch channel could not be opened; files sent from Explorer to this instance will not arrive.");
                return;
            }

            await using (server.ConfigureAwait(false))
            {
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(ReadTimeout);
                    byte[]? payload = await ReadBoundedAsync(server, readCts.Token).ConfigureAwait(false);
                    if (payload is not null && TryDecode(payload, out LaunchRequest request))
                    {
                        _onRequest(request);
                    }
                    else
                    {
                        Log.Warning("Launch channel ignored a malformed or oversized message.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    Log.Debug(ex, "Launch channel connection ended early.");
                }
            }
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxMessageBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static void AllowServerToTakeForeground(NamedPipeClientStream client)
    {
        try
        {
            if (GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverProcessId) && serverProcessId != 0)
            {
                _ = AllowSetForegroundWindow(serverProcessId);
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug(ex, "Could not let the running instance take the foreground.");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
