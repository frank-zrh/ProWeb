namespace ProWeb.Client.Core;

/// <summary>A single row in the keyboard-shortcut cheat sheet: a friendly label and its gesture.</summary>
public sealed record ShortcutCheatSheetEntry(BrowserCommand Command, string Label, string Gesture);

/// <summary>
/// Builds the "键盘快捷键速查" (keyboard shortcut cheat sheet) shown from the Help menu
/// (UT-X-R3-001). It is generated directly from <see cref="ShortcutTable.Bindings"/> so the sheet
/// can never drift from the gestures actually wired into the window.
/// </summary>
public static class ShortcutCheatSheet
{
    /// <summary>Friendly Chinese label for each command.</summary>
    public static string LabelFor(BrowserCommand command) => command switch
    {
        BrowserCommand.NewTab => "新建标签页",
        BrowserCommand.CloseTab => "关闭当前标签页",
        BrowserCommand.ReopenClosedTab => "重新打开关闭的标签页",
        BrowserCommand.FocusAddressBar => "聚焦地址栏",
        BrowserCommand.Reload => "重新加载",
        BrowserCommand.StopLoading => "停止加载",
        BrowserCommand.Back => "后退",
        BrowserCommand.Forward => "前进",
        BrowserCommand.OpenSettings => "打开设置 / 连接",
        BrowserCommand.FindInPage => "在页面中查找",
        BrowserCommand.GoHome => "回到主页",
        _ => command.ToString(),
    };

    /// <summary>Cheat-sheet rows, one per bound shortcut, in the table's order.</summary>
    public static IReadOnlyList<ShortcutCheatSheetEntry> Entries =>
        ShortcutTable.Bindings
            .Select(b => new ShortcutCheatSheetEntry(b.Command, LabelFor(b.Command), b.Gesture))
            .ToList();

    /// <summary>Plain-text rendering suitable for an About/Help message box.</summary>
    public static string RenderText() =>
        string.Join(Environment.NewLine, Entries.Select(e => $"{e.Gesture,-14}{e.Label}"));
}
