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
        private Point? lastMousePosition = null;
        private int currentItem = -1;
        private int currentLastMediaItem = -1;
        private bool isLoadingFiles = false;
        private bool exitEnabled = true;
        private CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private List<String> mediaPaths;
        private List<String> mediaFiles;
        private DispatcherTimer imageTimer;
        private DispatcherTimer timeoutTimer;
        private DispatcherTimer infoShowingTimer;
        private List<String> acceptedExtensionsImages = new List<string>() {".jpg", ".png", ".bmp", ".gif"};
        private List<String> acceptedExtensionsVideos = new List<string>() { ".avi", ".wmv", ".mpg", ".mpeg", ".mkv", ".mp4" };
        private List<String> lastMedia;
        private int algorithm;
        private int imageRotationAngle;
        private static LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private bool _isPlaying = false;
        private double _volume;
        private double volume {
            get { return _volume; }
            set {
                _volume = Math.Max(Math.Min(value, 1), 0);
                if (_mediaPlayer != null) _mediaPlayer.Volume = (int)(_volume * 100);
                PreferenceManager.WriteVolumeSetting(_volume);
                if (timeoutTimer != null) timeoutTimer.Start();
            }
        }
        public MainWindow(bool preview) {
            InitializeComponent();
            this.preview = preview;
            try {
                if (_libVLC == null) {
                    string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string libvlcPath = Path.Combine(exeDir, "libvlc", Environment.Is64BitProcess ? "win-x64" : "win-x86");
                    Core.Initialize(libvlcPath);
                    _libVLC = new LibVLC("--no-osd");
                }
                _mediaPlayer = new MediaPlayer(_libVLC);
                VlcVideoView.MediaPlayer = _mediaPlayer;
                _volume = PreferenceManager.ReadVolumeSetting();
                _mediaPlayer.Volume = (int)(_volume * 100);
                _mediaPlayer.EndReached += (s, a) => { Dispatcher.BeginInvoke(new Action(() => MediaEnded())); };
            } catch (Exception ex) {
                File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhotoVideoScreensaver_error.log"), DateTime.Now + ": VLC init failed: " + ex + Environment.NewLine);
                ShowError("VLC init failed: " + ex.Message);
            }
            imageTimer = new DispatcherTimer();            if (isLoadingFiles && ((currentItem + 1) >= mediaFiles.Count || mediaFiles.Count == 0)) {
                while (isLoadingFiles && currentItem >= mediaFiles.Count) { await Task.Delay(100); }
            }