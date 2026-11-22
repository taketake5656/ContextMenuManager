using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextMenuManager.Methods
{
    /// <summary>
    /// パッケージアプリの右クリックメニュー項目（IExplorerCommand）から、
    /// 実際に右クリックメニューに表示される名前（GetTitle）を取得する。
    /// 他社の拡張を読み込むため、本体が落ちたり固まったりしないよう別プロセスで実行する。
    /// </summary>
    internal static class ExplorerCommandTitleProbe
    {
        /// <summary>子プロセスとして起動するときのコマンドライン引数</summary>
        public const string ProbeArg = "--cmm-probe-explorer-titles";

        private const int TimeoutMilliseconds = 15000;

        #region 親プロセス側

        /// <summary>
        /// 指定した CLSID の表示名を子プロセスで取得する。取得できなかったものは結果に含まれない。
        /// </summary>
        /// <param name="requests">CLSID と、GetTitle に渡す対象（ファイルやフォルダー）のパス</param>
        public static Dictionary<Guid, string> Query(IReadOnlyList<(Guid Guid, string ItemPath)> requests, string logPath = null)
        {
            var result = new Dictionary<Guid, string>();
            if (requests.Count == 0) return result;

            var workDir = Path.Combine(Path.GetTempPath(), "ContextMenuManager_probe");
            var inFile = Path.Combine(workDir, $"in_{Environment.ProcessId}.txt");
            var outFile = Path.Combine(workDir, $"out_{Environment.ProcessId}.txt");
            try
            {
                Directory.CreateDirectory(workDir);
                var sb = new StringBuilder();
                foreach (var (guid, itemPath) in requests) sb.Append(guid.ToString("B")).Append('\t').Append(itemPath ?? string.Empty).Append('\n');
                File.WriteAllText(inFile, sb.ToString(), Encoding.UTF8);
                if (File.Exists(outFile)) File.Delete(outFile);

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return result;
                var psi = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(ProbeArg);
                psi.ArgumentList.Add(inFile);
                psi.ArgumentList.Add(outFile);
                if (!string.IsNullOrEmpty(logPath))
                {
                    try { File.WriteAllText(logPath, $"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} 表示名の取得ログ\n", Encoding.UTF8); } catch { }
                    psi.ArgumentList.Add(logPath);
                }
                using (var process = Process.Start(psi))
                {
                    if (process != null && !process.WaitForExit(TimeoutMilliseconds))
                    {
                        try { process.Kill(true); } catch { }
                        AppendLog(logPath, "タイムアウトしたため取得処理を打ち切りました");
                    }
                    else if (process != null && process.ExitCode != 0)
                    {
                        AppendLog(logPath, $"取得処理が異常終了しました（終了コード 0x{process.ExitCode:X8}）");
                    }
                }

                // 子プロセスは 1 件ごとに書き出すので、途中で落ちても取得済みの分は使える
                if (File.Exists(outFile))
                {
                    foreach (var line in File.ReadAllLines(outFile, Encoding.UTF8))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length < 2) continue;
                        if (!Guid.TryParse(parts[0], out var guid)) continue;
                        var title = parts[1].Trim();
                        if (title.Length > 0) result[guid] = title;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try { File.Delete(inFile); } catch { }
                try { File.Delete(outFile); } catch { }
            }
            return result;
        }

        #endregion

        #region 子プロセス側

        /// <summary>子プロセスのエントリーポイント。Program.Main から呼ばれる。</summary>
        private static string logFile;

        private static void AppendLog(string path, string message)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { File.AppendAllText(path, message + "\n", Encoding.UTF8); } catch { }
        }

        private static void Log(string message) => AppendLog(logFile, message);

        public static void RunProbe(string inFile, string outFile, string logPath = null)
        {
            logFile = logPath;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(inFile, Encoding.UTF8);
            }
            catch
            {
                return;
            }
            _ = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length < 1 || !Guid.TryParse(parts[0], out var guid)) continue;
                var itemPath = parts.Length > 1 ? parts[1] : null;
                string title = null;
                Log($"{guid:B} 対象: {itemPath}");
                try
                {
                    title = GetTitle(guid, itemPath);
                }
                catch (Exception ex)
                {
                    Log($"  例外: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
                }
                Log($"  結果: {(string.IsNullOrWhiteSpace(title) ? "（取得できず）" : title)}");
                if (string.IsNullOrWhiteSpace(title)) continue;
                title = title.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                try
                {
                    File.AppendAllText(outFile, $"{guid:B}\t{title}\n", Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        private static string GetTitle(Guid clsid, string itemPath)
        {
            var iid = typeof(IExplorerCommand).GUID;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER, ref iid, out var obj);
            Log($"  CoCreateInstance: 0x{hr:X8}");
            if (hr < 0 || obj == null) return null;
            var command = (IExplorerCommand)obj;
            IntPtr itemArray = IntPtr.Zero;
            try
            {
                itemArray = CreateItemArray(itemPath);
                // まず選択項目を渡して取得し、だめなら選択項目なしで取得する
                Log($"  選択項目の作成: {(itemArray != IntPtr.Zero ? "成功" : "失敗")}");
                var title = CallGetTitle(command, itemArray);
                if (string.IsNullOrWhiteSpace(title) && itemArray != IntPtr.Zero) title = CallGetTitle(command, IntPtr.Zero);
                return title;
            }
            finally
            {
                if (itemArray != IntPtr.Zero) Marshal.Release(itemArray);
                try { Marshal.ReleaseComObject(obj); } catch { }
            }
        }

        private static string CallGetTitle(IExplorerCommand command, IntPtr itemArray)
        {
            var pName = IntPtr.Zero;
            try
            {
                var hr = command.GetTitle(itemArray, out pName);
                Log($"  GetTitle（選択項目{(itemArray != IntPtr.Zero ? "あり" : "なし")}）: 0x{hr:X8}");
                if (hr < 0 || pName == IntPtr.Zero) return null;
                var title = Marshal.PtrToStringUni(pName);
                // 「@{パッケージ?ms-resource://...}」のような間接文字列で返す実装もある
                if (!string.IsNullOrEmpty(title) && title.StartsWith("@"))
                {
                    var direct = ResourceString.GetDirectString(title);
                    if (!string.IsNullOrWhiteSpace(direct)) title = direct;
                }
                return title;
            }
            catch (Exception ex)
            {
                Log($"  GetTitle 例外: {ex.GetType().Name} 0x{ex.HResult:X8}");
                return null;
            }
            finally
            {
                if (pName != IntPtr.Zero) Marshal.FreeCoTaskMem(pName);
            }
        }

        private static IntPtr CreateItemArray(string path)
        {
            if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
            var iidShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            var iidShellItemArray = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out var item) < 0 || item == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                if (SHCreateShellItemArrayFromShellItem(item, ref iidShellItemArray, out var array) < 0) return IntPtr.Zero;
                return array;
            }
            finally
            {
                Marshal.Release(item);
            }
        }

        private const uint CLSCTX_INPROC_SERVER = 0x1;
        private const uint CLSCTX_LOCAL_SERVER = 0x4;
        private const uint COINIT_APARTMENTTHREADED = 0x2;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [DllImport("shell32.dll")]
        private static extern int SHCreateShellItemArrayFromShellItem(IntPtr psi, ref Guid riid, out IntPtr ppv);

        [ComImport]
        [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IExplorerCommand
        {
            [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
            [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
            [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
            [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
            [PreserveSig] int GetState(IntPtr psiItemArray, int fOkToBeSlow, out uint pCmdState);
            [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
            [PreserveSig] int GetFlags(out uint pFlags);
            [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
        }

        #endregion
    }
}
