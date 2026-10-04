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
/// v24：环在按下点【固定】显示（不再跟随光标，光标从环心向外拖=选择方向）；
/// 窗口尺寸按 DPI 缩放换算（修复环被窗口裁切、右侧标注被截一半）；
/// 非激活浮层（WS_EX_NOACTIVATE，绝不抢焦点、不打断前台应用）；每次显示前回发 OVERLAY_ON。
/// </summary>
public sealed class RingOverlayForm : Form
{
    private const string VirtualHost = "flowring.local";

    /// <summary>色键：环以外像素的挖除色（不会与任何 UI 颜色撞车）。</summary>
    private static readonly Color Chroma = Color.FromArgb(255, 1, 2, 3);

    private readonly HostController _controller;
    private readonly string _frontendDist;
    private readonly CoreWebView2Environment? _environment;
    private readonly ILogger<RingOverlayForm> _logger;
    private readonly WebView2 _webView = new();
    private bool _contentReady;

    public RingOverlayForm(
        HostController controller,
        ILoggerFactory? loggerFactory = null,
        string? frontendDist = null,
        CoreWebView2Environment? environment = null)
    {
        _controller = controller;
        _frontendDist = frontendDist ?? string.Empty;
        _environment = environment;
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
    }

    /// <summary>v24：非激活浮层——Show() 不抢焦点，绝不打断前台应用（键盘类动作才能落到目标应用）。</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>v24：WS_EX_NOACTIVATE（不抢焦点）+ WS_EX_TOOLWINDOW（不进 Alt-Tab）。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return cp;
        }
    }

    /// <summary>预加载：创建句柄 + 初始化 WebView2。在 UI 线程调用一次。</summary>
    public async Task InitializeAsync()
    {
        _ = Handle; // 强制创建句柄，后续 BeginInvoke 才可用
        try
        {
            await _webView.EnsureCoreWebView2Async(_environment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快捷环 WebView2 EnsureCoreWebView2Async 失败");
        }
    }

    /// <summary>
    /// v24：在光标处显示环并【固定】在那里（不再跟随光标）。
    /// 返回环心（屏幕物理像素坐标）——宿主以它为方向判定与高亮偏移的原点。
    /// 环整体夹取在屏幕工作区内，保证完整可见；窗口尺寸 = (环径+留白) × DPI 缩放（CSS px → 物理像素）。
    /// 任意线程可调（实际都在 UI 线程）。
    /// </summary>
    public Point ShowRing(int ringSizePx)
    {
        if (InvokeRequired)
        {
            var fallback = Cursor.Position;
            _ = BeginInvoke(() => ShowRing(ringSizePx));
            return fallback;
        }

        var margin = 44;
        var scale = DeviceDpi > 0 ? DeviceDpi / 96.0 : 1.0;
        var win = (int)Math.Round((ringSizePx + margin * 2) * scale);
        Size = new Size(win, win);

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var cx = Math.Max(area.Left + Width / 2, Math.Min(Cursor.Position.X, area.Right - Width / 2));
        var cy = Math.Max(area.Top + Height / 2, Math.Min(Cursor.Position.Y, area.Bottom - Height / 2));
        Location = new Point(cx - Width / 2, cy - Height / 2);

        TopMost = true;
        Show(); // v24：不调 Activate()——非激活浮层

        // v22.2：每次显示都让前端重新进入覆盖层模式（修复"只有第一次是圆环"）
        if (_contentReady)
        {
            PostToPage("{\"type\":\"OVERLAY_ON\"}");
        }
        _logger.LogInformation("快捷环已显示（环径 {Size}px，窗口 {Win}px）@ ({X},{Y})", ringSizePx, win, cx, cy);
        return new Point(cx, cy);
    }

    /// <summary>v23：向快捷环页面回发消息（OVERLAY_ON / FOLLOW / RELEASE_SELECT / OVERLAY_CLOSE）。</summary>
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
                case "DEAD_ZONE":
                    // v24：与主窗一致——仅当 radius 是可解析的整数才更新（缺失字段不改动）
                    if (doc.RootElement.TryGetProperty("radius", out var r) && r.TryGetInt32(out var rv))
                    {
                        _controller.SetDeadZone(rv);
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
