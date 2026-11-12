using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace IslandIntegrated
{
    public sealed class DesktopSwitchLauncher
    {
        private static readonly string[] Actions = { "local", "macbook", "ipad-mac", "overview" };
        private static readonly string[] Labels = { "本机", "MacBook Pro", "iPad Mac", "总览" };
        private readonly string configurationPath;
        private bool accessDenied;

        private sealed class Configuration
        {
            public string schema { get; set; }
            public bool enabled { get; set; }
            public string executable { get; set; }
        }

        public DesktopSwitchLauncher(string configurationPath)
        {
            this.configurationPath = configurationPath;
        }

        public ToolStripMenuItem CreateMenu()
        {
            var menu = new ToolStripMenuItem("切换桌面");
            for (int index = 0; index < Actions.Length; index++)
            {
                string action = Actions[index];
                var item = new ToolStripMenuItem(Labels[index]);
                item.Click += (sender, args) => Start(action);
                menu.DropDownItems.Add(item);
            }
            menu.DropDownOpening += (sender, args) =>
            {
                string executable, reason;
                bool ready = TryReadExecutable(out executable, out reason);
                menu.ToolTipText = ready ? "切换本机或远程桌面" : reason;
                foreach (ToolStripItem item in menu.DropDownItems)
                {
                    item.Enabled = ready;
                    item.ToolTipText = ready ? String.Empty : reason;
                }
            };
            return menu;
        }

        private bool TryReadExecutable(out string executable, out string reason)
        {
            executable = null;
            reason = "桌面切换器尚未配置";
            if (accessDenied)
            {
                reason = "桌面切换器访问被拒绝，请检查权限后重新启动侧栏";
                return false;
            }
            try
            {
                var file = new FileInfo(configurationPath);
                if (file.Length > 16384) throw new InvalidDataException("桌面切换配置过大");
                string json = File.ReadAllText(configurationPath, new UTF8Encoding(false, true));
                var serializer = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 8 };
                var configuration = serializer.Deserialize<Configuration>(json);
                if (configuration == null || configuration.schema != "sidebar.desktop-switch-connection.v1")
                    throw new InvalidDataException("桌面切换配置格式无效");
                if (!configuration.enabled) return false;
                var start = CreateStartInfo(configuration.executable, "local");
                reason = "找不到桌面切换器，请检查配置中的程序路径";
                if ((File.GetAttributes(start.FileName) & FileAttributes.Directory) != 0)
                {
                    return false;
                }
                executable = start.FileName;
                reason = String.Empty;
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                accessDenied = true;
                reason = "桌面切换配置访问被拒绝";
            }
            catch (System.Security.SecurityException)
            {
                accessDenied = true;
                reason = "桌面切换配置访问被拒绝";
            }
            catch (Exception error)
            {
                reason = "桌面切换不可用，" + error.Message;
            }
            return false;
        }

        private void Start(string action)
        {
            string executable, reason;
            if (!TryReadExecutable(out executable, out reason))
            {
                MessageBox.Show(reason, "切换桌面", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                using (var process = Process.Start(CreateStartInfo(executable, action)))
                {
                    if (process == null) throw new InvalidOperationException("桌面切换器未能启动");
                }
            }
            catch (Exception error)
            {
                var nativeError = error as Win32Exception;
                if (error is UnauthorizedAccessException || error is System.Security.SecurityException ||
                    (nativeError != null && nativeError.NativeErrorCode == 5)) accessDenied = true;
                MessageBox.Show("无法打开桌面切换器，" + error.Message, "切换桌面", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static ProcessStartInfo CreateStartInfo(string executable, string action)
        {
            if (Array.IndexOf(Actions, action) < 0) throw new ArgumentOutOfRangeException("action");
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) ||
                !String.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("桌面切换器需要完整的 EXE 程序路径", "executable");
            string program = Path.GetFullPath(executable);
            return new ProcessStartInfo
            {
                FileName = program,
                Arguments = "--action " + action,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(program)
            };
        }
    }
}
