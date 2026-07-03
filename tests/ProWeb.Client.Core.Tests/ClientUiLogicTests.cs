using System.Text;
using FluentAssertions;
using ProWeb.Client.Core;
using ProWeb.Shared.Protocol;
using Xunit;

namespace ProWeb.Client.Core.Tests;

/// <summary>Records whether the encrypted channel was used and asserts no direct target connect.</summary>
internal sealed class StubProxyChannel : IProxyChannel
{
    private readonly Func<string, ResponseEnvelope> _responder;

    public StubProxyChannel(bool connected, Func<string, ResponseEnvelope>? responder = null)
    {
        IsConnected = connected;
        _responder = responder ?? (url => new ResponseEnvelope
        {
            RequestId = "r",
            StatusCode = 200,
            Body = Encoding.UTF8.GetBytes("proxied:" + url),
            ContentType = "text/html",
            FinalUrl = url,
        });
    }

    public bool IsConnected { get; }

    public string? SessionId => IsConnected ? "sid" : null;

    public int FetchCalls { get; private set; }

    public Func<string, ResponseEnvelope>? OverrideThrow { get; set; }

    public Task HandshakeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ResponseEnvelope> FetchAsync(
        string url, string method = "GET",
        IReadOnlyDictionary<string, string>? headers = null, byte[]? body = null,
        CancellationToken cancellationToken = default)
    {
        FetchCalls++;
        return Task.FromResult(_responder(url));
    }

    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class ProxyInterceptionCoordinatorTests
{
    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html,hi")]
    [InlineData("blob:https://x/y")]
    [InlineData("javascript:void(0)")]
    public async Task LocalScheme_LoadsNatively_WithoutTouchingChannel(string url)
    {
        var channel = new StubProxyChannel(connected: true);
        var coordinator = new ProxyInterceptionCoordinator(channel);

        coordinator.Decide(url).Should().Be(InterceptionDecision.LoadNatively);
        var result = await coordinator.HandleAsync(url);

        result.ServesContent.Should().BeFalse();
        channel.FetchCalls.Should().Be(0, "local schemes must never hit the proxy channel");
    }

    [Fact]
    public async Task Http_WithSession_GoesThroughEncryptedChannel()
    {
        var channel = new StubProxyChannel(connected: true);
        var coordinator = new ProxyInterceptionCoordinator(channel);

        coordinator.Decide("https://example.com/").Should().Be(InterceptionDecision.Proxy);
        var result = await coordinator.HandleAsync("https://example.com/");

        result.Decision.Should().Be(InterceptionDecision.Proxy);
        result.ServesContent.Should().BeTrue();
        Encoding.UTF8.GetString(result.Body).Should().Be("proxied:https://example.com/");
        channel.FetchCalls.Should().Be(1);
    }

    [Fact]
    public async Task Http_WithoutSession_IsBlocked_NoDirectConnect()
    {
        var channel = new StubProxyChannel(connected: false);
        var coordinator = new ProxyInterceptionCoordinator(channel);

        coordinator.Decide("https://example.com/").Should().Be(InterceptionDecision.BlockNoSession);
        var result = await coordinator.HandleAsync("https://example.com/");

        result.Decision.Should().Be(InterceptionDecision.BlockNoSession);
        result.StatusCode.Should().Be(503);
        result.ServesContent.Should().BeTrue("an error page is served instead of a direct connection");
        Encoding.UTF8.GetString(result.Body).Should().Contain("会话");
        channel.FetchCalls.Should().Be(0, "no session ⇒ absolutely no network fetch");
    }

    [Fact]
    public async Task ProxyFailure_RendersErrorPage()
    {
        var channel = new StubProxyChannel(connected: true,
            responder: _ => throw new ProxyRequestException(502, "req-9"));
        var coordinator = new ProxyInterceptionCoordinator(channel);

        var result = await coordinator.HandleAsync("https://example.com/");
        result.StatusCode.Should().Be(502);
        Encoding.UTF8.GetString(result.Body).Should().Contain("req-9");
    }
}

public class AddressValidationModelTests
{
    [Theory]
    [InlineData("https://example.com", AddressFieldState.Url)]
    [InlineData("example.com", AddressFieldState.Url)]
    [InlineData("hello world search", AddressFieldState.Search)]
    [InlineData("ht!tp://broken", AddressFieldState.Invalid)]
    [InlineData("http://", AddressFieldState.Invalid)]
    [InlineData("", AddressFieldState.Empty)]
    public void Evaluate_ClassifiesInput(string input, AddressFieldState expected)
    {
        AddressValidationModel.Evaluate(input).State.Should().Be(expected);
    }

