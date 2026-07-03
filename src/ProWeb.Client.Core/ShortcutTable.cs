namespace ProWeb.Client.Core;

/// <summary>Logical browser commands bound to keyboard shortcuts and the menu (UT-F-R1-005).</summary>
public enum BrowserCommand
{
    NewTab,
    CloseTab,
    ReopenClosedTab,
    FocusAddressBar,
    Reload,
    StopLoading,
    Back,
    Forward,
    OpenSettings,
    FindInPage,
    GoHome,
}

/// <summary>The canonical key gesture for a command (as WPF InputGesture text).</summary>
public sealed record ShortcutBinding(BrowserCommand Command, string Gesture);

/// <summary>
/// Single source of truth for the keyboard shortcuts wired into the window's InputBindings and the
/// menu accelerators, so the two never drift apart.
/// </summary>
public static class ShortcutTable
{
    public static readonly IReadOnlyList<ShortcutBinding> Bindings = new[]
    {
        new ShortcutBinding(BrowserCommand.NewTab, "Ctrl+T"),
        new ShortcutBinding(BrowserCommand.CloseTab, "Ctrl+W"),
        new ShortcutBinding(BrowserCommand.ReopenClosedTab, "Ctrl+Shift+T"),
        new ShortcutBinding(BrowserCommand.FocusAddressBar, "Ctrl+L"),
        new ShortcutBinding(BrowserCommand.Reload, "F5"),
        new ShortcutBinding(BrowserCommand.StopLoading, "Esc"),
        new ShortcutBinding(BrowserCommand.Back, "Alt+Left"),
        new ShortcutBinding(BrowserCommand.Forward, "Alt+Right"),
        new ShortcutBinding(BrowserCommand.OpenSettings, "Ctrl+OemComma"),
        new ShortcutBinding(BrowserCommand.FindInPage, "Ctrl+F"),
        new ShortcutBinding(BrowserCommand.GoHome, "Alt+Home"),
    };

    public static string GestureFor(BrowserCommand command) =>
        Bindings.First(b => b.Command == command).Gesture;
}
