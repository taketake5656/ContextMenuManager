using ContextMenuManager.Controls;
using ContextMenuManager.Methods;
using System;
using System.IO;

namespace ContextMenuManager
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Check for logon restore command-line argument (silent background restore)
            var args = Environment.GetCommandLineArgs();

            // パッケージアプリの右クリックメニュー項目の表示名を取得する子プロセス（UI なし）
            if (args.Length > 3 && args[1].Equals(ExplorerCommandTitleProbe.ProbeArg, StringComparison.Ordinal))
            {
                ExplorerCommandTitleProbe.RunProbe(args[2], args[3]);
                return;
            }
            if (args.Length > 1 && args[1].Equals(LogonTaskHelper.LogonRestoreArg, StringComparison.OrdinalIgnoreCase))
            {
                PerformLogonRestore();
                return;
            }

            // 启动WPF应用
            if (SingleInstance<App>.InitializeAsFirstInstance())
            {
                // 埋め込みの最新の言語ファイルと辞書を設定フォルダーに反映
                AppConfig.SyncBundledFiles();
                // 初始化字符串、更新检查和XML字典
                AppString.LoadStrings();
                XmlDicHelper.ReloadDics();

                using var application = new App();
                application.InitializeComponent();
                application.Run();
            }
        }

        /// <summary>Performs a silent backup restore for the logon scheduled task (no UI).</summary>
        private static void PerformLogonRestore()
        {
            try
            {
                AppConfig.SyncBundledFiles();
                AppString.LoadStrings();

                var filePath = AppConfig.LogonRestoreFilePath;
                var scenesStr = AppConfig.LogonRestoreScenes;
                var restoreMode = (RestoreMode)AppConfig.LogonRestoreMode;

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    throw new FileNotFoundException("Logon restore file not found.", filePath);

                var sceneTexts = LogonTaskHelper.ParseSceneTexts(scenesStr);
                if (sceneTexts.Count == 0)
                    throw new InvalidDataException("No valid scenes specified for logon restore.");

                BackupList.LoadBackupDataMetaData(filePath);
                if (BackupList.metaData == null
                    || BackupList.metaData.Version <= BackupHelper.DeprecatedBackupVersion)
                    throw new InvalidDataException("Backup file is of an unsupported version.");

                var helper = new BackupHelper();
                var silentReporter = new LoadingDialogInterface();
                helper.RestoreItems(filePath, sceneTexts, restoreMode, silentReporter);

                var restoreList = helper.restoreList ?? throw new InvalidDataException("Failed to parse restore items from backup file.");

                // If any items need to restart Explorer, restart Explorer so the changes take effect.
                if (ExplorerRestarter.IsPendingRestart)
                    ExternalProgram.RestartExplorer();
            }
            catch
            {
                // Silent – do not surface errors during a logon task
            }
        }
    }
}
