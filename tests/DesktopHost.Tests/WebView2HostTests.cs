using FlowRing.DesktopHost.Host;
using Xunit;

namespace FlowRing.DesktopHost.Tests;

public class WebView2HostTests
{
    [Fact]
    public void ConstructorBeforeInitializeIsInitializedFalse()
    {
        var controller = new MockHostController();
        var host = new WebView2Host(controller);
        Assert.False(host.IsInitialized);
    }

    [Fact]
    public void NavigateToBeforeInitializeDoesNotThrow()
    {
        var controller = new MockHostController();
        var host = new WebView2Host(controller);
        var ex = Record.Exception(() => host.NavigateTo("studio"));
        Assert.Null(ex);
    }

    [Fact]
    public void NavigateToAfterDisposeDoesNotThrow()
    {
        var controller = new MockHostController();
        var host = new WebView2Host(controller);
        host.Dispose();
        var ex = Record.Exception(() => host.NavigateTo("studio"));
        Assert.Null(ex);
    }

    [Fact]
    public void DisposeCalledTwiceDoesNotThrow()
    {
        var controller = new MockHostController();
        var host = new WebView2Host(controller);
        host.Dispose();
        var ex = Record.Exception(() => host.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void DisposeClearsState()
    {
        var controller = new MockHostController();
        var host = new WebView2Host(controller);
        host.Dispose();
        Assert.False(host.IsInitialized);
    }

    private sealed class MockHostController : IHostController
    {
        public bool IsPaused => false;
        public bool IsActive => false;
        public void NavigateTo(string route) { }
        public void SetPaused(bool paused) { }
        public void ExitApp() { }
    }
}