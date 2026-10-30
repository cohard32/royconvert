using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RoyConvert.Core.Updates;

namespace RoyConvert.Core.Tests;

public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly Uri Latest = new("https://example.test/releases/latest");
    private static readonly byte[] Setup = Encoding.ASCII.GetBytes("pretend this is royconvert-1.2.0-setup.exe");
    private static readonly string SetupSha = Convert.ToHexString(SHA256.HashData(Setup)).ToLowerInvariant();

    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-update-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Release(string tag = "v1.2.0", bool prerelease = false, string? digest = "default", string body = "")
    {
        var asset = new Dictionary<string, object?>
        {
            ["name"] = $"royconvert-{tag.TrimStart('v')}-setup.exe",
            ["browser_download_url"] = "https://example.test/download/setup.exe",
        };
        if (digest is not null)
        {
            asset["digest"] = digest == "default" ? "sha256:" + SetupSha : digest;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["prerelease"] = prerelease,
            ["draft"] = false,
            ["html_url"] = "https://example.test/releases/" + tag,
            ["body"] = body,
            ["assets"] = new[] { asset },
        });
    }

    private static HttpClient Server(string? releaseJson, byte[]? download = null, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FakeServer(request => request.RequestUri == Latest
            ? releaseJson is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(status) { Content = new StringContent(releaseJson) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(download ?? Setup) }));

    [Fact]
    public async Task NewerRelease_IsAnUpdate_WithItsSetupAndDigest()
    {
        var update = await UpdateChecker.CheckAsync(Server(Release()), Latest, new Version(1, 0, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 2, 0), update.Version);
        Assert.Equal("royconvert-1.2.0-setup.exe", update.FileName);
        Assert.Equal(SetupSha, update.Sha256);
        Assert.Equal("https://example.test/releases/v1.2.0", update.PageUrl!.ToString());
    }

    [Theory]
    [InlineData("v1.0.0")] // the same
    [InlineData("v0.9.5")] // older
    [InlineData("v1.0.1-test1")] // not a plain version
    public async Task SameOlderOrOddVersion_IsNoUpdate(string tag)
    {
        Assert.Null(await UpdateChecker.CheckAsync(Server(Release(tag)), Latest, new Version(1, 0, 0)));
    }

    [Fact]
    public async Task PreRelease_OrNoRelease_IsNoUpdate()
    {
        Assert.Null(await UpdateChecker.CheckAsync(Server(Release(prerelease: true)), Latest, new Version(1, 0, 0)));
        Assert.Null(await UpdateChecker.CheckAsync(Server(null), Latest, new Version(1, 0, 0)));
    }

    [Fact]
    public async Task WithoutDigest_TheShaInTheNotesIsUsed_AndWithoutEither_ItIsNoUpdate()
    {
        var notes = Release(digest: null, body: $"Kurulum\n\nSHA-256: `{SetupSha.ToUpperInvariant()}`\n");

        Assert.Equal(SetupSha, (await UpdateChecker.CheckAsync(Server(notes), Latest, new Version(1, 0, 0)))!.Sha256);
        Assert.Null(await UpdateChecker.CheckAsync(Server(Release(digest: null)), Latest, new Version(1, 0, 0)));
    }

    [Fact]
    public async Task ServerError_Throws()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            UpdateChecker.CheckAsync(Server(Release(), status: HttpStatusCode.Forbidden), Latest, new Version(1, 0, 0)));
    }

    [Fact]
    public async Task Download_KeepsTheSetup_WhenTheShaMatches()
    {
        var http = Server(Release());
        var update = (await UpdateChecker.CheckAsync(http, Latest, new Version(1, 0, 0)))!;
        var progress = new List<double>();

        var path = await UpdateChecker.DownloadAsync(http, update, _dir, new SyncProgress(progress));

        Assert.Equal(Path.Combine(_dir, "royconvert-1.2.0-setup.exe"), path);
        Assert.Equal(Setup, File.ReadAllBytes(path));
        Assert.Equal(1, progress[^1]);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_ThatDoesNotMatch_IsRejected_AndRemoved()
    {
        var http = Server(Release(), download: Encoding.ASCII.GetBytes("something else"));
        var update = (await UpdateChecker.CheckAsync(http, Latest, new Version(1, 0, 0)))!;

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.DownloadAsync(http, update, _dir));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("2.10", "2.10.0")]
    [InlineData("v1.0.0-test6", null)]
    [InlineData("latest", null)]
    [InlineData(null, null)]
    public void ParseVersion_AcceptsPlainVersions(string? tag, string? expected)
    {
        Assert.Equal(expected is null ? null : Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class SyncProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
