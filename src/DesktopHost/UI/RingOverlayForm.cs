using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// v22.2：快捷环弹窗——"只有环本体"的透明浮层。
/// WebView2 背景透明 + 窗体色键（TransparencyKey）挖掉环以外的一切：
/// 看得穿、点得穿，视野里只有圆环本身（修复"很大一坨方块占视野"）。
/// 位置跟随光标（环中心=光标，夹在屏幕工作区内）；尺寸随设置实时变化。
/// 每次显示前回发 OVERLAY_ON，前端重新进入覆盖层模式（修复"只有一次是圆环"）。
/// </summary>
public sealed class RingOverlayForm : Form
{
    private const string VirtualHost = "flowring.local";

    /// <summary>色键：环以外像素的挖除色（不会与任何 UI 颜色撞车）。</summary>
    private static readonly Color Chroma = Color.FromArgb(255, 1, 2, 3);

    private readonly HostController _controller;
    private readonly string _frontendDist;
    private readonly ILogger<RingOverlayForm> _logger;
    private readonly WebView2 _webView = new();
    private bool _contentReady;

    public RingOverlayForm(HostController controller, ILoggerFactory? loggerFactory = null, string? frontendDist = null)
    {
        _controller = controller;
        _frontendDist = frontendDist ?? string.Empty;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<RingOverlayForm>();

        Text = "Flow Ring — 快捷环";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(580, 580);
        BackColor = Chroma;
        TransparencyKey = Chroma;

        // v22.2：WebView2 背景透明——网页里环以外的部分直接透出桌面
        _webView.DefaultBackgroundColor = Color.Transparent;
        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);

        _webView.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                _logger.LogError(e.InitializationException, "快捷环 WebView2 初始化失败");
                return;
            }
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            // v22.1：弹窗是独立的 CoreWebView2 实例，虚拟主机映射必须自己配一份
            if (!string.IsNullOrEmpty(_frontendDist))
            {
                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost, _frontendDist, CoreWebView2HostResourceAccessKind.Allow);
            }
            _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
            _webView.CoreWebView2.Navigate($"https://{VirtualHost}/index.html#overlay");
            _contentReady = true;
            _logger.LogInformation("快捷环 WebView2 已就绪并导航到 #overlay（dist={Dist}）", _frontendDist);
        };

        Deactivate += (_, _) =>
        {
            // 点击窗外即关闭（与覆盖层"点空白关闭"一致）
            if (Visible)
            {
                HideRing();
            }
        };
    }

    /// <summary>预加载：创建句柄 + 初始化 WebView2。在 UI 线程调用一次。</summary>
    public async Task InitializeAsync()
    {
        _ = Handle; // 强制创建句柄，后续 BeginInvoke 才可用
        try
        {
            await _webView.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快捷环 WebView2 EnsureCoreWebView2Async 失败");
        }
    }

    /// <summary>在光标处显示环（环中心=光标，夹在屏幕工作区内）。任意线程可调。</summary>
    public void ShowRing(int ringSizePx)
    {
        if (InvokeRequired)
        {
            _ = BeginInvoke(() => ShowRing(ringSizePx));
            return;
        }

        var margin = 44;
        Size = new Size(ringSizePx + margin * 2, ringSizePx + margin * 2);

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var cx = Math.Max(area.Left + Width / 2, Math.Min(Cursor.Position.X, area.Right - Width / 2));
        var cy = Math.Max(area.Top + Height / 2, Math.Min(Cursor.Position.Y, area.Bottom - Height / 2));
        Location = new Point(cx - Width / 2, cy - Height / 2);

        TopMost = true;
        Show();
        Activate();

        // v22.2：每次显示都让前端重新进入覆盖层模式（修复"只有第一次是圆环"）
        if (_contentReady)
        {
            PostToPage("{\"type\":\"OVERLAY_ON\"}");
        }
        _logger.LogInformation("快捷环已显示（环径 {Size}px）@ 光标", ringSizePx);
    }

    /// <summary>
    /// v23：跟随模式——环中心以 lerp 追光标（弹性跟手），返回光标相对环心的偏移。
    /// 在 UI 线程调用。
    /// </summary>
    public (int Dx, int Dy) FollowCursor(int cursorX, int cursorY)
    {
        if (InvokeRequired)
        {
            // 跟随是高频调用，绝不能阻塞钩子线程：直接 BeginInvoke 并返回粗略偏移
            _ = BeginInvoke(() => FollowCursor(cursorX, cursorY));
            var a0 = Cursor.Position;
            return (cursorX - a0.X, cursorY - a0.Y);
        }

        var half = Width / 2;
        var centerX = Location.X + half;
        var centerY = Location.Y + Height / 2;
        var ncx = centerX + (int)((cursorX - centerX) * 0.42);
        var ncy = centerY + (int)((cursorY - centerY) * 0.42);

        var area = Screen.FromPoint(new Point(ncx, ncy)).WorkingArea;
        ncx = Math.Max(area.Left + half, Math.Min(ncx, area.Right - half));
        ncy = Math.Max(area.Top + Height / 2, Math.Min(ncy, area.Bottom - Height / 2));
        Location = new Point(ncx - half, ncy - Height / 2);

        return (cursorX - ncx, cursorY - ncy);
    }

    /// <summary>v23：向快捷环页面回发消息（OVERLAY_ON / FOLLOW / RELEASE_SELECT）。</summary>
    public void PostToPage(string json)
    {
        if (InvokeRequired)
        {
            _ = BeginInvoke(() => PostToPage(json));
            return;
        }
        if (_contentReady)
        {
            _webView.CoreWebView2?.PostWebMessageAsString(json);
        }
    }

    public void HideRing()
    {
        if (InvokeRequired)
        {
            _ = BeginInvoke(HideRing);
            return;
        }
        Hide();
        _logger.LogInformation("快捷环已隐藏");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            switch (type)
            {
                case "ACTION_TRIGGER":
                    var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
                    var arg = doc.RootElement.TryGetProperty("arg", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String ? a.GetString() : null;
                    if (!string.IsNullOrEmpty(code))
                    {
                        _logger.LogInformation("快捷环触发动作：{Code}", code);
                        _ = _controller.ExecuteActionAsync(code, arg);
                    }
                    break;
                case "OVERLAY_DONE":
                    HideRing();
                    break;
                case "RING_SIZE":
                    var size = doc.RootElement.TryGetProperty("size", out var sz) && sz.TryGetInt32(out var s) ? s : 0;
                    if (size > 0)
                    {
                        _controller.SetRingSize(size);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快捷环 WebMessage 处理失败");
        }
    }
}
