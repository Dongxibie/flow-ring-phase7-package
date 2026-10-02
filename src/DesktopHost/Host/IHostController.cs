namespace FlowRing.DesktopHost.Host;

/// <summary>
/// HostController 的可测接口。WebView2Host / TrayIcon / MainWindow 都依赖此接口。
/// Phase 5b polish 新增，让 WebView2Host 可在测试中传 Mock。
/// </summary>
public interface IHostController
{
    bool IsPaused { get; }
    bool IsActive { get; }
    void NavigateTo(string route);
    void SetPaused(bool paused);
    void ExitApp();
}