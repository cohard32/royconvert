using System.Globalization;
using System.Text.Json;

namespace RoyConvert.App;

/// <summary>User settings, stored as JSON in the roaming application data folder (royconvert\settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>The UI culture before <see cref="ApplyLanguage"/> changed it.</summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    /// <summary>Languages with a translation; "en" is the neutral resources.</summary>
    public static IReadOnlyList<string> Languages { get; } = ["en", "tr"];

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "royconvert", "settings.json");

    /// <summary>One of <see cref="Languages"/>, or null to follow the system language.</summary>
    public string? Language { get; set; }

    /// <summary>Missing or unreadable settings give the defaults.</summary>
    public static AppSettings Load(string? path = null)
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? DefaultPath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Sets the UI culture of this process (and its new threads) to <see cref="Language"/>.</summary>
    public void ApplyLanguage()
    {
        var culture = Language is { } code && Languages.Contains(code) ? CultureInfo.GetCultureInfo(code) : SystemCulture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
