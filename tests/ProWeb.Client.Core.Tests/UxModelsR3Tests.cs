using FluentAssertions;
using ProWeb.Client.Core;
using Xunit;

namespace ProWeb.Client.Core.Tests;

/// <summary>UT-X-R3-001: About + keyboard shortcut cheat sheet.</summary>
public class AboutAndCheatSheetTests
{
    [Fact]
    public void AboutInfo_HasProductVersionAndBuildDate()
    {
        AboutInfo.ProductName.Should().Be("ProWeb");
        AboutInfo.Version.Should().NotBeNullOrWhiteSpace();
        AboutInfo.BuildDate.Should().NotBeNullOrWhiteSpace();
        AboutInfo.Describe().Should().Contain("ProWeb").And.Contain(AboutInfo.Version);
    }

    [Fact]
    public void CheatSheet_HasOneEntryPerBinding_AndMatchesGestures()
    {
        ShortcutCheatSheet.Entries.Should().HaveCount(ShortcutTable.Bindings.Count);
        foreach (var entry in ShortcutCheatSheet.Entries)
        {
            entry.Label.Should().NotBeNullOrWhiteSpace();
            entry.Gesture.Should().Be(ShortcutTable.GestureFor(entry.Command));
        }
    }

    [Fact]
    public void CheatSheet_RenderText_IsNonEmpty_AndListsGestures()
    {
        var text = ShortcutCheatSheet.RenderText();
        text.Should().Contain("Ctrl+T");
        text.Should().Contain("Ctrl+F");
    }
}

/// <summary>UT-X-R3-003: Ctrl+F find-in-page binding, and UT-X-R3-002 home resolution.</summary>
public class FindAndHomeTests
{
    [Fact]
    public void ShortcutTable_ContainsFindInPage_BoundToCtrlF()
    {
        ShortcutTable.GestureFor(BrowserCommand.FindInPage).Should().Be("Ctrl+F");
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("example.com", "https://example.com")]
    public void HomeResolver_ResolvesConfiguredHome(string home, string expected)
    {
        var settings = new ClientSettings { HomePage = home };
        HomeNavigationResolver.ResolveHomeTarget(settings).Should().Be(expected);
        HomeNavigationResolver.HasHome(settings).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HomeResolver_EmptyHome_FallsBackToNull(string home)
    {
        var settings = new ClientSettings { HomePage = home };
        HomeNavigationResolver.ResolveHomeTarget(settings).Should().BeNull();
        HomeNavigationResolver.HasHome(settings).Should().BeFalse();
    }

    [Fact]
    public void HomeResolver_NullSettings_IsNull()
    {
        HomeNavigationResolver.ResolveHomeTarget(null).Should().BeNull();
    }
}

/// <summary>UT-X-R3-005: feedback / report-problem entry with RequestId.</summary>
public class FeedbackInfoTests
{
    [Fact]
    public void BuildMailto_WithoutRequestId_IsValidMailto()
    {
        var link = FeedbackInfo.BuildMailto();
        link.Should().StartWith("mailto:" + FeedbackInfo.ContactEmail);
        link.Should().Contain("subject=");
    }

    [Fact]
    public void BuildMailto_WithRequestId_EmbedsIt()
    {
        var link = FeedbackInfo.BuildMailto("req-123");
        Uri.UnescapeDataString(link).Should().Contain("req-123");
    }

    [Fact]
    public void ErrorPage_IncludesReportLinkWithRequestId()
    {
        var html = ErrorPageModel.Render(502, "req-xyz", "boom");
        html.Should().Contain("报告问题");
        html.Should().Contain("mailto:");
        html.Should().Contain("req-xyz");
    }
}
