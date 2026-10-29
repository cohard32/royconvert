using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoyConvert.Core.Updates;

/// <param name="Sha256">Lower-case hex of the setup file, as published with the release.</param>
public sealed record UpdateInfo(Version Version, Uri DownloadUrl, string FileName, string Sha256, Uri? PageUrl);

/// <summary>
/// Looks for a newer release on GitHub and downloads its setup. Only full releases count ("latest" skips
/// pre-releases), and a download is used only when its SHA-256 matches the one published with the release.
/// </summary>
public static partial class UpdateChecker
{
    /// <summary>The newest full release; ROYCONVERT_UPDATE_URL replaces it, for testing.</summary>
    public static Uri LatestReleaseUrl { get; } = new(
        Environment.GetEnvironmentVariable("ROYCONVERT_UPDATE_URL") is { Length: > 0 } custom
            ? custom
            : "https://api.github.com/repos/cohard32/royconvert/releases/latest");

    /// <param name="current">The running version, sent as the User-Agent (GitHub's API refuses requests without one).</param>
    public static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("royconvert", current.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>The release when it is newer than <paramref name="current"/> and has a Windows setup; otherwise null.</summary>
    /// <exception cref="HttpRequestException">GitHub could not be reached.</exception>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, Uri latestUrl, Version current, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(latestUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No full release yet.
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Parse(json.RootElement, current);
    }

    internal static UpdateInfo? Parse(JsonElement release, Version current)
    {
        if (!release.TryGetProperty("tag_name", out var tag) || ParseVersion(tag.GetString()) is not { } version || version <= current
            || (release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
            || (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()))
        {
            return null;
        }

        var body = release.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty;
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // GitHub records each upload's digest; older releases have it only in the notes ("SHA-256: `…`").
            var sha = asset.TryGetProperty("digest", out var digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.Ordinal)
                ? d["sha256:".Length..]
                : ShaInNotes().Match(body) is { Success: true } m ? m.Groups[1].Value : null;
            if (sha is null)
            {
                return null;
            }

            var page = release.TryGetProperty("html_url", out var html) && Uri.TryCreate(html.GetString(), UriKind.Absolute, out var p) ? p : null;
            return new UpdateInfo(version, new Uri(asset.GetProperty("browser_download_url").GetString()!), name, sha.ToLowerInvariant(), page);
        }

        return null;
    }

    /// <summary>"v1.2.3" or "1.2.3"; null for anything else (such as "v1.0.0-test1").</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = tag?.TrimStart('v', 'V');
        return text is not null && TagRegex().IsMatch(text) && Version.TryParse(text, out var v)
            ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0))
            : null;
    }

    /// <summary>Downloads the setup into <paramref name="folder"/> and checks it.</summary>
    /// <returns>The setup's path.</returns>
    /// <exception cref="InvalidDataException">The download does not match the published SHA-256; nothing is kept.</exception>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, string folder, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(update.FileName));
        var partial = path + ".part";
        try
        {
            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    sha.AppendData(buffer, 0, read);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report((double)done / total.Value);
                    }
                }
            }

            if (Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant() != update.Sha256)
            {
                throw new InvalidDataException("The downloaded setup does not match the published SHA-256.");
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    [GeneratedRegex(@"^\d+\.\d+(\.\d+)?$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"SHA-256:\s*`?([0-9a-fA-F]{64})`?")]
    private static partial Regex ShaInNotes();
}
