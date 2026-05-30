using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Image = System.Drawing.Image;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VideoScreensaver {
    public partial class MainWindow : Window {
        private bool preview;
        private int currentItem = -1;
        private int currentLastMediaItem = -1;
        private bool isLoadingFiles = false;
        private CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private static readonly Random _random = new Random();
        private List<string> mediaPaths;
        private List<string> mediaFiles;
        private DispatcherTimer imageTimer;
        private DispatcherTimer timeoutTimer;
        private DispatcherTimer infoShowingTimer;
        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".png", ".bmp", ".gif" };
        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".avi", ".wmv", ".mpg", ".mpeg", ".mkv", ".mp4" };
        private List<string> lastMedia;
        private int algorithm;
        private int imageRotationAngle;

        private static LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private bool _isPlaying;
        private double _volume;
        private double _defaultVolume;
        private EventHandler<EventArgs> _volumePlayingHandler;

        private int VlcVolume {
            get { return (int)(_volume * 100); }
        }

        private void ApplyVolume() {
            if (_mediaPlayer != null) _mediaPlayer.Volume = VlcVolume;
        }

        public MainWindow(bool preview) {
            InitializeComponent();
            this.preview = preview;
            try {
                if (_libVLC == null) {
                    string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                    string libvlcPath = Path.Combine(exeDir, "libvlc", arch);
                    if (!Directory.Exists(libvlcPath))
                        libvlcPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoVideoScreensaver", "libvlc", arch);
                    Core.Initialize(libvlcPath);
                    _libVLC = new LibVLC("--no-osd", "--no-video-title-show", "--no-volume-save");
                }
                _mediaPlayer = new MediaPlayer(_libVLC);
                _defaultVolume = PreferenceManager.ReadVolumeSetting();
                _volume = _defaultVolume;
                ApplyVolume();
                _mediaPlayer.EndReached += (s, a) => Dispatcher.BeginInvoke(new Action(() => { StopVlc(); NextMediaItem(); }));
            } catch (Exception ex) {
                LogError("VLC init failed: " + ex);
            }
            // Install global low-level mouse hook for wheel events over VLC native window
            InstallMouseHook();
            InitClickTimer();
            imageTimer = new DispatcherTimer();
            imageTimer.Tick += (s, a) => { imageTimer.Stop(); FullScreenImage.Source = null; GC.Collect(0, GCCollectionMode.Optimized); NextMediaItem(); };
            imageTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(PreferenceManager.ReadIntervalSetting(), 1000));
            infoShowingTimer = new DispatcherTimer();
            infoShowingTimer.Tick += (s, a) => { infoShowingTimer.Stop(); infoShowingTimer.Interval = TimeSpan.FromSeconds(5); HideError(); };
            infoShowingTimer.Interval = TimeSpan.FromSeconds(5);
            if (preview) ShowError("Control volume with up/down arrows.");
            var timeout = PreferenceManager.ReadVolumeTimeoutSetting();
            if (timeout > 0) {
                timeoutTimer = new DispatcherTimer();
                timeoutTimer.Interval = TimeSpan.FromMinutes(timeout);
                timeoutTimer.Tick += (o, ev) => { _volume = 0; ApplyVolume(); ShowError("Volume muted"); infoShowingTimer.Interval = TimeSpan.FromSeconds(5); infoShowingTimer.Start(); };
                timeoutTimer.Start();
            }
        }
        private void ScrKeyDown(object sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Up: case Key.VolumeUp: _volume = Math.Min(_volume + 0.1, 1.0); ApplyVolume(); break;
                case Key.Down: case Key.VolumeDown: _volume = Math.Max(_volume - 0.1, 0); ApplyVolume(); break;
                case Key.VolumeMute: case Key.D0: _volume = 0; ApplyVolume(); break;
                case Key.Right: case Key.Tab: imageTimer.Stop(); NextMediaItem(); break;
                case Key.Left: case Key.Back: imageTimer.Stop(); PrevMediaItem(); break;
                case Key.P: TogglePause(); break;
                case Key.Delete: imageTimer.Stop(); if (_mediaPlayer != null && _isPlaying) _mediaPlayer.SetPause(true); PromptDeleteCurrentMedia(); break;
                case Key.I: Overlay.Visibility = Overlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; break;
                case Key.H: case Key.OemQuestion:
                    if (ErrorText.Visibility == Visibility.Visible && ErrorText.Text.StartsWith("Controls:")) {
                        HideError();
                    } else {
                        ShowError("Controls:\n" +
                                  "Esc or double-click - Exit screensaver. Mouse movement is ignored.\n" +
                                  "Left arrow, Backspace, or left-click - Previous media\n" +
                                  "Right arrow, Tab, or right-click - Next media\n" +
                                  "Up/Down arrows or mouse wheel - Adjust volume\n" +
                                  "0 or Mute key - Mute volume\n" +
                                  "F - Show current file in File Explorer\n" +
                                  "P - Pause slideshow\n" +
                                  "Del - Delete current file\n" +
                                  "I - Toggle info overlay\n" +
                                  "R - Rotate image 90 degrees\n" +
                                  "O - Open current file in default application\n" +
                                  "H or ? - Show help");
                        infoShowingTimer.Stop();
                    }
                    break;
                case Key.R: if (currentItem >= 0 && currentItem < mediaFiles.Count && IsImage(mediaFiles[currentItem])) { imageRotationAngle += 90; imageTimer.Stop(); LoadImage(mediaFiles[currentItem]); } break;
                case Key.O: if (currentItem >= 0 && currentItem < mediaFiles.Count) { Process.Start(mediaFiles[currentItem]); EndScreensaver(); } break;
                case Key.F: if (currentItem >= 0 && currentItem < mediaFiles.Count) { Process.Start("explorer.exe", "/select,\"" + mediaFiles[currentItem] + "\""); EndScreensaver(); } break;
                case Key.Escape: EndScreensaver(); break;
                default: break;
            }
            e.Handled = true;
        }

        private void TogglePause() {
            if (FullScreenImage.Visibility == Visibility.Visible) {
                if (imageTimer.IsEnabled) imageTimer.Stop(); else { HideError(); imageTimer.Start(); }
            } else if (_mediaPlayer != null) {
                if (_isPlaying) { _mediaPlayer.SetPause(true); _isPlaying = false; } else { _mediaPlayer.SetPause(false); _isPlaying = true; HideError(); }
            }
        }

        private void PromptDeleteCurrentMedia() {
            if (currentItem < 0 || currentItem >= mediaFiles.Count) return;
            var dial = new PromptDialog("Delete file?", "Type yes to delete " + Path.GetFileName(mediaFiles[currentItem]), "yes,ok");
            if (dial.ShowDialog() == true) {
                string fileToDelete = mediaFiles[currentItem];
                if (algorithm == PreferenceManager.ALGORITHM_RANDOM && lastMedia != null) {
                    if (lastMedia.IndexOf(fileToDelete) >= currentLastMediaItem) currentLastMediaItem--;
                    lastMedia.Remove(fileToDelete);
                }
                mediaFiles.RemoveAt(currentItem);
                PrevMediaItem();
                try { File.Delete(fileToDelete); } catch { }
                try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoVideoScreensaver_deletedFiles.log"), DateTime.Now + ": " + fileToDelete + Environment.NewLine); } catch { }
            } else {
                if (FullScreenImage.Visibility == Visibility.Visible) imageTimer.Start();
                else if (_mediaPlayer != null) _mediaPlayer.SetPause(false);
            }
        }

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private static IntPtr _mouseHookId = IntPtr.Zero;
        private static MainWindow _instance;
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelMouseProc _mouseProc;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private void InstallMouseHook() {
            _instance = this;
            _mouseProc = MouseHookCallback;
            using (var proc = System.Diagnostics.Process.GetCurrentProcess())
            using (var mod = proc.MainModule) {
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(mod.ModuleName), 0);
            }
        }

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MOUSEHWHEEL = 0x020E;
        private static int _pendingAction = 0; // 0=none, 1=left click pending, 2=right click, 3=exit
        private static DateTime _lastLeftClick = DateTime.MinValue;
        private DispatcherTimer _clickTimer;

        private void InitClickTimer() {
            _clickTimer = new DispatcherTimer();
            _clickTimer.Interval = TimeSpan.FromMilliseconds(350);
            _clickTimer.Tick += (s, a) => {
                _clickTimer.Stop();
                if (_pendingAction == 1) {
                    if (currentItem > 0 || (algorithm == PreferenceManager.ALGORITHM_RANDOM && lastMedia != null && currentLastMediaItem > 0)) {
                        imageTimer.Stop(); PrevMediaItem();
                    }
                }
                _pendingAction = 0;
            };
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && _instance != null) {
                int msg = wParam.ToInt32();
                if (msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL) {
                    int mouseData = System.Runtime.InteropServices.Marshal.ReadInt32(lParam, 8);
                    int delta = (short)((mouseData >> 16) & 0xFFFF);
                    if (msg == WM_MOUSEWHEEL) {
                        _instance.Dispatcher.BeginInvoke(new Action(() => {
                            _instance._volume = Math.Max(Math.Min(_instance._volume + delta / 1200.0, 1.0), 0);
                            _instance.ApplyVolume();
                        }));
                    }
                } else if (msg == WM_LBUTTONDOWN) {
                    DateTime now = DateTime.Now;
                    if ((now - _lastLeftClick).TotalMilliseconds < 400) {
                        // Double click - exit
                        _pendingAction = 3;
                        _lastLeftClick = DateTime.MinValue;
                        _instance.Dispatcher.BeginInvoke(new Action(() => {
                            _instance._clickTimer.Stop();
                            _instance.EndScreensaver();
                        }));
                    } else {
                        // First click - wait to see if double click follows
                        _lastLeftClick = now;
                        _pendingAction = 1;
                        _instance.Dispatcher.BeginInvoke(new Action(() => {
                            _instance._clickTimer.Stop();
                            _instance._clickTimer.Start();
                        }));
                    }
                } else if (msg == WM_RBUTTONDOWN) {
                    _instance.Dispatcher.BeginInvoke(new Action(() => {
                        _instance.imageTimer.Stop();
                        _instance.NextMediaItem();
                    }));
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void EndScreensaver() {
            if (!preview) { if (_mouseHookId != IntPtr.Zero) UnhookWindowsHookEx(_mouseHookId); ShowCursor(true); cancellationSource.Cancel(); StopVlc(); if (Application.Current != null) Application.Current.Shutdown(); }
        }

        private void StopVlc() {
            try {
                if (_mediaPlayer != null) {
                    _mediaPlayer.EncounteredError -= OnMediaError;
                    if (_volumePlayingHandler != null) { _mediaPlayer.Playing -= _volumePlayingHandler; _volumePlayingHandler = null; }
                    if (_mediaPlayer.IsPlaying) ThreadPool.QueueUserWorkItem(_ => { try { _mediaPlayer.Stop(); } catch { } });
                }
            } catch { }
        }
        private bool IsImage(string path) { return ImageExts.Contains(Path.GetExtension(path)); }
        private bool IsMedia(string fileName) {
            if (fileName.Contains("$RECYCLE.BIN")) return false;
            string ext = Path.GetExtension(fileName);
            return ImageExts.Contains(ext) || VideoExts.Contains(ext);
        }

        private async Task AddMediaFilesFromDirRecursive(string path, CancellationToken token) {
            try {
                foreach (var f in Directory.GetFiles(path)) { if (token.IsCancellationRequested) return; if (IsMedia(f)) mediaFiles.Add(f); }
                foreach (var d in Directory.GetDirectories(path)) { if (token.IsCancellationRequested) return; await AddMediaFilesFromDirRecursive(d, token); }
            } catch { }
        }

        private async Task LoadFiles() {
            int tempAlg = algorithm;
            if (algorithm == PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT) algorithm = PreferenceManager.ALGORITHM_RANDOM;
            ConnectToNas(mediaPaths);
            foreach (string vp in mediaPaths) {
                if (!Directory.Exists(vp)) { LogError("LoadFiles: directory not found: " + vp); continue; }
                LogError("LoadFiles: scanning " + vp);
                await AddMediaFilesFromDirRecursive(vp, cancellationSource.Token);
            }
            algorithm = tempAlg;
            if (algorithm == PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT) {
                if (lastMedia != null && lastMedia.Count > 0) {
                    var historySet = new HashSet<string>(lastMedia);
                    mediaFiles = mediaFiles.Where(f => !historySet.Contains(f)).OrderBy(_ => Guid.NewGuid()).ToList();
                    mediaFiles.InsertRange(0, lastMedia);
                    currentItem = currentLastMediaItem >= 0 ? currentLastMediaItem : 0;
                } else {
                    mediaFiles = mediaFiles.OrderBy(_ => Guid.NewGuid()).ToList();
                }
            }
            if (algorithm == PreferenceManager.ALGORITHM_RANDOM) { currentItem = 0; currentLastMediaItem = 0; }
            isLoadingFiles = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (!preview) { while (ShowCursor(false) >= 0) { } }
            mediaPaths = PreferenceManager.ReadVideoSettings();
            mediaFiles = new List<string>();
            algorithm = PreferenceManager.ReadAlgorithmSetting();
            if (algorithm == PreferenceManager.ALGORITHM_RANDOM || algorithm == PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT) lastMedia = new List<string>();
            isLoadingFiles = true;
            Task.Factory.StartNew(() => LoadFiles());
            if (mediaPaths.Count == 0) { ShowError("Configure screensaver first."); return; }
            NextMediaItem();
        }
        private void PrevMediaItem() {
            _isPlaying = false; imageRotationAngle = 0;
            switch (algorithm) {
                case PreferenceManager.ALGORITHM_SEQUENTIAL:
                case PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT:
                    currentItem--; if (currentItem < 0) currentItem = isLoadingFiles ? 0 : Math.Max(mediaFiles.Count - 1, 0); break;
                case PreferenceManager.ALGORITHM_RANDOM:
                    if (lastMedia != null && lastMedia.Count >= 2 && currentLastMediaItem > 0) { currentLastMediaItem--; currentItem = mediaFiles.IndexOf(lastMedia[currentLastMediaItem]); }
                    break;
            }
            ShowCurrentItem();
        }

        private async void NextMediaItem() {
            _isPlaying = false; imageRotationAngle = 0;
            if (isLoadingFiles && mediaFiles.Count == 0) {
                while (isLoadingFiles && mediaFiles.Count == 0) await Task.Delay(200);
            }
            if (isLoadingFiles && (currentItem + 1 >= mediaFiles.Count)) {
                while (isLoadingFiles && currentItem + 1 >= mediaFiles.Count) await Task.Delay(200);
            }
            if (mediaFiles.Count == 0) { ShowError("No media files found."); return; }
            switch (algorithm) {
                case PreferenceManager.ALGORITHM_SEQUENTIAL:
                case PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT:
                    if (isLoadingFiles && currentItem <= 0) await Task.Delay(1000);
                    currentItem++; if (currentItem >= mediaFiles.Count) currentItem = 0; break;
                case PreferenceManager.ALGORITHM_RANDOM:
                    if (isLoadingFiles && currentItem <= 0) await Task.Delay(1000);
                    if (lastMedia != null && currentLastMediaItem < lastMedia.Count - 1) { currentLastMediaItem++; currentItem = mediaFiles.IndexOf(lastMedia[currentLastMediaItem]); }
                    else {
                        currentItem = _random.Next(mediaFiles.Count);
                        if (lastMedia != null) { lastMedia.Add(mediaFiles[currentItem]); if (lastMedia.Count > 100) lastMedia.RemoveAt(0); currentLastMediaItem = lastMedia.Count - 1; }
                    }
                    break;
            }
            ShowCurrentItem();
        }

        private void ShowCurrentItem() {
            HideError();
            if (mediaFiles.Count == 0 || currentItem < 0 || currentItem >= mediaFiles.Count) { ShowError("No media files found."); return; }
            string file = mediaFiles[currentItem];
            if (IsImage(file)) LoadImage(file); else LoadMedia(file);
        }
        private void LoadImage(string filename) {
            if (_mediaPlayer != null && _mediaPlayer.IsPlaying) {
                ThreadPool.QueueUserWorkItem(_ => { try { _mediaPlayer.Stop(); } catch { } });
            }
            FullScreenImage.Visibility = Visibility.Visible;
            VlcVideoView.Visibility = Visibility.Collapsed;
            FullScreenImage.RenderTransform = null;
            Overlay.Text = "";
            string ext = Path.GetExtension(filename).ToLower();
            if (ext == ".jpg") {
                UInt16 orient = 1;
                try {
                    var exif = new ExifUtils();
                    if (exif.ReadExifFromFile(filename)) { orient = exif.GetOrientation(); Overlay.Text = filename + Environment.NewLine + exif.GetInfoString(); }
                } catch { }
                if (imageRotationAngle == 90) {
                    try { orient = ExifUtils.RotateImageViaInPlaceBitmapMetadataWriter(filename, orient); }
                    catch { try { orient = ExifUtils.RotateImageViaTranscoding(filename, orient); } catch { } }
                }
                imageRotationAngle = ExifUtils.GetBitmapRotationAngleByRotationFlipType(ExifUtils.GetRotateFlipTypeByExifOrientationData(orient));
            } else if (imageRotationAngle == 90) {
                try {
                    using (var fs = File.Open(filename, FileMode.Open, FileAccess.ReadWrite))
                    using (var img = Image.FromStream(fs, false, false)) {
                        img.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);
                        fs.Seek(0, SeekOrigin.Begin);
                        if (ext == ".png") img.Save(fs, ImageFormat.Png);
                        else if (ext == ".bmp") img.Save(fs, ImageFormat.Bmp);
                        else if (ext == ".gif") img.Save(fs, ImageFormat.Gif);
                    }
                    imageRotationAngle = 0;
                } catch { }
            }
            try {
                using (var s = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read)) {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)SystemParameters.PrimaryScreenWidth;
                    bmp.StreamSource = s;
                    bmp.EndInit();
                    bmp.Freeze();
                    if (imageRotationAngle != 0) {
                        var tb = new TransformedBitmap(); tb.BeginInit(); tb.Source = bmp; tb.Transform = new RotateTransform(imageRotationAngle); tb.EndInit(); tb.Freeze();
                        FullScreenImage.Source = tb;
                    } else {
                        FullScreenImage.Source = bmp;
                    }
                    imageRotationAngle = 0;
                    imageTimer.Start();
                    if (string.IsNullOrWhiteSpace(Overlay.Text)) Overlay.Text = filename + "\n" + bmp.PixelWidth + "x" + bmp.PixelHeight;
                }
            } catch {
                FullScreenImage.Source = null;
                NextMediaItem();
                return;
            }
        }

        private void LoadMedia(string filename) {
            FullScreenImage.Source = null;
            FullScreenImage.Visibility = Visibility.Collapsed;
            if (VlcVideoView.MediaPlayer == null) VlcVideoView.MediaPlayer = _mediaPlayer;
            VlcVideoView.Visibility = Visibility.Visible;
            _volume = _defaultVolume;
            var media = new LibVLCSharp.Shared.Media(_libVLC, new Uri(filename));
            media.AddOption(":start-volume=" + VlcVolume);
            _mediaPlayer.EncounteredError += OnMediaError;
            // Re-apply volume once VLC's audio pipeline is actually running
            if (_volumePlayingHandler != null) _mediaPlayer.Playing -= _volumePlayingHandler;
            _volumePlayingHandler = (s, a) => {
                _mediaPlayer.Playing -= _volumePlayingHandler;
                _volumePlayingHandler = null;
                Dispatcher.BeginInvoke(new Action(() => ApplyVolume()));
            };
            _mediaPlayer.Playing += _volumePlayingHandler;
            _mediaPlayer.Play(media);
            _isPlaying = true;
            ApplyVolume();
            Overlay.Text = Path.GetFileName(filename);
        }

        private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.Visibility = Visibility.Visible; if (preview) ErrorText.FontSize = 12; }
        private void HideError() { ErrorText.Visibility = Visibility.Collapsed; }

        private void OnMediaError(object sender, EventArgs e) {
            if (_mediaPlayer != null) _mediaPlayer.EncounteredError -= OnMediaError;
            Dispatcher.BeginInvoke(new Action(() => { StopVlc(); NextMediaItem(); }));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        [System.Runtime.InteropServices.DllImport("mpr.dll")]
        private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string password, string username, int flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NETRESOURCE {
            public int dwScope; public int dwType; public int dwDisplayType; public int dwUsage;
            public string lpLocalName; public string lpRemoteName; public string lpComment; public string lpProvider;
        }

        private static void ConnectToNas(List<string> mediaPaths) {
            string user = PreferenceManager.ReadNasUsername();
            string pass = PreferenceManager.ReadNasPassword();
            if (string.IsNullOrEmpty(user)) { LogError("NAS: no username configured, skipping auth"); return; }
            // Find unique UNC servers from media paths
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in mediaPaths) {
                if (p.StartsWith("\\\\")) {
                    // Extract \\server\share from the path
                    string[] parts = p.TrimStart(new char[]{(char)92}).Split(new char[]{(char)92});
                    if (parts.Length >= 2) servers.Add("\\\\" + parts[0] + "\\" + parts[1]);
                    else if (parts.Length == 1) servers.Add("\\\\" + parts[0]);
                }
            }
            LogError("NAS: found " + servers.Count + " server(s) to authenticate from " + mediaPaths.Count + " paths");
            foreach (var server in servers) {
                LogError("NAS: connecting to " + server + " as " + user);
                try {
                    var nr = new NETRESOURCE();
                    nr.dwType = 1;
                    nr.lpRemoteName = server;
                    int result = WNetAddConnection2(ref nr, pass, user, 0);
                    if (result == 0) LogError("NAS: connected to " + server + " OK");
                    else if (result == 1219) LogError("NAS: " + server + " already connected");
                    else LogError("NAS: connect to " + server + " failed with error " + result);
                } catch (Exception ex) { LogError("NAS connect " + server + ": " + ex.Message); }
            }
        }

        private static void LogError(string msg) {
            try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoVideoScreensaver_error.log"), DateTime.Now + ": " + msg + Environment.NewLine); } catch { }
        }
    }
}