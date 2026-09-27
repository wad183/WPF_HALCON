using System.IO;                      // ★ 改：WPF 工程的隐式 using 里没有 System.IO，要手写
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using HalconDotNet;

namespace HelcWPF
{
    public partial class MainViewModel : ObservableObject, IDisposable     
    {
        #region 字段
        private readonly ImageSourceService _source;
        private readonly ILogger _log = Log.ForContext<MainViewModel>();
        private readonly DispatcherTimer _autoTimer;                      
        #endregion

        #region 属性
        [ObservableProperty]
        private string _statusText = "请先选择数据源文件夹";                

        [ObservableProperty]
        private string _folderPath = "未选择文件夹";

        [ObservableProperty]
        private string _currentFileName = "-";

        [ObservableProperty]
        private string _progressText = "0/0";

        [ObservableProperty]
        private int _autoIntervalMs = 1000;

        [ObservableProperty]
        private bool _isAutoMode = false;

        [ObservableProperty]
        private string _imageInfoText = "---";

        [ObservableProperty]
        private HImage? _currentImage;
        #endregion

        public MainViewModel(ImageSourceService source)
        {
            _source = source;

            _autoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AutoIntervalMs)
            };
            _autoTimer.Tick += OnAutoTimerTick;
        }

        #region RelayCommand
        [RelayCommand]
        private void SelectFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择数据源文件夹",
                InitialDirectory = Directory.Exists(ImageSourceService.DefaultFolder)
                    ? ImageSourceService.DefaultFolder        
                    : AppContext.BaseDirectory
            };

            if (dialog.ShowDialog() != true) return;

            int count = LoadFolder(dialog.FolderName);
            StatusText = count > 0 ? $"已加载 {count} 张图片" : "这个文件夹里没有支持的图片";
        }

        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void Next() => Navigate(+1);

        [RelayCommand(CanExecute = nameof(CanNavigate))]
        private void Previous() => Navigate(-1);

        private bool CanNavigate() => _source.HasImages;
        #endregion

        #region 业务逻辑
        /// <summary>加载文件夹（public：以后命令行/自检也能直接用）</summary>
        public int LoadFolder(string folder)
        {
            bool wasAuto = IsAutoMode;
            StopAuto(updateStatus: false);                 // 换数据源前先停表（但不要弹提示）

            int count = _source.LoadFolder(folder);
            FolderPath = count > 0 ? folder : $"{folder}（没有图片）";

            if (count > 0)
            {
                ShowCurrent();
            }
            else
            {
                CurrentImage?.Dispose();
                CurrentImage = null;

                CurrentFileName = "-";
                ProgressText = "0/0";
            }

            RefreshNavigation();

            if (wasAuto && count > 0) StartAuto();         // 如果本来就是自动模式，换完继续播
            return count;
        }

        /// <summary>上一页 / 下一页：真正干活的地方</summary>
        private void Navigate(int step)
        {
            if (IsAutoMode) IsAutoMode = false;

            bool moved = step > 0 ? _source.MoveNext() : _source.MovePrevious();

            if (!moved)
            {
                string bound = step > 0 ? "最后一张" : "第一张";
                StatusText = $"已经是{bound}了";
                _log.Information("[切换] 已到边界（{Bound}），当前 {Index}/{Total}",
                    bound, _source.CurrentIndex + 1, _source.Count);
                return;
            }

            ShowCurrent();
        }

        /// <summary>
        /// ★ 状态更新的唯一出口：手动翻页、自动播放、换文件夹最后都走这里
        /// 步骤 2 加图像显示时，只改这个方法
        /// </summary>
        private void ShowCurrent()
        {
            var item = _source.Current;
            if (item is null) return;

            CurrentFileName = item.FileName;
            ProgressText = $"{_source.CurrentIndex + 1} / {_source.Count}";
            LoadImage(item);
            RefreshNavigation();

            _log.Information("[切换] {Mode} 第 {Index}/{Total} 张:{File}",
                IsAutoMode ? "自动" : "手动", item.Index, _source.Count, item.FileName);
        }

        private void LoadImage(ImageItem item)
        {
            CurrentImage?.Dispose();
            CurrentImage = null;

            try
            {
                var image = new HImage(item.FilePath);       // = HDevelop 的 read_image

                HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);
                HOperatorSet.CountChannels(image, out HTuple channels);

                CurrentImage = image;                        // ★ 赋值 → 触发 View 去显示
                ImageInfoText = $"{width.I}×{height.I}  {channels.I} 通道";
                _log.Information("[图像] 读入 {File}：{W}×{H}，{C} 通道",
                    item.FileName, width.I, height.I, channels.I);
            }
            catch (HalconException ex)                       // 损坏图片、没权限…都走这
            {
                ImageInfoText = "-";
                StatusText = "读图失败：" + ex.Message;
                _log.Error(ex, "[图像] 读图失败：{Path}", item.FilePath);
            }
        }

        private void RefreshNavigation()
        {
            NextCommand.NotifyCanExecuteChanged();
            PreviousCommand.NotifyCanExecuteChanged();
        }

        private void OnAutoTimerTick(object? sender, EventArgs e)
        {
            if (!_source.MoveNext())
            {
                IsAutoMode = false;                       
                StatusText = "已到最后一张，自动切换结束";
                _log.Information("[播放] 已到最后一张，自动切换结束");
                return;
            }
            ShowCurrent();
        }

        /// <summary>开始自动切换</summary>
        private void StartAuto()
        {
            int interval = Math.Max(50, AutoIntervalMs);   // 防止手滑填 0
            _autoTimer.Interval = TimeSpan.FromMilliseconds(interval);
            _autoTimer.Start();
            StatusText = $"自动切换中（间隔 {interval} ms）";
            _log.Information("[播放] 开始自动切换，间隔 {Interval} ms", interval);
        }

        /// <summary>停止自动切换</summary>
        private void StopAuto(bool updateStatus = true)
        {
            if (_autoTimer.IsEnabled)
            {
                _autoTimer.Stop();
                _log.Information("[播放] 停止自动切换");
            }
            if (updateStatus) StatusText = "手动模式：用「上一页 / 下一页」翻页";
        }

        #region 属性变化钩子

        partial void OnIsAutoModeChanged(bool value)
        {
            if (value)
            {
                if (!_source.HasImages)                    // 还没选文件夹就勾了自动
                {
                    StatusText = "请先选择数据源文件夹";
                    _log.Warning("[播放] 未加载图片，无法开始自动切换");
                    IsAutoMode = false;                    // 把开关弹回去（会再进一次本方法，走 else 分支）
                    return;
                }
                StartAuto();
            }
            else
            {
                StopAuto();
            }
        }

        partial void OnAutoIntervalMsChanged(int value)
        {
            if (value > 0 && _autoTimer is not null)
            {
                _autoTimer.Interval = TimeSpan.FromMilliseconds(value);   
                _log.Information("[播放] 切换间隔改为 {Interval} ms", value);
            }
        }
        #endregion

        public void Dispose()
        {
            _autoTimer.Stop();
            _autoTimer.Tick -= OnAutoTimerTick;
            CurrentImage?.Dispose();
            CurrentImage = null;
        }
        #endregion
    }
}
