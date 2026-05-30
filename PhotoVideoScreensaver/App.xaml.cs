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
        static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoVideoScreensaver_error.log");

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(IntPtr hWnd);

        public App() {
            DispatcherUnhandledException += (s, e) => { try { File.AppendAllText(LogPath, DateTime.Now + ": " + e.Exception + Environment.NewLine); } catch { } e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { try { File.AppendAllText(LogPath, DateTime.Now + ": " + e.ExceptionObject + Environment.NewLine); } catch { } };
        }

        private void OnStartup(object sender, StartupEventArgs e) {
            if (e.Args.Length > 0) {
                string arg = e.Args[0].Length >= 2 ? e.Args[0].Substring(0, 2).ToLower() : "";
                if (arg == "/c") { new SettingsWindow().ShowDialog(); Shutdown(0); return; }
                if (arg == "/p" && e.Args.Length > 1) { ShowInParent(new IntPtr(Convert.ToInt32(e.Args[1]))); return; }
            }
            // Black out secondary screens
            foreach (var screen in Screen.AllScreens) {
                if (!screen.Primary) {
                    var bw = new Window();
                    bw.WindowStyle = WindowStyle.None;
                    bw.ResizeMode = ResizeMode.NoResize;
                    bw.ShowInTaskbar = false;
                    bw.AllowsTransparency = true;
                    bw.Background = System.Windows.Media.Brushes.Black;
                    bw.Left = screen.Bounds.Left;
                    bw.Top = screen.Bounds.Top;
                    bw.Width = screen.Bounds.Width;
                    bw.Height = screen.Bounds.Height;
                    bw.Topmost = true;
                    bw.Cursor = System.Windows.Input.Cursors.None;
                    bw.ForceCursor = true;
                    bw.KeyDown += (ks, ke) => { Shutdown(); };
                    bw.MouseDown += (ms, me) => { if (me.ClickCount >= 2) Shutdown(); };
                    bw.Show();
                }
            }
            // Main screensaver on primary screen
            var scr = Screen.PrimaryScreen;
            var w = new MainWindow(false);
            w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.NoResize; w.ShowInTaskbar = false;
            w.Left = scr.Bounds.Left; w.Top = scr.Bounds.Top;
            w.Width = scr.Bounds.Width; w.Height = scr.Bounds.Height;
            w.Topmost = true; w.ForceCursor = true; w.WindowState = WindowState.Normal;
            w.Show();
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
            await Task.Factory.StartNew(() => { while (IsWindow(parentHwnd)) Task.Delay(1000).Wait(); });
            Shutdown();
        }
    }
}