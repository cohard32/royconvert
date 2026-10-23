using System.IO.Pipes;
using System.Text.Json;

namespace RoyConvert.App;

/// <summary>
/// Explorer starts one process per selected file. The first process becomes the primary instance and
/// shows a single progress window; later processes forward their arguments over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Must be identical in every process, so no string.GetHashCode (randomized per process).
    private static readonly string Name = "royconvert-" + string.Concat(Environment.UserName.Where(char.IsLetterOrDigit));

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _ = Task.Run(ServeAsync);
    }

    // Explorer starts all processes at once, so arguments usually arrive before the UI is ready to listen.
    private readonly List<string[]> _pending = [];
    private Action<string[]>? _handler;

    /// <summary>
    /// Sets the handler for arguments sent by other processes, first replaying any that arrived earlier.
    /// The handler runs on a background thread.
    /// </summary>
    public void Listen(Action<string[]> handler)
    {
        lock (_pending)
        {
            _handler = handler;
            _pending.ForEach(handler);
            _pending.Clear();
        }
    }

    /// <summary>
    /// Returns the primary instance, or null after the arguments were forwarded to an existing one.
    /// </summary>
    public static SingleInstance? Acquire(IReadOnlyList<string> args)
    {
        // The primary may be shutting down between our mutex check and the connection, so retry both.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var mutex = new Mutex(initiallyOwned: true, Name, out var createdNew);
            if (createdNew)
            {
                return new SingleInstance(mutex);
            }

            mutex.Dispose();
            if (TrySend(args))
            {
                return null;
            }

            Thread.Sleep(100);
        }

        // Could not reach the other instance: work on our own rather than dropping the files.
        return new SingleInstance(new Mutex(false));
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (fallback instance).
        }

        _mutex.Dispose();
    }

    private static bool TrySend(IReadOnlyList<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            client.Connect(1000);
            JsonSerializer.Serialize(client, args);
            client.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(Name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                var args = await JsonSerializer.DeserializeAsync<string[]>(server, cancellationToken: _stop.Token);
                if (args is not null)
                {
                    lock (_pending)
                    {
                        if (_handler is null)
                        {
                            _pending.Add(args);
                        }
                        else
                        {
                            _handler(args);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                // A client that disconnects early or sends garbage must not stop the server.
            }
        }
    }
}
