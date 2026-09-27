// ============================================================================
//  Views/MainWindow.xaml.cs —— 窗口自身行为 + 把图像显示到 HALCON 大屏
//
//  显示就三行： SetPart(按比例算好的显示区域) + ClearWindow + DispObj
//  比例为什么不能交给控件：控件的 HKeepAspectRatio 只在"控件尺寸变化时"生效，
//  初次显示和手动 disp 时它不管 → 图会被拉满窗口（实测会拉伸到 1.88 / 2.20 的比例）。
//
//  画图必须等 HInitWindow（HALCON 窗口句柄就绪），否则报 #2453 HALCON handle is NULL。
// ============================================================================

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using HalconDotNet;
using Serilog;                              // ★ 补：_log 要用

namespace HelcWPF;

public partial class MainWindow : Window
{
    private readonly ILogger _log = Log.ForContext<MainWindow>();   // ★ 补：日志
    private readonly MainViewModel _viewModel;

    private bool _halconReady;      // ★ 补：HInitWindow 之后才为 true
    private HImage? _shown;         // ★ 补：已经显示过的图像（同一张不重复画）

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;          // ★ 绑定总开关：XAML 里的 {Binding} 全指向它

        // ① ViewModel 的"当前图像"变了 → 由 View 负责显示
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // ② 等 HALCON 窗口初始化完成（句柄就绪）才能画图
        HalconView.HInitWindow += (_, _) =>
        {
            _halconReady = true;
            _log.Information("[图像] HALCON 窗口初始化完成（句柄已可用）");
            ShowCurrentImage();           // 可能 ViewModel 早就准备好了图，这里补显示
        };

        // ③ 窗口大小变了 → 重新按比例适应（比例是我们自己算的，所以要重算）
        HalconView.SizeChanged += (_, _) =>
        {
            if (_halconReady && _viewModel.CurrentImage is not null) FitAndDraw();
        };
    }

    #region 图像显示

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentImage))
            ShowCurrentImage();
    }

    private void ShowCurrentImage()
    {
        if (!_halconReady) return;                     // 句柄没好，等 HInitWindow

        var image = _viewModel.CurrentImage;
        if (image is null || ReferenceEquals(image, _shown)) return;

        _shown = image;
        FitAndDraw();
    }

    /// <summary>
    /// 把整幅图按比例显示到窗口：
    ///   part 的宽高比 = 窗口宽高比，且刚好包住图像
    ///   → 短的那边落在图像外，显示成窗口背景（黑边），图像本身不变形
    /// </summary>
    private void FitAndDraw()
    {
        var image = _viewModel.CurrentImage;
        if (image is null || !_halconReady) return;

        HalconView.HalconWindow.GetWindowExtents(out int _, out int _, out int winW, out int winH);
        if (winW <= 0 || winH <= 0) return;

        HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
        double imgW = w.D, imgH = h.D;

        double winAspect = (double)winW / winH;
        double partW, partH;
        if (imgW / imgH > winAspect)      // 图比窗口"宽" → 上下留黑边
        {
            partW = imgW;
            partH = imgW / winAspect;
        }
        else                              // 图比窗口"高" → 左右留黑边
        {
            partH = imgH;
            partW = imgH * winAspect;
        }

        double row1 = imgH / 2 - partH / 2;
        double col1 = imgW / 2 - partW / 2;

        try
        {
            HWindow window = HalconView.HalconWindow;
            window.SetPart(row1, col1, row1 + partH, col1 + partW);   // 显示区域
            window.ClearWindow();
            window.DispObj(image);                                   // 画图像
            // ★ 步骤3 的检测框加在这里：先 DispObj 图像，再叠框 / 编号
        }
        catch (HalconException ex)
        {
            _log.Error(ex, "[图像] 显示失败");
            _viewModel.StatusText = "显示失败：" + ex.Message;
        }
    }

    #endregion

    #region 标题栏

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { DragMove(); }                       // 鼠标松开那一瞬间可能已无效，包一层更稳
        catch (InvalidOperationException) { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // WindowChrome 自绘标题栏时，最大化会往外溢 8 像素（系统的隐形边框），
    // 这里用"工作区尺寸"把最大尺寸压回来，免得盖住任务栏。
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Maximized)
        {
            MaxHeight = SystemParameters.WorkArea.Height + 8;
            MaxWidth = SystemParameters.WorkArea.Width + 8;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
        }
    }
    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;   // ★ 补：解绑，避免关窗后还挂着引用
        _viewModel.Dispose();                                       // 释放图像 + 停定时器
        base.OnClosed(e);
    }
}
