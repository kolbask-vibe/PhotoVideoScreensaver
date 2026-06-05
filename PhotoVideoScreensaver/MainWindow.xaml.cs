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
using System.Windows.Interop;
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
        private readonly object _mediaFilesLock = new object();
        private DispatcherTimer imageTimer;
        private DispatcherTimer timeoutTimer;
        private DispatcherTimer infoShowingTimer;
        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".png", ".bmp", ".gif" };
        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".avi", ".wmv", ".mpg", ".mpeg", ".mkv", ".mp4" };
        private List<string> lastMedia;
        private int algorithm;
        private int imageRotationAngle;
        private int _consecutiveErrors = 0;
        private const int MAX_CONSECUTIVE_ERRORS = 50;

        private static LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private bool _isPlaying;
        private double _volume;
        private double _defaultVolume;
        private EventHandler<EventArgs> _volumePlayingHandler;
        private EventHandler<EventArgs> _playingHandler;

        private static Task _vlcInitTask;

        private static readonly object _vlcInitLock = new object();

        private static void InitializeVlcStatic() {
            lock (_vlcInitLock) {
                if (_libVLC != null) return;
                string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string libvlcPath = Path.Combine(exeDir, "libvlc", arch);
                if (!Directory.Exists(libvlcPath)) {
                    // When .scr runs from System32, find VLC libraries via the install directory
                    string installDir = App.GetInstallDir();
                    if (installDir != null) {
                        string regPath = Path.Combine(installDir, "libvlc", arch);
                        if (Directory.Exists(regPath)) libvlcPath = regPath;
                    }
                }
                if (!Directory.Exists(libvlcPath))
                    libvlcPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoVideoScreensaver", "libvlc", arch);
                Core.Initialize(libvlcPath);
                _libVLC = new LibVLC("--no-osd", "--no-video-title-show", "--no-volume-save");
            }
        }

        private async Task EnsureVlcInitialized() {
            await _vlcInitTask;
            if (_mediaPlayer == null) {
                _mediaPlayer = new MediaPlayer(_libVLC);
                _mediaPlayer.EndReached += (s, a) => Dispatcher.BeginInvoke(new Action(() => { StopVlc(); NextMediaItem(); }));
                ApplyVolume();
            }
        }

        private int VlcVolume {
            get { return (int)(_volume * 100); }
        }

        private void ApplyVolume() {
            if (_mediaPlayer != null) _mediaPlayer.Volume = VlcVolume;
        }

        public MainWindow(bool preview) {
            InitializeComponent();
            this.preview = preview;
            if (_vlcInitTask == null) {
                _vlcInitTask = Task.Run(() => InitializeVlcStatic());
            }
            _defaultVolume = PreferenceManager.ReadVolumeSetting();
            _volume = _defaultVolume;
            ApplyVolume();
            // Install global low-level mouse hook for wheel events over VLC native window
            if (!preview) {
                InstallMouseHook();
            }
            InitClickTimer();
            imageTimer = new DispatcherTimer();
            imageTimer.Tick += (s, a) => { imageTimer.Stop(); FullScreenImage.Source = null; NextMediaItem(); };
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
                case Key.I: Overlay.Visibility = Overlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; break;
                case Key.H: case Key.OemQuestion:
                    if (ErrorText.Visibility == Visibility.Visible && ErrorText.Text.StartsWith("Controls:")) {
                        HideError();
                    } else {
                        ShowError("Controls:\n" +
                                  "Esc, standard keys, or double-click - Exit screensaver.\n" +
                                  "Left arrow, Backspace, or left-click - Previous media\n" +
                                  "Right arrow, Tab, or right-click - Next media\n" +
                                  "Up/Down arrows or mouse wheel - Adjust volume\n" +
                                  "0 or Mute key - Mute volume\n" +
                                  "F - Show current file in File Explorer\n" +
                                  "P - Pause slideshow\n" +
                                  "I - Toggle info overlay\n" +
                                  "R - Rotate image 90 degrees\n" +
                                  "H or ? - Show help");
                        infoShowingTimer.Stop();
                    }
                    break;
                case Key.R:
                    string rFile = null;
                    lock (_mediaFilesLock) {
                        if (currentItem >= 0 && currentItem < mediaFiles.Count) {
                            string ext = Path.GetExtension(mediaFiles[currentItem]);
                            if (string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)) {
                                rFile = mediaFiles[currentItem];
                            }
                        }
                    }
                    if (rFile != null) {
                        imageRotationAngle += 90;
                        imageTimer.Stop();
                        LoadImage(rFile);
                    }
                    break;
                case Key.F:
                    string fFile = null;
                    lock (_mediaFilesLock) {
                        if (currentItem >= 0 && currentItem < mediaFiles.Count) {
                            fFile = mediaFiles[currentItem];
                        }
                    }
                    if (fFile != null) {
                        Process.Start("explorer.exe", "/select,\"" + fFile + "\"");
                        EndScreensaver();
                    }
                    break;
                case Key.Escape: EndScreensaver(); break;
                default: EndScreensaver(); break;
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

        // --- Win32 helpers for hiding VLC native windows (Win11 file-path flash fix) ---
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLongW(IntPtr hWnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_HIDE = 0;

        /// <summary>
        /// Finds all native HWNDs on the current thread that are NOT our main WPF window
        /// and sanitizes them: clears title text and applies WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        /// so they never flash on screen as a File Explorer-like window (Win11 DWM compositor issue).
        /// </summary>
        private void SanitizeVlcNativeWindows() {
            try {
                IntPtr mainHwnd = new WindowInteropHelper(this).Handle;
                uint threadId = GetCurrentThreadId();
                var foreignWindows = new List<IntPtr>();
                EnumThreadWindows(threadId, (hWnd, _) => {
                    if (hWnd != mainHwnd) foreignWindows.Add(hWnd);
                    return true;
                }, IntPtr.Zero);
                foreach (var hWnd in foreignWindows) {
                    // Clear the window title so the file path is never visible
                    SetWindowText(hWnd, "");
                    // Apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE so it doesn't flash in taskbar/alt-tab
                    int exStyle = GetWindowLongW(hWnd, GWL_EXSTYLE);
                    SetWindowLongW(hWnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                }
            } catch { }
        }

        /// <summary>
        /// Hides all native HWNDs on the current thread that are NOT our main WPF window.
        /// Called during shutdown to prevent the VLC native window from flashing on exit.
        /// </summary>
        private void HideAllVlcNativeWindows() {
            try {
                IntPtr mainHwnd = new WindowInteropHelper(this).Handle;
                uint threadId = GetCurrentThreadId();
                EnumThreadWindows(threadId, (hWnd, _) => {
                    if (hWnd != mainHwnd) {
                        ShowWindow(hWnd, SW_HIDE);
                    }
                    return true;
                }, IntPtr.Zero);
            } catch { }
        }

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
        private const int WM_MOUSEMOVE = 0x0200;
        private static System.Drawing.Point _initialMousePos = new System.Drawing.Point(-1, -1);
        private static int _pendingAction = 0; // 0=none, 1=left click pending, 2=right click, 3=exit
        private static DateTime _lastLeftClick = DateTime.MinValue;
        private DispatcherTimer _clickTimer;

        private void InitClickTimer() {
            _clickTimer = new DispatcherTimer();
            _clickTimer.Interval = TimeSpan.FromMilliseconds(350);
            _clickTimer.Tick += (s, a) => {
                _clickTimer.Stop();
                if (_pendingAction == 1) {
                    bool shouldGoPrev = false;
                    lock (_mediaFilesLock) {
                        shouldGoPrev = currentItem > 0 || (algorithm == PreferenceManager.ALGORITHM_RANDOM && lastMedia != null && currentLastMediaItem > 0);
                    }
                    if (shouldGoPrev) {
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

        public async void EndScreensaver() {
            if (!preview) {
                if (_mouseHookId != IntPtr.Zero) {
                    UnhookWindowsHookEx(_mouseHookId);
                    _mouseHookId = IntPtr.Zero;
                }
                ShowCursor(true);
                cancellationSource.Cancel();

                // Fire and forget VLC stop to avoid hanging exit
                try {
                    if (_mediaPlayer != null) {
                        _mediaPlayer.EncounteredError -= OnMediaError;
                        if (_volumePlayingHandler != null) {
                            _mediaPlayer.Playing -= _volumePlayingHandler;
                            _volumePlayingHandler = null;
                        }
                        if (_playingHandler != null) {
                            _mediaPlayer.Playing -= _playingHandler;
                            _playingHandler = null;
                        }
                        if (_mediaPlayer.IsPlaying) {
                            ThreadPool.QueueUserWorkItem(_ => { try { _mediaPlayer.Stop(); } catch { } });
                        }
                    }
                } catch { }

                // Hide all VLC native HWNDs before closing WPF windows to prevent
                // the Win11 DWM compositor from flashing the native window (which
                // has the file path in its title bar) when WPF windows close.
                HideAllVlcNativeWindows();

                await Task.Delay(100);

                Environment.Exit(0);
            }
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            if (_mouseHookId != IntPtr.Zero) {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }
            HideAllVlcNativeWindows();
            Environment.Exit(0);
        }

        private void StopVlc() {
            try {
                if (_mediaPlayer != null) {
                    _mediaPlayer.EncounteredError -= OnMediaError;
                    if (_volumePlayingHandler != null) { _mediaPlayer.Playing -= _volumePlayingHandler; _volumePlayingHandler = null; }
                    if (_playingHandler != null) { _mediaPlayer.Playing -= _playingHandler; _playingHandler = null; }
                    if (_mediaPlayer.IsPlaying) ThreadPool.QueueUserWorkItem(_ => { try { _mediaPlayer.Stop(); } catch { } });
                }
            } catch { }
        }
        private bool IsImage(string path) { return ImageExts.Contains(Path.GetExtension(path)); }
        private bool IsMedia(string fileName) {
            if (fileName.IndexOf("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            try {
                var attr = File.GetAttributes(fileName);
                if (((int)attr & 0x00441000) != 0) {
                    return false;
                }
            } catch { }
            string ext = Path.GetExtension(fileName);
            return ImageExts.Contains(ext) || VideoExts.Contains(ext);
        }

        private async Task AddMediaFilesFromDirRecursive(string path, CancellationToken token) {
            try {
                foreach (var f in Directory.GetFiles(path)) { 
                    if (token.IsCancellationRequested) return; 
                    if (IsMedia(f)) {
                        lock (_mediaFilesLock) {
                            mediaFiles.Add(f);
                        }
                    }
                }
                foreach (var d in Directory.GetDirectories(path)) { 
                    if (token.IsCancellationRequested) return; 
                    try {
                        var attr = File.GetAttributes(d);
                        if (((int)attr & 0x00441000) != 0) continue;
                    } catch { }
                    await AddMediaFilesFromDirRecursive(d, token); 
                }
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

            bool empty = false;
            lock (_mediaFilesLock) {
                empty = mediaFiles.Count == 0;
            }
            if (empty) {
                LogError("LoadFiles: no media files found in configured paths. Loading fallback images from C:\\Windows\\Web.");
                LoadFallbackImages();
            }

            algorithm = tempAlg;
            lock (_mediaFilesLock) {
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
        }

        private void LoadFallbackImages() {
            try {
                string webDir = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows", "Web");
                if (Directory.Exists(webDir)) {
                    string[] searchPaths = new string[] {
                        Path.Combine(webDir, "Wallpaper"),
                        Path.Combine(webDir, "Screen")
                    };
                    foreach (var path in searchPaths) {
                        if (Directory.Exists(path)) {
                            AddFallbackImagesFromDir(path);
                        }
                    }
                }
            } catch (Exception ex) {
                LogError("LoadFallbackImages error: " + ex.Message);
            }
        }

        private void AddFallbackImagesFromDir(string path) {
            try {
                foreach (var f in Directory.GetFiles(path)) {
                    if (IsMedia(f)) {
                        lock (_mediaFilesLock) {
                            mediaFiles.Add(f);
                        }
                    }
                }
                foreach (var d in Directory.GetDirectories(path)) {
                    try {
                        var attr = File.GetAttributes(d);
                        if (((int)attr & 0x00441000) != 0) continue;
                    } catch { }
                    AddFallbackImagesFromDir(d);
                }
            } catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (!preview) { while (ShowCursor(false) >= 0) { } }
            mediaPaths = PreferenceManager.ReadVideoSettings();
            lock (_mediaFilesLock) {
                mediaFiles = new List<string>();
            }
            algorithm = PreferenceManager.ReadAlgorithmSetting();
            if (algorithm == PreferenceManager.ALGORITHM_RANDOM || algorithm == PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT) lastMedia = new List<string>();
            isLoadingFiles = true;
            Focus();
            Keyboard.Focus(this);
            var unused = Task.Run(async () => await LoadFiles());
            if (mediaPaths.Count == 0) { ShowError("Configure screensaver first."); return; }
            NextMediaItem();
        }
        private void PrevMediaItem() {
            _isPlaying = false; imageRotationAngle = 0;
            lock (_mediaFilesLock) {
                switch (algorithm) {
                    case PreferenceManager.ALGORITHM_SEQUENTIAL:
                    case PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT:
                        currentItem--; if (currentItem < 0) currentItem = isLoadingFiles ? 0 : Math.Max(mediaFiles.Count - 1, 0); break;
                    case PreferenceManager.ALGORITHM_RANDOM:
                        if (lastMedia != null && lastMedia.Count >= 2 && currentLastMediaItem > 0) { currentLastMediaItem--; currentItem = mediaFiles.IndexOf(lastMedia[currentLastMediaItem]); }
                        break;
                }
            }
            ShowCurrentItem();
        }

        private async void NextMediaItem() {
            _isPlaying = false; imageRotationAngle = 0;
            while (true) {
                bool shouldWait = false;
                lock (_mediaFilesLock) {
                    shouldWait = isLoadingFiles && mediaFiles.Count == 0;
                }
                if (shouldWait) await Task.Delay(200);
                else break;
            }
            while (true) {
                bool shouldWait = false;
                lock (_mediaFilesLock) {
                    shouldWait = isLoadingFiles && (currentItem + 1 >= mediaFiles.Count);
                }
                if (shouldWait) await Task.Delay(200);
                else break;
            }
            bool noFiles = false;
            lock (_mediaFilesLock) {
                noFiles = mediaFiles.Count == 0;
            }
            if (noFiles) { ShowError("No media files found."); return; }
            
            bool delaySequential = false;
            lock (_mediaFilesLock) {
                if (algorithm == PreferenceManager.ALGORITHM_SEQUENTIAL || algorithm == PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT) {
                    delaySequential = isLoadingFiles && currentItem <= 0;
                }
            }
            if (delaySequential) {
                await Task.Delay(1000);
            }

            lock (_mediaFilesLock) {
                switch (algorithm) {
                    case PreferenceManager.ALGORITHM_SEQUENTIAL:
                    case PreferenceManager.ALGORITHM_RANDOM_NO_REPEAT:
                        currentItem++; if (currentItem >= mediaFiles.Count) currentItem = 0; break;
                    case PreferenceManager.ALGORITHM_RANDOM:
                        if (lastMedia != null && currentLastMediaItem < lastMedia.Count - 1) { currentLastMediaItem++; currentItem = mediaFiles.IndexOf(lastMedia[currentLastMediaItem]); }
                        else {
                            currentItem = _random.Next(mediaFiles.Count);
                            if (lastMedia != null) { lastMedia.Add(mediaFiles[currentItem]); if (lastMedia.Count > 100) lastMedia.RemoveAt(0); currentLastMediaItem = lastMedia.Count - 1; }
                        }
                        break;
                }
            }
            ShowCurrentItem();
        }

        private void ShowCurrentItem() {
            HideError();
            string file = null;
            lock (_mediaFilesLock) {
                if (mediaFiles.Count == 0 || currentItem < 0 || currentItem >= mediaFiles.Count) { ShowError("No media files found."); return; }
                file = mediaFiles[currentItem];
            }
            if (IsImage(file)) LoadImage(file); else LoadMedia(file);
        }
        private void LoadImage(string filename) {
            if (_mediaPlayer != null && _mediaPlayer.IsPlaying) {
                ThreadPool.QueueUserWorkItem(_ => { try { _mediaPlayer.Stop(); } catch { } });
            }
            FullScreenImage.Visibility = Visibility.Visible;
            // Defer collapsing VlcVideoView until the image is successfully loaded to avoid transparent "holes"
            FullScreenImage.RenderTransform = null;
            Overlay.Text = "";
            string ext = Path.GetExtension(filename).ToLower();
            if (ext == ".jpg" || ext == ".jpeg") {
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
            }
            try {
                using (var s = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read)) {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
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
                    _consecutiveErrors = 0;
                    imageTimer.Start();
                    if (string.IsNullOrWhiteSpace(Overlay.Text)) Overlay.Text = filename + "\n" + bmp.PixelWidth + "x" + bmp.PixelHeight;
                }
                VlcVideoView.Visibility = Visibility.Hidden;
            } catch {
                FullScreenImage.Source = null;
                _consecutiveErrors++;
                if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) { ShowError("Too many errors loading media. Check your media files."); return; }
                NextMediaItem();
                return;
            }
        }

        private async void LoadMedia(string filename) {
            try {
                await EnsureVlcInitialized();
            } catch (Exception ex) {
                LogError("VLC deferred init failed: " + ex);
                ShowError("VLC initialization failed.");
                return;
            }
            if (VlcVideoView.MediaPlayer == null) VlcVideoView.MediaPlayer = _mediaPlayer;
            // Sanitize any existing VLC native windows immediately when attaching the media player.
            // This prevents file-path-titled native windows from flashing on Win11.
            SanitizeVlcNativeWindows();
            _volume = _defaultVolume;
            using (var media = new LibVLCSharp.Shared.Media(_libVLC, new Uri(filename))) {
                media.AddOption(":start-volume=" + VlcVolume);
                _mediaPlayer.EncounteredError += OnMediaError;

                if (_playingHandler != null) _mediaPlayer.Playing -= _playingHandler;
                _playingHandler = (s, a) => {
                    _mediaPlayer.Playing -= _playingHandler;
                    _playingHandler = null;
                    Dispatcher.BeginInvoke(new Action(() => {
                        // Sanitize VLC native windows again now that playback has started,
                        // as VLC may have created new windows with file-path titles.
                        SanitizeVlcNativeWindows();
                        VlcVideoView.Visibility = Visibility.Visible;
                        FullScreenImage.Source = null;
                        FullScreenImage.Visibility = Visibility.Collapsed;
                    }));
                };
                _mediaPlayer.Playing += _playingHandler;

                if (_volumePlayingHandler != null) _mediaPlayer.Playing -= _volumePlayingHandler;
                _volumePlayingHandler = (s, a) => {
                    _mediaPlayer.Playing -= _volumePlayingHandler;
                    _volumePlayingHandler = null;
                    Dispatcher.BeginInvoke(new Action(() => ApplyVolume()));
                };
                _mediaPlayer.Playing += _volumePlayingHandler;
                _mediaPlayer.Play(media);
            }
            _isPlaying = true;
            _consecutiveErrors = 0;
            ApplyVolume();
            Overlay.Text = Path.GetFileName(filename);
        }

        private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.Visibility = Visibility.Visible; if (preview) ErrorText.FontSize = 12; }
        private void HideError() { ErrorText.Visibility = Visibility.Collapsed; }

        private void OnMediaError(object sender, EventArgs e) {
            if (_mediaPlayer != null) _mediaPlayer.EncounteredError -= OnMediaError;
            Dispatcher.BeginInvoke(new Action(() => {
                StopVlc();
                _consecutiveErrors++;
                if (_consecutiveErrors >= MAX_CONSECUTIVE_ERRORS) { ShowError("Too many errors loading media. Check your media files."); return; }
                NextMediaItem();
            }));
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
                LogError("NAS: connecting to " + server + " as [credentials redacted]");
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
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "PhotoVideoScreensaver_error.log"), DateTime.Now + ": " + msg + Environment.NewLine); } catch { }
        }
    }
}