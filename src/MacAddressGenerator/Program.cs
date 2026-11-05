using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Thread = System.Threading.Thread;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MacAddressGenerator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                if (Environment.GetEnvironmentVariable("MACGEN_DEBUG") == "1")
                {
                    File.WriteAllText(
                        Path.Combine(AppStorage.DataDirectory, "startup-debug.log"),
                        "Main started " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                }

                if (!IsAdministrator())
                {
                    MessageBox.Show(
                        "修改网卡地址需要管理员权限。\r\n\r\n请右键点击“MAC 地址生成器”，选择“以管理员身份运行”。",
                        "MAC 地址生成器",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception exception)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(AppStorage.DataDirectory, "startup-error.log"),
                        exception.ToString());
                }
                catch
                {
                }

                MessageBox.Show(
                    "程序启动失败：\r\n\r\n" + exception.Message,
                    "MAC 地址生成器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember(Name = "current")]
        public string Current { get; set; }

        [DataMember(Name = "adapterId")]
        public string AdapterId { get; set; }
    }

    [DataContract]
    public sealed class MacHistoryEntry
    {
        [DataMember(Name = "time")]
        public string Time { get; set; }

        [DataMember(Name = "mac")]
        public string Mac { get; set; }
    }

    [DataContract]
    public sealed class MacHistoryFile
    {
        [DataMember(Name = "entries")]
        public List<MacHistoryEntry> Entries { get; set; }
    }

    internal static class AppStorage
    {
        private const string ConfigFileName = "config.json";
        private const string HistoryFileName = "history.json";

        private static readonly object SyncRoot = new object();

        public static string DataDirectory
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataDirectory, ConfigFileName); }
        }

        public static string HistoryPath
        {
            get { return Path.Combine(DataDirectory, HistoryFileName); }
        }

        public static AppSettings LoadSettings()
        {
            lock (SyncRoot)
            {
                if (!File.Exists(ConfigPath))
                {
                    return new AppSettings();
                }

                try
                {
                    using (FileStream stream = File.OpenRead(ConfigPath))
                    {
                        DataContractJsonSerializer serializer =
                            new DataContractJsonSerializer(typeof(AppSettings));
                        AppSettings settings = serializer.ReadObject(stream) as AppSettings;
                        return settings ?? new AppSettings();
                    }
                }
                catch
                {
                    BackupBrokenFile(ConfigPath);
                    return new AppSettings();
                }
            }
        }

        public static List<MacHistoryEntry> LoadHistory()
        {
            lock (SyncRoot)
            {
                if (!File.Exists(HistoryPath))
                {
                    return new List<MacHistoryEntry>();
                }

                try
                {
                    using (FileStream stream = File.OpenRead(HistoryPath))
                    {
                        DataContractJsonSerializer serializer =
                            new DataContractJsonSerializer(typeof(MacHistoryFile));
                        MacHistoryFile file = serializer.ReadObject(stream) as MacHistoryFile;
                        if (file == null || file.Entries == null)
                        {
                            return new List<MacHistoryEntry>();
                        }

                        List<MacHistoryEntry> entries = new List<MacHistoryEntry>();
                        foreach (MacHistoryEntry entry in file.Entries)
                        {
                            if (entry != null && !string.IsNullOrEmpty(entry.Mac))
                            {
                                entries.Add(entry);
                            }
                        }

                        return entries;
                    }
                }
                catch
                {
                    BackupBrokenFile(HistoryPath);
                    return new List<MacHistoryEntry>();
                }
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            lock (SyncRoot)
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(AppSettings));
                WriteSerialized(ConfigPath, settings, serializer);
            }
        }

        public static void SaveHistory(List<MacHistoryEntry> entries)
        {
            lock (SyncRoot)
            {
                MacHistoryFile file = new MacHistoryFile();
                file.Entries = entries;
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(MacHistoryFile));
                WriteSerialized(HistoryPath, file, serializer);
            }
        }

        private static void WriteSerialized(string path, object value, DataContractJsonSerializer serializer)
        {
            byte[] data;
            using (MemoryStream memory = new MemoryStream())
            {
                serializer.WriteObject(memory, value);
                data = memory.ToArray();
            }

            string temporaryPath = path + ".tmp";
            File.WriteAllBytes(temporaryPath, data);

            try
            {
                if (File.Exists(path))
                {
                    string backupPath = path + ".bak";
                    File.Replace(temporaryPath, path, backupPath, true);
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            catch
            {
                File.Copy(temporaryPath, path, true);
                File.Delete(temporaryPath);
            }
        }

        private static void BackupBrokenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return;
                }

                string backupPath = path + ".broken-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Move(path, backupPath);
            }
            catch
            {
                // A corrupt data file should never stop the generator from opening.
            }
        }
    }

    internal static class MacAddressFactory
    {
        public static string CreateRandom(string avoid)
        {
            byte[] bytes = new byte[6];
            using (RNGCryptoServiceProvider random = new RNGCryptoServiceProvider())
            {
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    random.GetBytes(bytes);

                    // Unicast, locally administered: 02:xx:xx:xx:xx:xx style.
                    bytes[0] = (byte)((bytes[0] & 0xFC) | 0x02);
                    string candidate = Format(bytes);
                    if (!string.Equals(candidate, avoid, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            bytes[5] ^= 0x01;
            return Format(bytes);
        }

        public static bool LooksValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 17)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (index % 3 == 2)
                {
                    if (character != ':')
                    {
                        return false;
                    }
                }
                else if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Format(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(17);
            for (int index = 0; index < bytes.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(':');
                }

                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    internal sealed class NetworkAdapterInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CurrentMac { get; set; }
        public bool IsUp { get; set; }
        public NetworkInterfaceType InterfaceType { get; set; }

        public string TypeLabel
        {
            get
            {
                if (InterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    return "无线";
                }

                return "以太网";
            }
        }

        public string DisplayName
        {
            get
            {
                string state = IsUp ? "● " : "○ ";
                string suffix = string.IsNullOrEmpty(Description) ? string.Empty : " · " + Description;
                return state + Name + "（" + TypeLabel + "）" + suffix;
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal static class NetworkAdapterService
    {
        private const string NetworkClassKeyPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

        private static readonly string[] VirtualAdapterHints = new string[]
        {
            "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "tap-windows",
            "tap adapter", "vpn", "miniport", "loopback", "npcap", "bluetooth",
            "wi-fi direct", "microsoft wi-fi", "tailscale", "zerotier", "wireguard",
            "openvpn", "radmin"
        };

        public static List<NetworkAdapterInfo> GetAdapters()
        {
            List<NetworkAdapterInfo> adapters = new List<NetworkAdapterInfo>();
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsSupportedType(networkInterface.NetworkInterfaceType))
                {
                    continue;
                }

                string description = networkInterface.Description ?? string.Empty;
                string name = networkInterface.Name ?? string.Empty;
                if (IsVirtualAdapter(name, description))
                {
                    continue;
                }

                string currentMac = FormatPhysicalAddress(networkInterface.GetPhysicalAddress());
                if (!MacAddressFactory.LooksValid(currentMac))
                {
                    continue;
                }

                NetworkAdapterInfo adapter = new NetworkAdapterInfo();
                adapter.Id = networkInterface.Id;
                adapter.Name = name;
                adapter.Description = description;
                adapter.CurrentMac = currentMac;
                adapter.IsUp = networkInterface.OperationalStatus == OperationalStatus.Up;
                adapter.InterfaceType = networkInterface.NetworkInterfaceType;
                adapters.Add(adapter);
            }

            adapters.Sort(delegate(NetworkAdapterInfo left, NetworkAdapterInfo right)
            {
                if (left.IsUp != right.IsUp)
                {
                    return left.IsUp ? -1 : 1;
                }

                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            return adapters;
        }

        public static bool TryApplyMac(NetworkAdapterInfo adapter, string mac, out string error)
        {
            error = null;
            if (adapter == null)
            {
                error = "没有选择目标网卡。";
                return false;
            }

            string normalizedMac = (mac ?? string.Empty).Replace(":", string.Empty).Replace("-", string.Empty);
            if (normalizedMac.Length != 12 || !IsHex(normalizedMac))
            {
                error = "MAC 地址格式不正确。";
                return false;
            }

            string registryKeyPath = FindAdapterRegistryPath(adapter.Id);
            if (registryKeyPath == null)
            {
                error = "没有找到该网卡的驱动配置项。请换一张网卡再试。";
                return false;
            }

            object previousValue = null;
            bool hadPreviousValue = false;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKeyPath, true))
                {
                    if (key == null)
                    {
                        error = "无法打开网卡驱动配置项，请以管理员身份运行。";
                        return false;
                    }

                    previousValue = key.GetValue(
                        "NetworkAddress",
                        null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    hadPreviousValue = previousValue != null;
                    key.SetValue("NetworkAddress", normalizedMac.ToUpperInvariant(), RegistryValueKind.String);
                }
            }
            catch (Exception exception)
            {
                error = "写入网卡配置失败：" + exception.Message;
                return false;
            }

            string restartError;
            if (!RestartAdapter(adapter, out restartError))
            {
                TryRestorePreviousValue(registryKeyPath, previousValue, hadPreviousValue);
                error = "地址已写入，但重启网卡失败：" + restartError;
                return false;
            }

            return true;
        }

        private static bool IsSupportedType(NetworkInterfaceType type)
        {
            return type == NetworkInterfaceType.Ethernet ||
                type == NetworkInterfaceType.GigabitEthernet ||
                type == NetworkInterfaceType.FastEthernetT ||
                type == NetworkInterfaceType.FastEthernetFx ||
                type == NetworkInterfaceType.Wireless80211;
        }

        private static bool IsVirtualAdapter(string name, string description)
        {
            string combined = (name + " " + description).ToLowerInvariant();
            foreach (string hint in VirtualAdapterHints)
            {
                if (combined.IndexOf(hint, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatPhysicalAddress(PhysicalAddress address)
        {
            if (address == null)
            {
                return null;
            }

            byte[] bytes = address.GetAddressBytes();
            if (bytes == null || bytes.Length != 6)
            {
                return null;
            }

            StringBuilder builder = new StringBuilder(17);
            for (int index = 0; index < bytes.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(':');
                }

                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static bool IsHex(string value)
        {
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FindAdapterRegistryPath(string adapterId)
        {
            string normalizedAdapterId = NormalizeGuid(adapterId);
            using (RegistryKey classKey = Registry.LocalMachine.OpenSubKey(NetworkClassKeyPath, false))
            {
                if (classKey == null)
                {
                    return null;
                }

                foreach (string subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.Length != 4 || !IsDigits(subKeyName))
                    {
                        continue;
                    }

                    using (RegistryKey adapterKey = classKey.OpenSubKey(subKeyName, false))
                    {
                        if (adapterKey == null)
                        {
                            continue;
                        }

                        string instanceId = adapterKey.GetValue("NetCfgInstanceId") as string;
                        if (string.Equals(
                            NormalizeGuid(instanceId),
                            normalizedAdapterId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            return NetworkClassKeyPath + "\\" + subKeyName;
                        }
                    }
                }
            }

            return null;
        }

        private static string NormalizeGuid(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Trim().Trim('{', '}').ToUpperInvariant();
        }

        private static bool IsDigits(string value)
        {
            foreach (char character in value)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static void TryRestorePreviousValue(
            string registryKeyPath,
            object previousValue,
            bool hadPreviousValue)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(registryKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (hadPreviousValue)
                    {
                        key.SetValue("NetworkAddress", previousValue, RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue("NetworkAddress", false);
                    }
                }
            }
            catch
            {
                // The UI reports the main failure; rollback is a best effort.
            }
        }

        private static bool RestartAdapter(NetworkAdapterInfo adapter, out string error)
        {
            error = null;
            if (adapter.Name.IndexOf('"') >= 0)
            {
                error = "网卡名称包含不支持的字符。";
                return false;
            }

            string disableError;
            if (!SetAdapterEnabled(adapter.Name, false, out disableError))
            {
                error = "禁用网卡失败（" + disableError + "）。";
                return false;
            }

            Thread.Sleep(1800);

            string enableError;
            if (!SetAdapterEnabled(adapter.Name, true, out enableError))
            {
                // Make one more attempt so the network is less likely to stay disabled.
                Thread.Sleep(1000);
                string secondError;
                SetAdapterEnabled(adapter.Name, true, out secondError);
                error = "网卡已禁用，但重新启用失败（" + enableError +
                    "）。请到“网络连接”中手动启用该网卡。";
                return false;
            }

            Thread.Sleep(1600);
            return true;
        }

        private static bool SetAdapterEnabled(string adapterName, bool enabled, out string error)
        {
            error = null;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "netsh.exe";
                startInfo.Arguments = "interface set interface name=\"" + adapterName + "\" admin=" +
                    (enabled ? "enabled" : "disabled");
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "无法启动系统网络命令。";
                        return false;
                    }

                    if (!process.WaitForExit(20000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        error = "系统网络命令超时。";
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        error = "系统网络命令返回错误码 " + process.ExitCode + "。";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public int CornerRadius { get; set; }
        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color OutlineColor { get; set; }
        public int OutlineWidth { get; set; }

        public RoundedButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            CornerRadius = 12;
            NormalColor = Color.White;
            HoverColor = Color.FromArgb(245, 249, 247);
            PressedColor = Color.FromArgb(235, 242, 239);
            OutlineColor = Color.Transparent;
            OutlineWidth = 0;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }

            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics graphics = pevent.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Parent == null ? Color.White : Parent.BackColor);

            Color fill = NormalColor;
            if (!Enabled)
            {
                fill = Color.FromArgb(210, 218, 215);
            }
            else if (_pressed)
            {
                fill = PressedColor;
            }
            else if (_hovered)
            {
                fill = HoverColor;
            }

            RectangleF bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = UiPrimitives.RoundedRectangle(bounds, CornerRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                graphics.FillPath(brush, path);
                if (OutlineWidth > 0 && OutlineColor.A > 0)
                {
                    using (Pen pen = new Pen(OutlineColor, OutlineWidth))
                    {
                        graphics.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                ClientRectangle,
                Enabled ? ForeColor : Color.White,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(ClientRectangle, -5, -5);
                ControlPaint.DrawFocusRectangle(graphics, focusBounds, ForeColor, Color.Transparent);
            }
        }
    }

    internal static class UiPrimitives
    {
        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float safeRadius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
            float diameter = safeRadius * 2f;

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Timer _statusTimer;
        private readonly ToolTip _toolTip;

        private AppSettings _settings;
        private List<MacHistoryEntry> _history;
        private Label _currentAddressLabel;
        private Label _statusLabel;
        private ComboBox _targetAdapterCombo;
        private RoundedButton _generateButton;
        private RoundedButton _historyButton;
        private bool _loadingAdapters;
        private bool _applying;

        public MainForm()
        {
            _settings = AppStorage.LoadSettings();
            _history = AppStorage.LoadHistory();
            _statusTimer = new Timer();
            _statusTimer.Interval = 3800;
            _statusTimer.Tick += StatusTimerTick;
            _toolTip = new ToolTip();

            BuildWindow();
            BuildContent();
            LoadAdaptersAndAddress();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusTimer.Dispose();
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildWindow()
        {
            Text = "MAC 地址生成器";
            ClientSize = new Size(540, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // The application still works if Windows cannot read the icon.
            }
        }

        private void BuildContent()
        {
            Label eyebrow = new Label();
            eyebrow.Text = "RANDOM MAC";
            eyebrow.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            eyebrow.ForeColor = Color.FromArgb(15, 157, 138);
            eyebrow.TextAlign = ContentAlignment.MiddleCenter;
            eyebrow.AutoSize = false;
            eyebrow.SetBounds(0, 22, ClientSize.Width, 18);
            Controls.Add(eyebrow);

            Label caption = new Label();
            caption.Text = "当前地址";
            caption.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            caption.ForeColor = Color.FromArgb(109, 124, 118);
            caption.TextAlign = ContentAlignment.MiddleCenter;
            caption.AutoSize = false;
            caption.SetBounds(0, 66, ClientSize.Width, 24);
            Controls.Add(caption);

            _currentAddressLabel = new Label();
            _currentAddressLabel.Text = "—";
            _currentAddressLabel.Font = new Font("Consolas", 25F, FontStyle.Bold, GraphicsUnit.Point);
            _currentAddressLabel.ForeColor = Color.FromArgb(16, 51, 46);
            _currentAddressLabel.TextAlign = ContentAlignment.MiddleCenter;
            _currentAddressLabel.AutoSize = false;
            _currentAddressLabel.SetBounds(24, 92, ClientSize.Width - 48, 48);
            _currentAddressLabel.Cursor = Cursors.Hand;
            _currentAddressLabel.DoubleClick += CopyCurrentAddress;
            _toolTip.SetToolTip(_currentAddressLabel, "双击复制当前地址");
            Controls.Add(_currentAddressLabel);

            Label adapterLabel = new Label();
            adapterLabel.Text = "目标网卡";
            adapterLabel.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            adapterLabel.ForeColor = Color.FromArgb(109, 124, 118);
            adapterLabel.TextAlign = ContentAlignment.MiddleRight;
            adapterLabel.AutoSize = false;
            adapterLabel.SetBounds(30, 144, 76, 22);
            Controls.Add(adapterLabel);

            _targetAdapterCombo = new ComboBox();
            _targetAdapterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _targetAdapterCombo.FlatStyle = FlatStyle.Flat;
            _targetAdapterCombo.BackColor = Color.White;
            _targetAdapterCombo.ForeColor = Color.FromArgb(46, 68, 61);
            _targetAdapterCombo.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            _targetAdapterCombo.DrawMode = DrawMode.OwnerDrawFixed;
            _targetAdapterCombo.ItemHeight = 20;
            _targetAdapterCombo.SetBounds(112, 142, 398, 26);
            _targetAdapterCombo.DropDownWidth = 590;
            _targetAdapterCombo.MaxDropDownItems = 8;
            _targetAdapterCombo.IntegralHeight = false;
            _targetAdapterCombo.TabIndex = 0;
            _targetAdapterCombo.SelectedIndexChanged += TargetAdapterChanged;
            _targetAdapterCombo.DrawItem += TargetAdapterComboDrawItem;
            _toolTip.SetToolTip(_targetAdapterCombo, "选择要自动写入 MAC 地址的网卡");
            Controls.Add(_targetAdapterCombo);

            _generateButton = new RoundedButton();
            _generateButton.Text = "生成并自动写入网卡";
            _generateButton.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Point);
            _generateButton.ForeColor = Color.White;
            _generateButton.NormalColor = Color.FromArgb(15, 157, 138);
            _generateButton.HoverColor = Color.FromArgb(18, 172, 151);
            _generateButton.PressedColor = Color.FromArgb(12, 136, 120);
            _generateButton.CornerRadius = 16;
            _generateButton.SetBounds(105, 184, 330, 60);
            _generateButton.TabIndex = 1;
            _generateButton.Click += GenerateButtonClick;
            Controls.Add(_generateButton);

            _statusLabel = new Label();
            _statusLabel.Text = "生成后会自动写入所选网卡，旧地址会存入历史";
            _statusLabel.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            _statusLabel.ForeColor = Color.FromArgb(145, 158, 153);
            _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            _statusLabel.AutoSize = false;
            _statusLabel.SetBounds(24, 260, ClientSize.Width - 48, 24);
            Controls.Add(_statusLabel);

            Panel divider = new Panel();
            divider.BackColor = Color.FromArgb(236, 242, 239);
            divider.SetBounds(76, 302, ClientSize.Width - 152, 1);
            Controls.Add(divider);

            _historyButton = new RoundedButton();
            _historyButton.Text = "过往 MAC 地址";
            _historyButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _historyButton.ForeColor = Color.FromArgb(79, 99, 92);
            _historyButton.NormalColor = Color.White;
            _historyButton.HoverColor = Color.FromArgb(246, 249, 248);
            _historyButton.PressedColor = Color.FromArgb(238, 244, 241);
            _historyButton.OutlineColor = Color.FromArgb(214, 225, 220);
            _historyButton.OutlineWidth = 1;
            _historyButton.CornerRadius = 10;
            _historyButton.SetBounds(195, 326, 150, 36);
            _historyButton.TabIndex = 2;
            _historyButton.Click += HistoryButtonClick;
            Controls.Add(_historyButton);

            Label footer = new Label();
            footer.Text = "修改时网络会短暂中断 · 配置与历史保存在程序目录";
            footer.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
            footer.ForeColor = Color.FromArgb(184, 194, 190);
            footer.TextAlign = ContentAlignment.MiddleCenter;
            footer.AutoSize = false;
            footer.SetBounds(0, 389, ClientSize.Width, 18);
            Controls.Add(footer);
        }

        private void LoadAdaptersAndAddress()
        {
            _loadingAdapters = true;
            try
            {
                List<NetworkAdapterInfo> adapters = NetworkAdapterService.GetAdapters();
                _targetAdapterCombo.Items.Clear();
                foreach (NetworkAdapterInfo adapter in adapters)
                {
                    _targetAdapterCombo.Items.Add(adapter);
                }

                if (adapters.Count == 0)
                {
                    _generateButton.Enabled = false;
                    _targetAdapterCombo.Enabled = false;
                    _currentAddressLabel.Text = "—";
                    _statusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                    _statusLabel.Text = "没有检测到可修改的物理网卡";
                    return;
                }

                int selectedIndex = -1;
                if (_settings != null && !string.IsNullOrEmpty(_settings.AdapterId))
                {
                    for (int index = 0; index < adapters.Count; index++)
                    {
                        if (string.Equals(
                            adapters[index].Id,
                            _settings.AdapterId,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }

                if (selectedIndex < 0)
                {
                    for (int index = 0; index < adapters.Count; index++)
                    {
                        if (adapters[index].IsUp)
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }

                if (selectedIndex < 0)
                {
                    selectedIndex = 0;
                }

                _targetAdapterCombo.SelectedIndex = selectedIndex;
                NetworkAdapterInfo selected = _targetAdapterCombo.SelectedItem as NetworkAdapterInfo;
                if (selected != null)
                {
                    _currentAddressLabel.Text = selected.CurrentMac;
                    SaveSelectedAdapter(selected);
                }

                _generateButton.Enabled = true;
                _targetAdapterCombo.Enabled = true;
                _statusLabel.ForeColor = Color.FromArgb(145, 158, 153);
                _statusLabel.Text = "生成后会自动写入所选网卡，旧地址会存入历史";
            }
            catch (Exception exception)
            {
                _generateButton.Enabled = false;
                _targetAdapterCombo.Enabled = false;
                _currentAddressLabel.Text = "—";
                _statusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _statusLabel.Text = "读取网卡信息失败";
                MessageBox.Show(
                    this,
                    "读取网卡信息失败。\r\n\r\n" + exception.Message,
                    "MAC 地址生成器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _loadingAdapters = false;
            }
        }

        private void TargetAdapterComboDrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= _targetAdapterCombo.Items.Count)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color background = selected ? Color.FromArgb(232, 247, 243) : Color.White;
            Color foreground = selected ? Color.FromArgb(10, 111, 98) : Color.FromArgb(46, 68, 61);
            using (SolidBrush brush = new SolidBrush(background))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                _targetAdapterCombo.Items[e.Index].ToString(),
                e.Font,
                e.Bounds,
                foreground,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }

        private void TargetAdapterChanged(object sender, EventArgs e)
        {
            if (_loadingAdapters || _applying)
            {
                return;
            }

            NetworkAdapterInfo adapter = _targetAdapterCombo.SelectedItem as NetworkAdapterInfo;
            if (adapter == null)
            {
                return;
            }

            _currentAddressLabel.Text = adapter.CurrentMac;
            SaveSelectedAdapter(adapter);
            _statusLabel.ForeColor = Color.FromArgb(15, 157, 138);
            _statusLabel.Text = "已切换目标网卡，当前显示该网卡的实际地址";
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void SaveSelectedAdapter(NetworkAdapterInfo adapter)
        {
            if (_settings == null)
            {
                _settings = new AppSettings();
            }

            _settings.AdapterId = adapter.Id;
            _settings.Current = adapter.CurrentMac;
            try
            {
                AppStorage.SaveSettings(_settings);
            }
            catch (Exception exception)
            {
                _statusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _statusLabel.Text = "配置文件保存失败：" + exception.Message;
            }
        }

        private void GenerateButtonClick(object sender, EventArgs e)
        {
            if (_applying)
            {
                return;
            }

            NetworkAdapterInfo adapter = _targetAdapterCombo.SelectedItem as NetworkAdapterInfo;
            if (adapter == null)
            {
                MessageBox.Show(
                    this,
                    "没有可用的目标网卡。",
                    "MAC 地址生成器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string previous = _currentAddressLabel.Text;
            if (!MacAddressFactory.LooksValid(previous))
            {
                previous = adapter.CurrentMac;
            }

            if (!MacAddressFactory.LooksValid(previous))
            {
                MessageBox.Show(
                    this,
                    "无法读取所选网卡当前的 MAC 地址，请换一张网卡再试。",
                    "MAC 地址生成器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string next = MacAddressFactory.CreateRandom(previous);
            MacHistoryEntry addedEntry = new MacHistoryEntry();
            addedEntry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            addedEntry.Mac = previous;

            List<MacHistoryEntry> updatedHistory = new List<MacHistoryEntry>(_history);
            updatedHistory.Add(addedEntry);
            try
            {
                // Save the old address before changing the adapter.
                AppStorage.SaveHistory(updatedHistory);
                _history = updatedHistory;
            }
            catch (Exception exception)
            {
                ShowStorageError("无法保存历史记录，地址没有更改", exception);
                return;
            }

            SetApplyingState(true, "正在写入「" + adapter.Name + "」并重启网卡，网络会短暂中断…");
            Task.Factory.StartNew(delegate
            {
                string applyError = null;
                bool success = false;
                try
                {
                    success = NetworkAdapterService.TryApplyMac(adapter, next, out applyError);
                }
                catch (Exception exception)
                {
                    applyError = exception.Message;
                }

                ApplyOutcome outcome = new ApplyOutcome();
                outcome.Success = success;
                outcome.Error = applyError;
                outcome.NewMac = next;
                outcome.Adapter = adapter;
                outcome.HistoryEntry = addedEntry;
                return outcome;
            }).ContinueWith(delegate(Task<ApplyOutcome> task)
            {
                ApplyOutcome outcome;
                try
                {
                    outcome = task.Result;
                }
                catch (Exception exception)
                {
                    outcome = new ApplyOutcome();
                    outcome.Success = false;
                    outcome.Error = exception.Message;
                    outcome.NewMac = next;
                    outcome.Adapter = adapter;
                    outcome.HistoryEntry = addedEntry;
                }

                if (IsDisposed)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        CompleteApply(outcome);
                    }));
                }
                catch
                {
                    // The window is closing; the adapter operation is already finished.
                }
            });
        }

        private void CompleteApply(ApplyOutcome outcome)
        {
            if (IsDisposed)
            {
                return;
            }

            SetApplyingState(false, null);
            if (outcome.Success)
            {
                if (_settings == null)
                {
                    _settings = new AppSettings();
                }

                _settings.Current = outcome.NewMac;
                _settings.AdapterId = outcome.Adapter.Id;
                string saveError = null;
                try
                {
                    AppStorage.SaveSettings(_settings);
                }
                catch (Exception exception)
                {
                    saveError = exception.Message;
                }

                outcome.Adapter.CurrentMac = outcome.NewMac;
                _currentAddressLabel.Text = outcome.NewMac;
                _statusLabel.ForeColor = Color.FromArgb(15, 157, 138);
                _statusLabel.Text = "已写入「" + outcome.Adapter.Name + "」，网卡已重启";
                _statusTimer.Stop();
                _statusTimer.Start();

                if (saveError != null)
                {
                    MessageBox.Show(
                        this,
                        "MAC 地址已经写入网卡，但配置文件保存失败：\r\n\r\n" + saveError,
                        "MAC 地址生成器",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else
            {
                RollbackHistoryEntry(outcome.HistoryEntry);
                _statusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _statusLabel.Text = "写入网卡失败，地址没有更改";
                MessageBox.Show(
                    this,
                    "自动写入网卡失败，地址没有更改。\r\n\r\n" + (outcome.Error ?? "未知错误") +
                    "\r\n\r\n请确认程序以管理员身份运行，并检查所选网卡是否支持修改 MAC 地址。",
                    "无法写入网卡",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RollbackHistoryEntry(MacHistoryEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                List<MacHistoryEntry> rollback = new List<MacHistoryEntry>(_history);
                int lastIndex = rollback.Count - 1;
                if (lastIndex >= 0 &&
                    string.Equals(rollback[lastIndex].Mac, entry.Mac, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rollback[lastIndex].Time, entry.Time, StringComparison.Ordinal))
                {
                    rollback.RemoveAt(lastIndex);
                    AppStorage.SaveHistory(rollback);
                    _history = rollback;
                }
            }
            catch
            {
                // Keep the in-memory history; a duplicate entry is safer than data loss.
            }
        }

        private void SetApplyingState(bool applying, string message)
        {
            _applying = applying;
            _targetAdapterCombo.Enabled = !applying && _targetAdapterCombo.Items.Count > 0;
            _generateButton.Enabled = !applying && _targetAdapterCombo.Items.Count > 0;
            _historyButton.Enabled = !applying;
            _generateButton.Text = applying ? "正在写入网卡…" : "生成并自动写入网卡";

            if (applying)
            {
                _statusTimer.Stop();
                _statusLabel.ForeColor = Color.FromArgb(15, 157, 138);
                _statusLabel.Text = message ?? "正在写入网卡…";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_applying)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "正在写入网卡，请等待操作完成后再关闭。",
                    "MAC 地址生成器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            base.OnFormClosing(e);
        }

        private void HistoryButtonClick(object sender, EventArgs e)
        {
            using (HistoryForm historyForm = new HistoryForm(_history))
            {
                historyForm.ShowDialog(this);
            }
        }

        private void CopyCurrentAddress(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(_currentAddressLabel.Text);
                _statusLabel.ForeColor = Color.FromArgb(15, 157, 138);
                _statusLabel.Text = "当前地址已复制到剪贴板";
                _statusTimer.Stop();
                _statusTimer.Start();
            }
            catch
            {
                _statusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _statusLabel.Text = "复制失败，请稍后重试";
            }
        }

        private void StatusTimerTick(object sender, EventArgs e)
        {
            _statusTimer.Stop();
            _statusLabel.ForeColor = Color.FromArgb(145, 158, 153);
            _statusLabel.Text = "生成后会自动写入所选网卡，旧地址会存入历史";
        }

        private void ShowStorageError(string message, Exception exception)
        {
            MessageBox.Show(
                this,
                message + "。\r\n\r\n请确认程序目录可写后重试。\r\n\r\n详细信息：" + exception.Message,
                "MAC 地址生成器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private sealed class ApplyOutcome
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public string NewMac { get; set; }
            public NetworkAdapterInfo Adapter { get; set; }
            public MacHistoryEntry HistoryEntry { get; set; }
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly List<MacHistoryEntry> _entries;
        private readonly Timer _copyStatusTimer;
        private ListView _listView;
        private Panel _emptyPanel;
        private Label _copyStatusLabel;

        public HistoryForm(List<MacHistoryEntry> entries)
        {
            _entries = entries == null ? new List<MacHistoryEntry>() : entries;
            _copyStatusTimer = new Timer();
            _copyStatusTimer.Interval = 2600;
            _copyStatusTimer.Tick += CopyStatusTimerTick;

            BuildWindow();
            BuildContent();
            FillHistory();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _copyStatusTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildWindow()
        {
            Text = "过往 MAC 地址";
            ClientSize = new Size(560, 462);
            MinimumSize = new Size(576, 501);
            MaximumSize = new Size(576, 501);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Optional cosmetic detail.
            }
        }

        private void BuildContent()
        {
            Label title = new Label();
            title.Text = "过往 MAC 地址";
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.FromArgb(26, 47, 42);
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.AutoSize = false;
            title.SetBounds(32, 24, 300, 34);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "共 " + _entries.Count + " 条 · 最近的在最上方";
            subtitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            subtitle.ForeColor = Color.FromArgb(136, 151, 145);
            subtitle.TextAlign = ContentAlignment.MiddleLeft;
            subtitle.AutoSize = false;
            subtitle.SetBounds(34, 60, 400, 20);
            Controls.Add(subtitle);

            _listView = new ListView();
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.HideSelection = false;
            _listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _listView.MultiSelect = false;
            _listView.BorderStyle = BorderStyle.FixedSingle;
            _listView.BackColor = Color.White;
            _listView.ForeColor = Color.FromArgb(35, 55, 49);
            _listView.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _listView.SetBounds(32, 90, 496, 300);
            _listView.Columns.Add("时间", 186, HorizontalAlignment.Left);
            _listView.Columns.Add("MAC 地址", 286, HorizontalAlignment.Left);
            _listView.DoubleClick += CopySelectedEntry;
            Controls.Add(_listView);

            _emptyPanel = new Panel();
            _emptyPanel.BackColor = Color.White;
            _emptyPanel.BorderStyle = BorderStyle.FixedSingle;
            _emptyPanel.SetBounds(32, 90, 496, 300);
            Controls.Add(_emptyPanel);

            Label emptyLabel = new Label();
            emptyLabel.Text = "还没有历史记录\r\n生成新地址后，旧地址会出现在这里";
            emptyLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            emptyLabel.ForeColor = Color.FromArgb(157, 170, 165);
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.Dock = DockStyle.Fill;
            _emptyPanel.Controls.Add(emptyLabel);

            RoundedButton copyButton = new RoundedButton();
            copyButton.Text = "复制所选";
            copyButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            copyButton.ForeColor = Color.FromArgb(15, 137, 121);
            copyButton.NormalColor = Color.White;
            copyButton.HoverColor = Color.FromArgb(246, 250, 248);
            copyButton.PressedColor = Color.FromArgb(238, 245, 242);
            copyButton.OutlineColor = Color.FromArgb(192, 219, 211);
            copyButton.OutlineWidth = 1;
            copyButton.CornerRadius = 9;
            copyButton.SetBounds(32, 406, 102, 34);
            copyButton.Click += CopySelectedEntry;
            Controls.Add(copyButton);

            _copyStatusLabel = new Label();
            _copyStatusLabel.Text = "双击一条记录也可以复制";
            _copyStatusLabel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            _copyStatusLabel.ForeColor = Color.FromArgb(155, 167, 162);
            _copyStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _copyStatusLabel.AutoSize = false;
            _copyStatusLabel.SetBounds(148, 406, 264, 34);
            Controls.Add(_copyStatusLabel);

            RoundedButton closeButton = new RoundedButton();
            closeButton.Text = "关闭";
            closeButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            closeButton.ForeColor = Color.White;
            closeButton.NormalColor = Color.FromArgb(32, 58, 51);
            closeButton.HoverColor = Color.FromArgb(42, 72, 64);
            closeButton.PressedColor = Color.FromArgb(24, 46, 40);
            closeButton.CornerRadius = 9;
            closeButton.SetBounds(428, 406, 100, 34);
            closeButton.Click += CloseButtonClick;
            Controls.Add(closeButton);
        }

        private void FillHistory()
        {
            _listView.BeginUpdate();
            try
            {
                _listView.Items.Clear();
                for (int index = _entries.Count - 1; index >= 0; index--)
                {
                    MacHistoryEntry entry = _entries[index];
                    ListViewItem item = new ListViewItem(entry.Time ?? string.Empty);
                    item.SubItems.Add(entry.Mac ?? string.Empty);
                    _listView.Items.Add(item);
                }
            }
            finally
            {
                _listView.EndUpdate();
            }

            bool empty = _entries.Count == 0;
            _listView.Visible = !empty;
            _emptyPanel.Visible = empty;
            if (!empty)
            {
                _emptyPanel.SendToBack();
            }
        }

        private void CopySelectedEntry(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0)
            {
                _copyStatusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _copyStatusLabel.Text = "请先选择一条记录";
                return;
            }

            try
            {
                string mac = _listView.SelectedItems[0].SubItems[1].Text;
                Clipboard.SetText(mac);
                _copyStatusLabel.ForeColor = Color.FromArgb(15, 157, 138);
                _copyStatusLabel.Text = "已复制 " + mac;
                _copyStatusTimer.Stop();
                _copyStatusTimer.Start();
            }
            catch
            {
                _copyStatusLabel.ForeColor = Color.FromArgb(176, 78, 65);
                _copyStatusLabel.Text = "复制失败，请稍后重试";
            }
        }

        private void CopyStatusTimerTick(object sender, EventArgs e)
        {
            _copyStatusTimer.Stop();
            _copyStatusLabel.ForeColor = Color.FromArgb(155, 167, 162);
            _copyStatusLabel.Text = "双击一条记录也可以复制";
        }

        private void CloseButtonClick(object sender, EventArgs e)
        {
            Close();
        }
    }
}
