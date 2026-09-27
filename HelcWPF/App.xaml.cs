// ============================================================================
//  App.xaml.cs —— 程序启动入口（只有环境相关的三件事 + 建窗口）
//
//      ① 配置日志（Serilog → exe 同目录 logs\vision-日期.log）
//      ② 注册全局异常兜底（界面线程 + 后台线程）
//      ③ 初始化 HALCON 环境（★ 必须在任何算子调用之前）
//      ④ 创建并显示主窗口
//
//  HALCON 环境三件事的细节见 Halcon\HalconEnvironment.cs。
// ============================================================================

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace HelcWPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ① 日志
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "vision-.log"),
                          rollingInterval: RollingInterval.Day,
                          outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // ② 全局异常兜底：上位机最忌讳"一个异常整个程序没了"
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "界面线程未处理异常");
            MessageBox.Show(args.Exception.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;                  // 不让程序直接崩掉
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "后台线程未处理异常");

        // ③ HALCON 环境（★ 必须在创建窗口、调用算子之前）
        try
        {
            HalconEnvironment.Init();
            Log.Information("HALCON {Version} 就绪 | HALCONROOT={Root} | license {Count} 个 | {Bits} 位进程",
                HalconEnvironment.Version, HalconEnvironment.Root,
                HalconEnvironment.LicenseFileCount(), Environment.Is64BitProcess ? 64 : 32);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "HALCON 初始化失败");
            MessageBox.Show("HALCON 初始化失败：\n" + ex.Message, "启动失败",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // ④ 组装三层：Service（干活）→ ViewModel（状态+命令）→ View（画面）
        //    ★ 改：原来直接 new MainWindow()，ViewModel 由窗口自己 new。
        //          现在改成 App 来组装（"谁创建、谁负责"更清楚，View 也不需要认识 Service）
        var source = new ImageSourceService();
        var viewModel = new MainViewModel(source);

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("程序退出");
        Log.CloseAndFlush();                      // 确保日志落盘
        base.OnExit(e);
    }
}
