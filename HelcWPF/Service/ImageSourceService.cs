using System.IO;
using Serilog;

namespace HelcWPF
{
    /// <summary>
    /// 提供图片数据相关的方法：扫描文件夹 + 自然排序 + 翻页游标
    /// ⚠️ 这个类里不许出现 WPF 类型（不能弹对话框、不能用 Dispatcher）
    /// </summary>
    public class ImageSourceService
    {
        /// <summary>默认文件夹路径</summary>
        public const string DefaultFolder = @"D:\PictureForMvt\508Pic";

        // ★ 改：原来叫 SupportedExtension（少个 s），但下面用的是 SupportedExtensions → 编译不过
        public static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        private readonly ILogger _log = Log.ForContext<ImageSourceService>();
        private readonly List<ImageItem> _images = new List<ImageItem>();
        public IReadOnlyList<ImageItem> Images => _images;

        public string FolderPath { get; private set; } = string.Empty;
        public int CurrentIndex { get; private set; } = -1;

        public int Count => _images.Count;

        /// <summary>有没有图片（给界面判断"上/下一页按钮能不能点"用）</summary>
        public bool HasImages => _images.Count > 0;

        public ImageItem? Current => CurrentIndex >= 0 && CurrentIndex < _images.Count ? _images[CurrentIndex] : null;

        public bool CanMoveNext => CurrentIndex >= 0 && CurrentIndex < _images.Count - 1;
        public bool CanMovePrevious => CurrentIndex > 0;

        public int LoadFolder(string floder)
        {
            _images.Clear();
            CurrentIndex = -1;
            FolderPath = floder;

            if (!Directory.Exists(floder))
            {
                _log.Warning("[数据源] 文件夹不存在：{Folder}", floder);
                return 0;
            }

            var allFiles = Directory.EnumerateFiles(floder).ToList();          // ★ 新增：为了统计跳过了几个

            var files = allFiles
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => (File.GetAttributes(f) & FileAttributes.Hidden) == 0)
                .ToList();

            files.Sort(NaturalStringComparer.Instance);                        // 自然排序 1,2,3…10…100

            for (int i = 0; i < files.Count; i++)
            {
                _images.Add(new ImageItem(files[i], i + 1));
            }

            if (_images.Count > 0)
            {
                CurrentIndex = 0;
            }
            _log.Information("[数据源] {Folder} → {Count} 张图片（目录共 {AllFiles} 个文件，跳过 {Skipped} 个非图片/隐藏文件）| 首张 {First} | 前 10 张：{Names}",
                floder, _images.Count, allFiles.Count, allFiles.Count - files.Count,
                Current?.FileName ?? "(无)",
                string.Join(", ", _images.Take(10).Select(x => x.FileName)));

            return _images.Count;
        }

        public bool MoveNext()
        {
            if (!CanMoveNext) return false;
            CurrentIndex++;
            return true;
        }

        public bool MovePrevious()
        {
            if (!CanMovePrevious) return false;
            CurrentIndex--;
            return true;
        }
    }

    internal sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new();   // 无状态，单例即可

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    // 两边都走到数字段：整段取出来，按数值比
                    int startI = i, startJ = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;

                    string numX = x.Substring(startI, i - startI).TrimStart('0');   // "007" == "7"
                    string numY = y.Substring(startJ, j - startJ).TrimStart('0');

                    if (numX.Length != numY.Length) return numX.Length - numY.Length;  // 位数多的数值大
                    int cmp = string.CompareOrdinal(numX, numY);
                    if (cmp != 0) return cmp;
                    // 数值相等 → 继续往后比
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (cmp != 0) return cmp;
                    i++; j++;
                }
            }
            return (x.Length - i) - (y.Length - j);      // 前缀相同：短的在前
        }
    }
}
