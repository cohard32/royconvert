using System.IO.Pipes;

namespace RoyConvert.App.Tests;

/// <remarks>Each test uses its own pipe name, so a running royconvert does not interfere.</remarks>
public class SingleInstanceTests
{
    private readonly string _name = "royconvert-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task SecondInstance_ForwardsArguments_EvenBeforeListening()
    {
        using var primary = SingleInstance.Acquire(["first"], _name);
        Assert.NotNull(primary);

        // Sent before Listen: must be replayed, not dropped.
        Assert.Null(SingleInstance.Acquire(["--preset", "mp4", "early.mov"], _name));

        var received = new List<string[]>();
        primary.Listen(args => { lock (received) { received.Add(args); } });
        Assert.Null(SingleInstance.Acquire(["--preset", "mp4", "late.mov"], _name));

        await WaitFor(() => { lock (received) { return received.Count == 2; } });
        Assert.Equal(["--preset", "mp4", "early.mov"], received[0]);
        Assert.Equal(["--preset", "mp4", "late.mov"], received[1]);
    }

    [Fact]
    public void Arguments_AreHandedOver_BeforeTheSenderReturns()
    {
        using var primary = SingleInstance.Acquire(["first"], _name)!;
        var received = new List<string[]>();
        primary.Listen(args => { lock (received) { received.Add(args); } });

        Assert.Null(SingleInstance.Acquire(["a.mov"], _name));

        lock (received)
        {
            Assert.Single(received);
        }
    }

    [Fact]
    public async Task ClosingPrimary_RefusesFiles_AndTheSenderTakesOver()
    {
        var primary = SingleInstance.Acquire(["first"], _name)!;
        var taken = 0;
        primary.Listen(_ => Interlocked.Increment(ref taken));
        Assert.True(primary.TryStopAccepting(() => true));

        // Explorer sends more files while the window closes.
        var sender = Task.Run(() => SingleInstance.Acquire(["--preset", "mp4", "late.mov"], _name));
        await Task.Delay(500);
        Assert.False(sender.IsCompleted);

        primary.Dispose();
        using var next = await sender.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(next); // it became the primary and converts the files itself
        Assert.Equal(0, taken);
    }

    [Fact]
    public void StopAccepting_IsRefused_WhileTheWindowIsBusy()
    {
        using var primary = SingleInstance.Acquire(["first"], _name)!;
        var received = 0;
        primary.Listen(_ => Interlocked.Increment(ref received));

        Assert.False(primary.TryStopAccepting(() => false));
        Assert.Null(SingleInstance.Acquire(["a.mov"], _name));
        Assert.Equal(1, received);
    }

    [Fact]
    public void StopAccepting_IsRefused_WhileFilesWaitForTheWindow()
    {
        using var primary = SingleInstance.Acquire(["first"], _name)!;
        Assert.Null(SingleInstance.Acquire(["early.mov"], _name)); // before Listen

        Assert.False(primary.TryStopAccepting(() => true));
    }

    [Fact]
    public async Task Garbage_DoesNotStopTheServer()
    {
        using var primary = SingleInstance.Acquire(["first"], _name)!;
        var received = 0;
        primary.Listen(_ => Interlocked.Increment(ref received));

        await using (var client = new NamedPipeClientStream(".", _name, PipeDirection.InOut))
        {
            await client.ConnectAsync(1000);
            try
            {
                await client.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 });
            }
            catch (IOException)
            {
                // The server hangs up as soon as it reads the impossible length.
            }
        }

        Assert.Null(SingleInstance.Acquire(["a.mov"], _name));
        Assert.Equal(1, received);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(20);
        }
    }
}
