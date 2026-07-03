namespace ProWeb.Client.Core;

/// <summary>
/// Tracks page-load progress and the reload/stop button mode (UT-F-R1-003 / UT-F-R1-004). The
/// primary action toggles between "reload" (idle) and "stop" (loading); a determinate progress
/// value drives the progress bar.
/// </summary>
public sealed class LoadProgressModel
{
    public bool IsLoading { get; private set; }

    /// <summary>Load progress in the range [0, 1].</summary>
    public double Progress { get; private set; }

    /// <summary>The primary toolbar action available to the user given the current state.</summary>
    public PrimaryLoadAction PrimaryAction => IsLoading ? PrimaryLoadAction.Stop : PrimaryLoadAction.Reload;

    public string ActionGlyph => IsLoading ? "✕" : "⟳";

    public string ActionLabel => IsLoading ? "停止加载" : "重新加载";

    /// <summary>Progress bar is only shown while loading.</summary>
    public bool ProgressVisible => IsLoading;

    public void Start()
    {
        IsLoading = true;
        Progress = 0.0;
    }

    public void Report(double progress)
    {
        if (!IsLoading)
            return;
        Progress = Math.Clamp(progress, 0.0, 1.0);
    }

    public void Complete()
    {
        IsLoading = false;
        Progress = 1.0;
    }
}

/// <summary>The primary reload/stop toolbar action.</summary>
public enum PrimaryLoadAction
{
    Reload,
    Stop,
}
