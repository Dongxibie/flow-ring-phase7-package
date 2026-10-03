using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FlowRing.DesktopHost.UI;

/// <summary>
/// v21：快捷环弹窗窗体——侧键长按唤起的运行时形态。
/// 无边框、置顶、不进任务栏、圆角；内嵌 WebView2 打开 index.html#overlay
/// （前端据此自动进入覆盖层模式：只有单纯的圆环，功能标注在扇区里）。
/// 生命周期：宿主启动时隐藏预加载；侧键 Intent → ShowRing()；触发/ESC → 前端
/// 回发 OVERLAY_DONE → HideRing()；点击窗外（Deactivate）→ HideRing()。
/// </summary>
public sealed class RingOverlayForm : Form
{
    private const int GripRadius = 28;

    private readonly HostController _controller;
    private readonly ILogger<RingOverlayForm> _logger;
    private readonly WebView2 _webView = new();

    public RingOverlayForm(HostController controller, ILoggerFactory? loggerFactory = null)
    {
        _controller = controller;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<RingOverlayForm>();

        Text = "Flow Ring — 快捷环";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(720, 720);
        BackColor = Color.FromArgb(11, 12, 10);
        SetRoundRegion();

        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.FromArgb(11, 12, 10);
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
            _webView.CoreWebView2.WebMessageReceived += OnWebMessage;
            _webView.CoreWebView2.Navigate("https://flowring.local/index.html#overlay");
            _logger.LogInformation("快捷环 WebView2 已就绪并导航到 #overlay");
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

    /// <summary>在光标所在屏幕居中显示（可在任意线程调用，内部 marshal 到 UI 线程）。</summary>
    public void ShowRing()
    {
        if (InvokeRequired)
        {
            _ = BeginInvoke(ShowRing);
            return;
        }

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            area.Left + ((area.Width - Width) / 2),
            area.Top + ((area.Height - Height) / 2));
        TopMost = true;
        Show();
        Activate();
        _logger.LogInformation("快捷环弹窗已显示 @ {Screen}", area);
    }

    public void HideRing()
    {
        if (InvokeRequired)
        {
            _ = BeginInvoke(HideRing);
            return;
        }
        Hide();
        _logger.LogInformation("快捷环弹窗已隐藏");
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快捷环 WebMessage 处理失败");
        }
    }

    private void SetRoundRegion()
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(0, 0, GripRadius, GripRadius, 180, 90);
        path.AddArc(Width - GripRadius - 1, 0, GripRadius, GripRadius, 270, 90);
        path.AddArc(Width - GripRadius - 1, Height - GripRadius - 1, GripRadius, GripRadius, 0, 90);
        path.AddArc(0, Height - GripRadius - 1, GripRadius, GripRadius, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }

}
