using System.IO;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace ProWeb.Client;

/// <summary>WPF application entry point. Initializes the CefSharp Chromium runtime.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new CefSettings
        {
            CachePath = Path.Combine(Path.GetTempPath(), "proweb-cef-cache"),
            LogSeverity = LogSeverity.Warning,
        };
        settings.CefCommandLineArgs.Add("disable-gpu", "1");

        // Initialize synchronously; performDependencyCheck=false tolerates dev layouts.
        Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        base.OnExit(e);
    }
}
