using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HelcWPF
{
    /// <summary>
    /// 数据源文件夹里的单个图片数据
    /// </summary>
    public class ImageItem
    {
        public ImageItem(string filePath,int index)
        {
            FilePath = filePath;
            Index = index;
        }
        public string FilePath { get;}

        public int Index { get; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(FilePath);
    }
}
