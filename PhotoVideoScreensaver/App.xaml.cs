using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;

namespace VideoScreensaver {
    public partial class App : Application {
        static string LogPath = Path.Combine(Path.GetTempPath(), "PhotoVideoScreensaver_error.log");

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(IntPtr hWnd);

        static App() {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }

        public App() {
            DispatcherUnhandledException += (s, e) => { try { File.AppendAllText(LogPath, DateTime.Now + ": " + e.Exception + Environment.NewLine); } catch { } e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { try { File.AppendAllText(LogPath, DateTime.Now + ": " + e.ExceptionObject + Environment.NewLine); } catch { } };
        }

        private static string _installDir;
        internal static string GetInstallDir() {
            if (_installDir != null) return _installDir;
            // 1. Read install directory from HKLM 64-bit registry view
            try {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"Software\VideoScreensaver")) {
                    if (key != null) {
                        string dir = key.GetValue("InstallDir") as string;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) {
                            _installDir = dir;
                            return _installDir;
                        }
                    }
                }
            } catch { }
            // 2. Read install directory from HKLM 32-bit registry view (WoW6432Node fallback)
            try {
                using (var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32))
                using (var key = hklm.OpenSubKey(@"Software\VideoScreensaver")) {
                    if (key != null) {
                        string dir = key.GetValue("InstallDir") as string;
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) {
                            _installDir = dir;
                            return _installDir;
                        }
                    }
                }
            } catch { }
            // Fallback: directory of the running assembly (when running directly from install dir)
            _installDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            return _installDir;
        }

        private static System.Reflection.Assembly ResolveAssembly(object sender, ResolveEventArgs args) {
            try {
                string installDir = GetInstallDir();
                if (installDir == null) return null;
                string dllName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
                string path = Path.Combine(installDir, dllName);
                if (File.Exists(path)) return System.Reflection.Assembly.LoadFrom(path);
            } catch { }
            return null;
        }

        private void OnStartup(object sender, StartupEventArgs e) {
            if (e.Args.Length > 0) {
                string arg = e.Args[0].Length >= 2 ? e.Args[0].Substring(0, 2).ToLower() : "";
                if (arg == "/c") { new SettingsWindow().ShowDialog(); Shutdown(0); return; }
                if (arg == "/p" && e.Args.Length > 1) {
                    long hwndLong;
                    if (long.TryParse(e.Args[1], out hwndLong)) {
                        ShowInParent(new IntPtr(hwndLong));
                    } else {
                        Shutdown(0);
                    }
                    return;
                }
                if (arg == "/u" || e.Args[0].ToLower() == "/uninstall") {
                    PreferenceManager.RemoveRegistryKeys();
                    try {
                        string logPath = Path.Combine(Path.GetTempPath(), "PhotoVideoScreensaver_error.log");
                        if (File.Exists(logPath)) File.Delete(logPath);
                    } catch { }
                    Shutdown(0);
                    return;
                }
            }
            try {
                // Calculate system DPI scale factor using GDI+ (safe under SYSTEM account / logon desktop)
                double dpiScaleX = 1.0;
                double dpiScaleY = 1.0;
                try {
                    using (var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) {
                        double dx = graphics.DpiX / 96.0;
                        double dy = graphics.DpiY / 96.0;
                        if (dx > 0 && dy > 0) {
                            dpiScaleX = dx;
                            dpiScaleY = dy;
                        }
                    }
                } catch { }

                // Main screensaver on primary screen
                var scr = Screen.PrimaryScreen;
                var w = new MainWindow(false);
                w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false;
                
                double mainLeft = 0;
                double mainTop = 0;
                double mainWidth = SystemParameters.PrimaryScreenWidth;
                double mainHeight = SystemParameters.PrimaryScreenHeight;

                if (scr != null) {
                    mainLeft = scr.Bounds.Left / dpiScaleX;
                    mainTop = scr.Bounds.Top / dpiScaleY;
                    mainWidth = scr.Bounds.Width / dpiScaleX;
                    mainHeight = scr.Bounds.Height / dpiScaleY;
                }

                w.Left = mainLeft;
                w.Top = mainTop;
                w.Width = mainWidth;
                w.Height = mainHeight;

                w.Topmost = true; w.ForceCursor = true; w.WindowState = WindowState.Normal;
                w.Show();
                try {
                    var helper = new WindowInteropHelper(w);
                    SetForegroundWindow(helper.Handle);
                } catch { }

                // Black out secondary screens
                foreach (var screen in Screen.AllScreens) {
                    if (scr != null && !screen.Primary) {
                        var bw = new Window();
                        bw.WindowStyle = WindowStyle.None;
                        bw.ResizeMode = ResizeMode.NoResize;
                        bw.ShowInTaskbar = false;
                        bw.Background = System.Windows.Media.Brushes.Black;
                        
                        bw.Left = screen.Bounds.Left / dpiScaleX;
                        bw.Top = screen.Bounds.Top / dpiScaleY;
                        bw.Width = screen.Bounds.Width / dpiScaleX;
                        bw.Height = screen.Bounds.Height / dpiScaleY;

                        bw.Topmost = true;
                        bw.Cursor = System.Windows.Input.Cursors.None;
                        bw.ForceCursor = true;
                        bw.KeyDown += (ks, ke) => { w.EndScreensaver(); };
                        bw.MouseDown += (ms, me) => { if (me.ClickCount >= 2) w.EndScreensaver(); };
                        bw.Show();
                    }
                }
            } catch (Exception ex) {
                try { File.AppendAllText(LogPath, DateTime.Now + ": Startup crash: " + ex + Environment.NewLine); } catch { }
                Shutdown(0);
            }
        }

        private async void ShowInParent(IntPtr parentHwnd) {
            var pw = new MainWindow(true);
            var wh = new WindowInteropHelper(pw); wh.Owner = parentHwnd;
            pw.WindowState = WindowState.Normal;
            RECT r; GetClientRect(parentHwnd, out r);
            pw.Left = 0; pw.Top = 0; pw.Width = 0; pw.Height = 0;
            pw.ShowInTaskbar = false; pw.ShowActivated = false;
            pw.Cursor = Cursors.Arrow; pw.ForceCursor = false;
            IntPtr focus = GetForegroundWindow();
            pw.Show();
            SetParent(wh.Handle, parentHwnd);
            SetWindowLong(wh.Handle, -16, new IntPtr(0x10000000 | 0x40000000 | 0x02000000));
            pw.Width = r.right - r.left; pw.Height = r.bottom - r.top;
            SetForegroundWindow(focus);
            await Task.Run(async () => { while (IsWindow(parentHwnd)) await Task.Delay(1000); });
            Shutdown();
        }
    }
}