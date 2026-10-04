using System.Drawing;
using System.Drawing.Drawing2D;
using FlowRing.DesktopHost.Host;

namespace FlowRing.DesktopHost.Tray;

/// <summary>
/// 系统托盘完整版。状态变化显示 Running（绿点）+ Paused（灰点）+ Idle（黄点）。
/// 菜单项 click 调用 HostController 路由到 WebView2 加载对应页面或调用 host API。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly HostController _controller;
    private NotifyIcon? _notifyIcon;

    public TrayIcon(HostController controller)
    {
        _controller = controller;
    }

    public void Initialize()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Studio", null, (_, _) => _controller.NavigateTo("studio"));
        menu.Items.Add("打开 Profile Manager", null, (_, _) => _controller.NavigateTo("profiles"));
        menu.Items.Add(new ToolStripSeparator());
        var paused = new ToolStripMenuItem("暂停 Flow Ring")
        {
            CheckOnClick = true,
            Checked = _controller.IsPaused,
        };
        paused.CheckedChanged += (_, _) =>
        {
            _controller.SetPaused(paused.Checked);
            UpdateIcon();
        };
        menu.Items.Add(paused);
        menu.Items.Add("设置", null, (_, _) => _controller.NavigateTo("settings"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _controller.ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = BuildIcon(_controller.IsPaused, _controller.IsActive),
            Visible = true,
            Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.NavigateTo("studio");
    }

    public void UpdateIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Icon = BuildIcon(_controller.IsPaused, _controller.IsActive);
        _notifyIcon.Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）";
    }

    public void ShowNotification(string text, string title = "Flow Ring")
    {
        _notifyIcon?.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);
    }

    /// <summary>
    /// 状态图标（16x16）：Flow Ring 标形——细线圆环 + 状态色弧光。
    /// 运行中 = 绿色弧光；暂停 = 灰色；未激活 = 黄色。
    /// HICON 生命周期：取句柄后立刻 Clone 出自有副本并 DestroyIcon——
    /// 避免历史 bug（句柄随 Bitmap 释放而失效 / 每次刷新泄漏一个 HICON）。
    /// </summary>
    private static Icon BuildIcon(bool paused, bool active)
    {
        var arcColor = paused
            ? Color.FromArgb(255, 150, 150, 150)
            : (active ? Color.FromArgb(255, 90, 200, 110) : Color.FromArgb(255, 214, 200, 74));
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            // 细线圆环
            using var ringPen = new Pen(Color.FromArgb(190, 230, 230, 225), 1.6f);
            g.DrawEllipse(ringPen, 2.6f, 2.6f, 10.8f, 10.8f);
            // 状态色弧光（右上，约 116°）
            using var arcPen = new Pen(arcColor, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arcPen, 2.4f, 2.4f, 11.2f, 11.2f, -112f, 116f);
        }
        var hicon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hicon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}