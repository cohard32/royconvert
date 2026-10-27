using Avalonia;
using RoyConvert.App.Platform;
using RoyConvert.Core;

namespace RoyConvert.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Before anything produces text: the menu labels written by --register-shell are translated too.
        var settings = AppSettings.Load();
        settings.ApplyLanguage();
        var command = CommandLine.Parse(args);
        SingleInstance? instance = null;

        switch (command.Kind)
        {
            case CommandKind.ListPresets:
                foreach (var preset in AvailablePresets.For(AvailablePresets.HasWord()))
                {
                    Console.WriteLine($"{preset.Id,-24} {preset.Name}");
                }

                return 0;

            case CommandKind.RegisterShell when OperatingSystem.IsWindows():
                var registration = WindowsShellMenu.Register(Environment.ProcessPath!, AvailablePresets.For(AvailablePresets.HasWord()));
                if (registration.ModernMenuError is { } registerError)
                {
                    Console.Error.WriteLine(registerError);
                    return 1;
                }

                return 0;

            case CommandKind.UnregisterShell when OperatingSystem.IsWindows():
                if (WindowsShellMenu.Unregister() is { } unregisterError)
                {
                    Console.Error.WriteLine(unregisterError);
                    return 1;
                }

                return 0;

            case CommandKind.RegisterShell or CommandKind.UnregisterShell:
                Console.Error.WriteLine("The right-click menu can only be managed on Windows.");
                return 1;

            case CommandKind.Convert:
                instance = SingleInstance.Acquire(command.ToArguments());
                if (instance is null)
                {
                    // Handed over to the window that is already open.
                    return 0;
                }

                break;
        }

        // Invalid commands still open the progress window, which shows the error.
        return BuildAvaloniaApp(command, instance, settings).StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(CommandLine.Parse([]), null, new AppSettings());

    private static AppBuilder BuildAvaloniaApp(CommandLine command, SingleInstance? instance, AppSettings settings)
        => AppBuilder.Configure(() => new App { Command = command, Instance = instance, Settings = settings })
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
