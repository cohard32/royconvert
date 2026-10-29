using System.Globalization;
using System.Text.Json;
using RoyConvert.Core;

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

    // Conversion choices from the settings window; null keeps the built-in default.

    /// <summary>Lossy image quality (JPG, WebP, AVIF), 1-100.</summary>
    public int? ImageQuality { get; set; }

    /// <summary>Video quality as CRF, see <see cref="ConversionSettings.VideoQuality"/>.</summary>
    public int? VideoQuality { get; set; }

    /// <summary>Resolution PDF pages are rendered at, see <see cref="ConversionSettings.PdfDpi"/>.</summary>
    public int? PdfDpi { get; set; }

    /// <summary>Conversions running at the same time; null for <see cref="ConversionRunner.DefaultParallelism"/>.</summary>
    public int? MaxParallelism { get; set; }

    /// <summary>Looks for a newer release about once a day when a window opens.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When the last look for a newer release was made.</summary>
    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>Missing or unreadable settings give the defaults; out-of-range values are brought into range.</summary>
    public static AppSettings Load(string? path = null)
    {
        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path ?? DefaultPath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }

        settings.ImageQuality = Clamp(settings.ImageQuality, 1, 100);
        settings.VideoQuality = Clamp(settings.VideoQuality, 0, 51);
        settings.PdfDpi = Clamp(settings.PdfDpi, 36, 600);
        settings.MaxParallelism = Clamp(settings.MaxParallelism, 1, 64);
        return settings;
    }

    /// <summary>
    /// The preset's settings with the user's choices, except where the preset sets its own value
    /// (e.g. "MP4 (small)" keeps its lower video quality).
    /// </summary>
    public ConversionSettings ApplyTo(ConversionSettings settings)
    {
        var defaults = new ConversionSettings();
        return settings with
        {
            ImageQuality = Choose(ImageQuality, settings.ImageQuality, defaults.ImageQuality),
            VideoQuality = Choose(VideoQuality, settings.VideoQuality, defaults.VideoQuality),
            PdfDpi = Choose(PdfDpi, settings.PdfDpi, defaults.PdfDpi),
        };

        static int Choose(int? user, int preset, int builtIn) => user is { } value && preset == builtIn ? value : preset;
    }

    private static int? Clamp(int? value, int min, int max) => value is { } v ? Math.Clamp(v, min, max) : null;

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
