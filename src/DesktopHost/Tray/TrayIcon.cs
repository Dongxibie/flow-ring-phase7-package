using System.Drawing;
using System.Drawing.Drawing2D;
using FlowRing.DesktopHost.Host;

namespace FlowRing.DesktopHost.Tray;

public sealed class TrayIcon : IDisposable
{
    private readonly HostController _controller;
    private NotifyIcon? _notifyIcon;
    private Icon? _iconRunning;
    private Icon? _iconPaused;
    private Icon? _iconIdle;

    public TrayIcon(HostController controller) { _controller = controller; }

    public void Initialize()
    {
        _iconRunning = BuildIcon(false, true);
        _iconPaused = BuildIcon(true, false);
        _iconIdle = BuildIcon(false, false);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Studio", null, (_, _) => _controller.NavigateTo("studio"));
        menu.Items.Add("打开 Profile Manager", null, (_, _) => _controller.NavigateTo("profiles"));
        menu.Items.Add(new ToolStripSeparator());
        var paused = new ToolStripMenuItem("暂停 Flow Ring") { CheckOnClick = true, Checked = _controller.IsPaused };
        paused.CheckedChanged += (_, _) => { _controller.SetPaused(paused.Checked); UpdateIcon(); };
        menu.Items.Add(paused);
        menu.Items.Add("设置", null, (_, _) => _controller.NavigateTo("settings"));
        menu.Items.Add("关于", null, (_, _) => _controller.NavigateTo("about"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _controller.ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = CurrentIcon(),
            Visible = true,
            Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）",
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => _controller.NavigateTo("studio");
    }

    private Icon CurrentIcon()
        => _controller.IsPaused ? _iconPaused! : (_controller.IsActive ? _iconRunning! : _iconIdle!);

    public void UpdateIcon()
    {
        if (_notifyIcon is null) return;
        _notifyIcon.Icon = CurrentIcon();
        _notifyIcon.Text = _controller.IsPaused ? "Flow Ring（暂停）" : "Flow Ring（运行）";
    }

    public void ShowNotification(string text, string title = "Flow Ring")
        => _notifyIcon?.ShowBalloonTip(8000, title, text, ToolTipIcon.Info);

    private static Icon BuildIcon(bool paused, bool active)
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var color = paused ? Color.Gray : (active ? Color.FromArgb(255, 0, 180, 0) : Color.FromArgb(255, 220, 200, 0));
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Icon?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _iconRunning?.Dispose();
        _iconPaused?.Dispose();
        _iconIdle?.Dispose();
        _iconRunning = null;
        _iconPaused = null;
        _iconIdle = null;
    }
}