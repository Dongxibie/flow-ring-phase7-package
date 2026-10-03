using FlowRing.DesktopHost.Host;
using Microsoft.Extensions.Logging;

namespace FlowRing.DesktopHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetCompatibleTextRenderingDefault(false);

        // 用 ConsoleLoggerProvider 让日志输出到 stdout
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss ";
            })
            .SetMinimumLevel(LogLevel.Information));

        // v19 诊断：记录 Main 线程的 ID 与 apartment 状态，用于对照后续所有 WebView2 相关回调的线程
        var bootLogger = loggerFactory.CreateLogger("boot");
        bootLogger.LogInformation(
            "Main 线程 {ThreadId}，apartment {Apt}",
            Environment.CurrentManagedThreadId,
            Thread.CurrentThread.GetApartmentState());

        var controller = new HostController(loggerFactory);
        using var cts = new CancellationTokenSource();
        Application.ApplicationExit += (_, _) => cts.Cancel();

        var startTask = controller.StartAsync(cts.Token);

        try
        {
            Application.Run();
        }
        finally
        {
            controller.Dispose();
        }

        try
        {
            startTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Host 启动失败：{ex.Message}");
            return 1;
        }
        return 0;
    }
}