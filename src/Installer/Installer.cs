using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;

namespace MacAddressGeneratorInstaller
{
    internal static class InstallerProgram
    {
        private const string DefaultInstallDirectory = @"C:\MacAddressGenerator";
        private const string AppFileName = "MacAddressGenerator.exe";
        private const string PayloadResourceName = "Payload.MacAddressGenerator.exe";
        private const string ShortcutFileName = "MAC 地址生成器.lnk";
        private const string AppProcessName = "MacAddressGenerator";

        [STAThread]
        private static void Main()
        {
            try
            {
                string installDirectory = ResolveInstallDirectory();
                bool testMode = installDirectory != DefaultInstallDirectory;

                Install(installDirectory, !testMode);
                TryStartApplication(installDirectory);
                ScheduleSelfDeletion();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "安装没有完成。\r\n\r\n" + exception.Message +
                    "\r\n\r\n请关闭正在运行的 MAC 地址生成器，然后重新双击安装程序。",
                    "MAC 地址生成器 · 安装程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string ResolveInstallDirectory()
        {
            string testOverride = Environment.GetEnvironmentVariable("MACGEN_TEST_DIR");
            if (!string.IsNullOrEmpty(testOverride))
            {
                return Path.GetFullPath(testOverride);
            }

            return DefaultInstallDirectory;
        }

        private static void Install(string installDirectory, bool createShortcut)
        {
            Directory.CreateDirectory(installDirectory);
            GrantCurrentUserModify(installDirectory);

            string targetExecutable = Path.Combine(installDirectory, AppFileName);
            StopInstalledApplication(targetExecutable);
            ExtractPayload(targetExecutable);

            if (createShortcut)
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                {
                    desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                }

                if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
                {
                    throw new DirectoryNotFoundException("找不到桌面文件夹，无法创建快捷方式。");
                }

                string shortcutPath = Path.Combine(desktop, ShortcutFileName);
                CreateShortcut(shortcutPath, targetExecutable, installDirectory);
            }
        }

        private static void GrantCurrentUserModify(string directory)
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity.User == null)
                {
                    return;
                }

                DirectorySecurity security = Directory.GetAccessControl(directory);
                FileSystemAccessRule rule = new FileSystemAccessRule(
                    identity.User,
                    FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);
                security.AddAccessRule(rule);
                Directory.SetAccessControl(directory, security);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "无法为安装目录设置写入权限，配置文件将不能保存。", exception);
            }
        }

        private static void ExtractPayload(string targetExecutable)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream payload = assembly.GetManifestResourceStream(PayloadResourceName))
            {
                if (payload == null)
                {
                    throw new InvalidOperationException("安装包中缺少主程序文件。");
                }

                string temporaryFile = targetExecutable + ".new";
                try
                {
                    using (FileStream output = new FileStream(
                        temporaryFile,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        payload.CopyTo(output);
                    }

                    if (File.Exists(targetExecutable))
                    {
                        File.Delete(targetExecutable);
                    }

                    File.Move(temporaryFile, targetExecutable);
                }
                finally
                {
                    if (File.Exists(temporaryFile))
                    {
                        try
                        {
                            File.Delete(temporaryFile);
                        }
                        catch
                        {
                            // Leave the temporary file in place if another process has it open.
                        }
                    }
                }
            }
        }

        private static void StopInstalledApplication(string targetExecutable)
        {
            foreach (Process process in Process.GetProcessesByName(AppProcessName))
            {
                try
                {
                    string processPath = process.MainModule.FileName;
                    if (!string.Equals(
                        Path.GetFullPath(processPath),
                        Path.GetFullPath(targetExecutable),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.CloseMainWindow();
                    if (!process.WaitForExit(2500))
                    {
                        process.Kill();
                        process.WaitForExit(2500);
                    }
                }
                catch
                {
                    // If the process cannot be inspected, the write below gives a clear error.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("系统无法创建快捷方式（Windows Script Host 不可用）。");
            }

            object shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic shellObject = shell;
                dynamic shortcut = shellObject.CreateShortcut(shortcutPath);
                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = workingDirectory;
                shortcut.IconLocation = targetPath + ",0";
                shortcut.Description = "一键生成随机 MAC 地址";
                shortcut.WindowStyle = 1;
                shortcut.Save();
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
        }

        private static void TryStartApplication(string installDirectory)
        {
            try
            {
                string executable = Path.Combine(installDirectory, AppFileName);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.WorkingDirectory = installDirectory;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "安装已完成，但无法自动打开程序。\r\n请到安装目录中双击 " + AppFileName +
                    " 启动。\r\n\r\n" + exception.Message,
                    "MAC 地址生成器 · 安装程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static void ScheduleSelfDeletion()
        {
            string executablePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments =
                    "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + executablePath + "\"";
                startInfo.CreateNoWindow = true;
                startInfo.UseShellExecute = false;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);
            }
            catch
            {
                // Self-deletion is best effort; a locked installer is harmless.
            }
        }
    }
}
