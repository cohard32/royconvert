using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

/// <summary>A window that can show the bar about a newer release.</summary>
public interface IUpdateHost
{
    /// <summary>False while installing must wait, e.g. files are still being converted.</summary>
    bool CanInstallUpdate { get; }

    void ShowUpdate(UpdateViewModel update);
}
