namespace RoyConvert.App.Tests;

/// <remarks>Uses the real per-user pipe name, so it fails if royconvert is running at the same time.</remarks>
public class SingleInstanceTests
{
    [Fact]
    public async Task SecondInstance_ForwardsArguments_EvenBeforeListening()
    {
        using var primary = SingleInstance.Acquire(["first"]);
        Assert.NotNull(primary);

        // Sent before Listen: must be replayed, not dropped.
        Assert.Null(SingleInstance.Acquire(["--preset", "mp4", "early.mov"]));

        var received = new List<string[]>();
        primary.Listen(args => { lock (received) { received.Add(args); } });
        Assert.Null(SingleInstance.Acquire(["--preset", "mp4", "late.mov"]));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            lock (received)
            {
                if (received.Count == 2)
                {
                    break;
                }
            }

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for forwarded arguments.");
            await Task.Delay(20);
        }

        Assert.Equal(["--preset", "mp4", "early.mov"], received[0]);
        Assert.Equal(["--preset", "mp4", "late.mov"], received[1]);
    }
}