    [Fact]
    public void InvalidInput_IsFlaggedAsError_WithMessage()
    {
        var r = AddressValidationModel.Evaluate("http://");
        r.IsError.Should().BeTrue();
        r.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidInput_IsNotError()
    {
        AddressValidationModel.Evaluate("https://example.com").IsError.Should().BeFalse();
    }
}

public class SecurityStatusModelTests
{
    [Fact]
    public void Secure_ShowsLockAndIsSecure()
    {
        var m = SecurityStatusModel.For(SecureChannelState.Secure);
        m.IsSecure.Should().BeTrue();
        m.Text.Should().Contain("🔒");
        m.Tooltip.Should().NotBeEmpty();
    }

    [Fact]
    public void Disconnected_IsNotSecure()
    {
        SecurityStatusModel.For(SecureChannelState.Disconnected).IsSecure.Should().BeFalse();
    }

    [Fact]
    public void From_ReflectsChannelConnection()
    {
        SecurityStatusModel.From(new StubProxyChannel(connected: true)).IsSecure.Should().BeTrue();
        SecurityStatusModel.From(new StubProxyChannel(connected: false)).IsSecure.Should().BeFalse();
    }
}

public class LoadProgressModelTests
{
    [Fact]
    public void Start_SetsLoading_ActionBecomesStop()
    {
        var m = new LoadProgressModel();
        m.PrimaryAction.Should().Be(PrimaryLoadAction.Reload);
        m.Start();
        m.IsLoading.Should().BeTrue();
        m.PrimaryAction.Should().Be(PrimaryLoadAction.Stop);
        m.ProgressVisible.Should().BeTrue();
    }

    [Fact]
    public void Report_ClampsProgress()
    {
        var m = new LoadProgressModel();
        m.Start();
        m.Report(1.5);
        m.Progress.Should().Be(1.0);
        m.Report(-0.5);
        m.Progress.Should().Be(0.0);
    }

    [Fact]
    public void Complete_ClearsLoading_ActionBecomesReload()
    {
        var m = new LoadProgressModel();
        m.Start();
        m.Complete();
        m.IsLoading.Should().BeFalse();
        m.PrimaryAction.Should().Be(PrimaryLoadAction.Reload);
        m.ProgressVisible.Should().BeFalse();
    }
}

public class ClosedTabStackTests
{
    [Fact]
    public void Reopen_IsLifo()
    {
        var stack = new ClosedTabStack();
        stack.Push(new ClosedTab("https://a", "A"));
        stack.Push(new ClosedTab("https://b", "B"));

        stack.CanReopen.Should().BeTrue();
        stack.Reopen()!.Url.Should().Be("https://b");
        stack.Reopen()!.Url.Should().Be("https://a");
        stack.Reopen().Should().BeNull();
        stack.CanReopen.Should().BeFalse();
    }

    [Fact]
    public void Push_RespectsCapacity()
    {
        var stack = new ClosedTabStack(capacity: 2);
        stack.Push(new ClosedTab("https://a", "A"));
        stack.Push(new ClosedTab("https://b", "B"));
        stack.Push(new ClosedTab("https://c", "C"));

        stack.Count.Should().Be(2);
        stack.Reopen()!.Url.Should().Be("https://c");
        stack.Reopen()!.Url.Should().Be("https://b");
        stack.Reopen().Should().BeNull("the oldest entry was evicted");
    }
}

public class NewTabPageModelTests
{
    [Fact]
    public void Render_ContainsMarkerAndPrompt()
    {
        var html = NewTabPageModel.Render();
        html.Should().Contain(NewTabPageModel.Marker);
        html.Should().Contain("地址栏");
    }
}

public class ShortcutTableTests
{
    [Fact]
    public void AllCommands_HaveAGesture()
    {
        foreach (BrowserCommand cmd in Enum.GetValues(typeof(BrowserCommand)))
            ShortcutTable.GestureFor(cmd).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CoreShortcuts_MatchExpectedGestures()
    {
        ShortcutTable.GestureFor(BrowserCommand.NewTab).Should().Be("Ctrl+T");
        ShortcutTable.GestureFor(BrowserCommand.CloseTab).Should().Be("Ctrl+W");
        ShortcutTable.GestureFor(BrowserCommand.ReopenClosedTab).Should().Be("Ctrl+Shift+T");
        ShortcutTable.GestureFor(BrowserCommand.FocusAddressBar).Should().Be("Ctrl+L");
    }
}

public class ErrorPageLoadFailureTests
{
    [Fact]
    public void RenderLoadFailure_IncludesUrlErrorAndRetry()
    {
        var html = ErrorPageModel.RenderLoadFailure(
            "https://example.com/x", "ERR_TIMED_OUT", "connection timed out", "req-1");
        html.Should().Contain("https://example.com/x");
        html.Should().Contain("ERR_TIMED_OUT");
        html.Should().Contain("req-1");
        html.Should().Contain("重试");
    }

    [Fact]
    public void RenderLoadFailure_EncodesHostileInput()
    {
        var html = ErrorPageModel.RenderLoadFailure(
            "https://x/<script>", "C", "<b>", "r");
        html.Should().NotContain("<script>");
        html.Should().Contain("&lt;script&gt;");
    }
}
