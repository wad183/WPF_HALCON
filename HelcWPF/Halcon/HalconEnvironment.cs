// ============================================================================
//  Halcon/HalconEnvironment.cs —— 让 HALCON 能跑起来
//
//  调用任何 HALCON 算子之前必须先执行 Init()。它解决三个"必踩"的问题：
//
//   1) C# 引用的 halcondotnet.dll 只是"托管壳"，真正算东西的是 native 的
//      halcon.dll（在 <HALCONROOT>\bin\x64-win64 里）。Windows 按 PATH 找 dll，
//      所以要把它插进当前进程的 PATH。
//
//   2) native halcon.dll 按环境变量 HALCONROOT 去找 <HALCONROOT>\license\license_*.dat，
//      找不到 license 就直接报错。
//
//   3) Environment.SetEnvironmentVariable 改的是"当前进程"的环境块，native 代码
//      立刻能读到 —— 所以不改系统环境变量也能跑。
//
//  （这段代码在控制台版 halc 里已经实测跑通过，这里直接用同一套逻辑。）
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HelcWPF
{
    public static class HalconEnvironment
    {
        /// <summary>HALCON 安装根目录，例如 D:\...\HALCON-24.11-Progress-Steady</summary>
        public static string Root { get; private set; } = "";

        /// <summary>native dll 所在目录：<see cref="Root"/>\bin\x64-win64</summary>
        public static string NativeDir { get; private set; } = "";

        /// <summary>HALCON 版本号，例如 "24.11"（由 Init 里探测得到）</summary>
        public static string Version { get; private set; } = "";

        public static void Init()
        {
            Root = FindHalconRoot();
            NativeDir = Path.Combine(Root, "bin", "x64-win64");

            if (!File.Exists(Path.Combine(NativeDir, "halcon.dll")))
                throw new DirectoryNotFoundException($"这个目录里没有 halcon.dll：{NativeDir}");

            // ① license 靠 HALCONROOT 定位
            Environment.SetEnvironmentVariable("HALCONROOT", Root);
            Environment.SetEnvironmentVariable("HALCONARCH", "x64-win64");
            // ② halcon.dll 靠 PATH 定位（插到最前面）
            Environment.SetEnvironmentVariable("PATH",
                NativeDir + ";" + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty));

            // ③ 探活：能取到版本号，说明 native 库和 license 都正常了
            HalconDotNet.HOperatorSet.GetSystem("version", out HalconDotNet.HTuple version);
            Version = version.S;
        }

        /// <summary>已加载的 license 文件个数，用于自检提示</summary>
        public static int LicenseFileCount()
        {
            string dir = Path.Combine(Root, "license");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "license*.dat").Length : 0;
        }

        // 优先用系统环境变量 HALCONROOT；没有就用 csproj 里 <HalconRoot> 编译进来的值
        private static string FindHalconRoot()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("HALCONROOT");
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
                return fromEnv.TrimEnd('\\');

            string? fromCsproj = Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "HalconRoot")?.Value;

            if (!string.IsNullOrWhiteSpace(fromCsproj) && Directory.Exists(fromCsproj))
                return fromCsproj.TrimEnd('\\');

            throw new DirectoryNotFoundException(
                $"找不到 HALCON 安装目录：系统变量 HALCONROOT=[{fromEnv}]，csproj 里 HalconRoot=[{fromCsproj}]");
        }
    }
}
