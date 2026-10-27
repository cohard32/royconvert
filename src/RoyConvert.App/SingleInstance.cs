using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace RoyConvert.App;

/// <summary>
/// Explorer starts one process per selected file. The first process becomes the primary instance and
/// shows a single progress window; later processes forward their arguments over a named pipe and exit.
/// </summary>
/// <remarks>
/// The primary answers every message with <see cref="Accepted"/> or <see cref="Refused"/>; a sender exits only after
/// "accepted". Once the primary decides to close (<see cref="TryStopAccepting"/>) it refuses, and the sender retries
/// until it becomes the next primary itself, so files sent while the window closes are not lost.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const byte Accepted = 1;
    private const byte Refused = 0;

    /// <summary>Far above any real argument list (long selections go through a list file).</summary>
    private const int MaxMessageBytes = 16 * 1024 * 1024;

    /// <summary>How long a sender keeps trying before it converts the files on its own.</summary>
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(10);

    // Must be identical in every process, so no string.GetHashCode (randomized per process).
    private static readonly string DefaultName = "royconvert-" + string.Concat(Environment.UserName.Where(char.IsLetterOrDigit));

    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    // Explorer starts all processes at once, so arguments usually arrive before the UI is ready to listen.
    private readonly List<string[]> _pending = [];
    private Action<string[]>? _handler;
    private bool _accepting = true;

    private SingleInstance(string name, Mutex mutex, bool serve)
    {
        _name = name;
        _mutex = mutex;
        if (serve)
        {
            _ = Task.Run(ServeAsync);
        }
    }

    /// <summary>
    /// Sets the handler for arguments sent by other processes, first replaying any that arrived earlier.
    /// The handler runs on a background thread, before the sender is told the arguments were accepted.
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
    /// Stops accepting arguments if <paramref name="canClose"/> still holds; checked under the same lock that accepts
    /// arguments, so none can slip in between. Returns false (and keeps accepting) otherwise.
    /// </summary>
    public bool TryStopAccepting(Func<bool> canClose)
    {
        lock (_pending)
        {
            if (_pending.Count > 0 || !canClose())
            {
                return false;
            }

            _accepting = false;
            return true;
        }
    }

    /// <summary>
    /// Returns the primary instance, or null after the arguments were accepted by an existing one.
    /// </summary>
    /// <param name="name">The pipe and mutex name; only tests pass one, to stay apart from a running royconvert.</param>
    public static SingleInstance? Acquire(IReadOnlyList<string> args, string? name = null)
    {
        name ??= DefaultName;
        var deadline = DateTime.UtcNow + AcquireTimeout;

        // The primary may be closing: it refuses, then releases the mutex, and a later attempt takes over.
        do
        {
            var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (createdNew)
            {
                return new SingleInstance(name, mutex, serve: true);
            }

            mutex.Dispose();
            if (TrySend(name, args))
            {
                return null;
            }

            Thread.Sleep(100);
        }
        while (DateTime.UtcNow < deadline);

        // Could not reach the other instance: work on our own rather than dropping the files.
        return new SingleInstance(name, new Mutex(false), serve: false);
    }

    public void Dispose()
    {
        lock (_pending)
        {
            _accepting = false;
        }

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

    private static bool TrySend(string name, IReadOnlyList<string> args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            client.Connect(1000);
            var message = JsonSerializer.SerializeToUtf8Bytes(args);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, message.Length);
            client.Write(length);
            client.Write(message);
            client.Flush();

            // The primary answers right after handing the arguments over; no answer means they were not taken.
            var answer = new byte[1];
            var read = client.ReadAsync(answer, 0, 1);
            return read.Wait(TimeSpan.FromSeconds(5)) && read.Result == 1 && answer[0] == Accepted;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or AggregateException)
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
                await using var server = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);

                var length = new byte[4];
                await server.ReadExactlyAsync(length, _stop.Token);
                var size = BinaryPrimitives.ReadInt32LittleEndian(length);
                if (size is <= 0 or > MaxMessageBytes)
                {
                    continue;
                }

                var message = new byte[size];
                await server.ReadExactlyAsync(message, _stop.Token);
                var answer = Accept(JsonSerializer.Deserialize<string[]>(message)) ? Accepted : Refused;
                await server.WriteAsync(new[] { answer }, _stop.Token);
                await server.FlushAsync(_stop.Token);
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

    private bool Accept(string[]? args)
    {
        lock (_pending)
        {
            if (args is null || !_accepting)
            {
                return false;
            }

            if (_handler is null)
            {
                _pending.Add(args);
            }
            else
            {
                _handler(args);
            }

            return true;
        }
    }
}
