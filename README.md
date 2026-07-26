# PhotoVideoScreensaver

A Windows screensaver that displays photos and videos from local or network folders with hardware-accelerated playback powered by LibVLCSharp.

## Download

Download the latest installer from [Releases](../../releases). Run `PhotoVideoScreensaver_2.7.1_setup.exe` — it will install the screensaver, register it with Windows, and optionally open the configuration dialog. Requires admin rights. Upgrades over previous versions automatically.

## Features

- Hardware-accelerated video playback via VLC engine (no separate VLC install needed)
- Supports JPG, PNG, BMP, GIF images with EXIF orientation and rotation
- Supports AVI, WMV, MPG, MPEG, MKV, MP4 video formats
- Network share (UNC path) support with optional NAS credentials
- NAS passwords encrypted via Windows DPAPI
- System volume control — screensaver sets Windows master volume directly
- Multi-monitor support — secondary screens blacked out
- Auto-skip broken or unreadable files
- Memory-optimized image loading (decoded at screen resolution)
- Sequential, Random, and Random (no repeat) playback modes
- Complete uninstaller — removes all files, folders, and registry settings

## Configuration

Open Screen Saver Settings and click "Settings..." to configure:

- **Media folders** — local folders or UNC paths (e.g. `\\NAS\photos`)
- **Network credentials** — optional username/password for NAS access
- **Interval (seconds)** — time between image transitions
- **Volume (0–100)** — sets Windows system volume directly
- **Mute after (min)** — auto-mute after this many minutes
- **Algorithm** — Sequential, Random, or Random (no repeat)

## Controls

| Input | Action |
|-------|--------|
| Esc / Standard keys | Exit screensaver |
| Right arrow / Tab / right click | Next |
| Left arrow / Backspace / left click | Previous |
| Double-click | Exit screensaver |
| Up / Down arrows | Volume up / down |
| Mouse wheel / touchpad scroll | Volume up / down |
| F | Show current file in File Explorer |
| P | Pause / unpause |
| I | Toggle info overlay |
| H | Show help |
| R | Rotate image |

Significant mouse movement or swiping exits the screensaver. Volume and playback mouse controls are active only when interacting directly.

## Cloud Photos

The screensaver works with cloud photo libraries via their desktop sync clients:
- **Google Photos**: Install Google Drive for Desktop, enable photo backup to Drive, point screensaver at the synced folder
- **OneDrive**: Photos sync to `C:\Users\<name>\OneDrive\Pictures` by default — just add that folder

## Requirements

- Windows 7, 8, 10, or 11
- .NET Framework 4.7.2+ (preinstalled on Win 10 1803+; available via Windows Update on 8.1)

## Building from Source

Prerequisites: .NET Framework 4.7.2 targeting pack or Visual Studio with .NET desktop workload.

```
msbuild PhotoVideoScreensaver\VideoScreensaver.csproj /p:Configuration=Release /p:Platform=AnyCPU
```

NuGet packages must be restored first (they are in the `packages/` folder or restore via `nuget restore`).

To build the installer, install [Inno Setup 6](https://jrsoftware.org/isdl.php) and run:

```
iscc installer.iss
```

## Technical Details

- WPF application targeting .NET Framework 4.7.2
- Video playback: [LibVLCSharp 3.9.6](https://github.com/videolan/libvlcsharp) with bundled VLC 3.0.21
- System volume: Windows Core Audio API (`IAudioEndpointVolume`)
- Mouse input: Global low-level hook (`WH_MOUSE_LL`) for input over VLC's native window
- Cursor hiding: Win32 `ShowCursor(false)` system-wide
- NAS auth: Win32 `WNetAddConnection2` with DPAPI-encrypted passwords
- Settings: Windows Registry `HKCU\Software\VideoScreensaver`
- Error log: `Documents\PhotoVideoScreensaver_error.log`

## Changelog

### v2.7.1
- **Video Buffering & Performance Enhancements**:
  - Increased file, network, and SMB caching buffers to 3000ms (3 seconds) in LibVLC to eliminate stuttering and buffering pauses over Wi-Fi and NAS network shares.
  - Enabled hardware video acceleration (`--avcodec-hw=any`) for video decoding.
  - Enabled frame-skipping flags (`--drop-late-frames`, `--skip-frames`) to prevent video freezes during temporary network jitter.

### v2.7.0
- **Sequence Repeat & Looping Fix**:
  - Fixed a critical bug where `ALGORITHM_RANDOM` and `ALGORITHM_RANDOM_NO_REPEAT` caused the screensaver to replay the same sequence of images twice after background folder indexing completed.
  - Implemented automatic $O(N)$ Fisher-Yates cycle re-shuffling in `Random (no repeat)` mode when reaching the end of the media collection, ensuring media sequences never repeat in identical order across playback cycles.
  - Eliminated index resetting upon loading completion, preserving active slideshow position and playback state seamlessly.
- **Performance & Deduplication**:
  - Added automatic case-insensitive file path deduplication during media discovery, preventing duplicate entries for nested or double-added directories.
  - Replaced $O(N \log N)$ Guid-based random sorting with an $O(N)$ Fisher-Yates shuffle algorithm.
- **Thread Safety & Stability**:
  - Encapsulated `lastMedia` navigation history under thread-lock synchronization to eliminate cross-thread collection modification exceptions between background indexing tasks and the WPF UI thread.
  - Added defensive screen bounds checking for High-DPI primary displays and improved consecutive image rotation (`R` key) handling.

### v2.6.2
- **Security & Vulnerability Fixes**:
  - Removed dangerous DPAPI `LocalMachine` fallback that allowed any local user account to decrypt stored NAS passwords.
  - Redacted NAS username from `%TEMP%` error logs.
  - Added path canonicalization (`Path.GetFullPath()`) to prevent network path traversal via `..` segments.
- **Stability & Performance**:
  - Fixed a thread pool starvation bug in the preview window mode.
  - Resolved native memory and thread handle leaks in VLC media loading.
  - Fixed memory leak of decoded frame data in EXIF image reader.
  - Transitioned background media loading to proper async/await `Task.Run` scheduling.
  - Implemented consecutive-error circuit breaker to safely skip completely broken media directories without infinite loops.
  - Ensured thread-safe locking during LibVLC core initialization.
  - Improved image sharpness on High-DPI screens by decoding to physical screen width.
  - Fixed potential crash on 64-bit platforms when launching the preview window by parsing system-provided window handles as 64-bit integers.
- **Bug Fixes & Refinements**:
  - Fixed a multi-monitor layout and coordinate alignment mismatch on High-DPI displays by using a dynamic WPF scaling matrix.
  - Fixed thread-safety race conditions on the scanned media list using thread lock synchronization.
  - Resolved lock screen / logon screen settings lookup: the screensaver now scans loaded HKEY_USERS hives when running under the SYSTEM context to display local media on resume/lock.
  - Deactivated the global mouse hook in preview mode to prevent system-wide input interception.
  - Implemented standard and interactive mouse swipe-to-exit with a 200px threshold.
  - Fully removed the obsolete O and Delete hotkeys. Retained and thread-locked the F key functionality.
  - Restrained image rotation options exclusively to JPEGs and JPGs.
  - Restricted uninstall registry cleanups to the current user hive to prevent invasive antiviruses from flagging uninstallation.
  - Made the `$RECYCLE.BIN` filter case-insensitive to ensure reliable skipping of recycle bin items.

### v2.5.8
- Offloaded native VLC library loading (`Core.Initialize`) to a background task at startup. This prevents the UI thread from freezing during antivirus scans on first launch, ensuring the blackout load screen remains fully responsive to key presses (including `Esc` to exit immediately).

### v2.5.7
- Resolved non-responsiveness of Esc key during the initial blackout load window by forcing logical keyboard focus onto the screensaver MainWindow in XAML and OnLoaded.
- Rewrote the uninstaller registry settings and log file cleanup to run natively inside the Inno Setup uninstaller script (Pascal code), ensuring 100% reliable settings deletion across all user profiles.

### v2.5.6
- Completely eliminated Windows Explorer window flashes on startup by setting WindowState to Normal and using native SetForegroundWindow activation.
- Wiped all screensaver registry settings across all user profiles on uninstallation by iterating over loaded hives in HKEY_USERS, ensuring settings do not survive upgrades or uninstallation under Administrator context.
- Added uninstaller cleanup for the error log file.

### v2.5.5
- Fixed screensaver startup causing Windows Explorer to flash or appear briefly on screen. The VLC video surface is no longer pre-initialized at window creation time, eliminating native window conflicts during startup.
- Improved uninstaller: screensaver registration in the .DEFAULT registry hive is now cleaned up on uninstall.

### v2.5.4
- Fixed volume ignoring settings: videos could start with sound even when volume was set to zero, because VLC's asynchronous playback reset the volume before the application could apply it. Volume is now re-applied via a one-shot Playing event once VLC's audio pipeline is ready.
- Fixed slow startup on multi-monitor setups: the screensaver now immediately displays a black screen while scanning folders, instead of leaving the active window visible for up to 30 seconds.
- NAS authentication and folder scanning now run on a background thread, keeping the UI responsive from the first moment.

### v2.5.3
- Fixed Settings window layout alignment issues, positioning &quot;Add network path&quot; and &quot;Screensaver controls&quot; sections horizontally in the same grid row
- Removed the redundant top border divider above the Playback section
- Expanded input fields (Path, Username, Password) in the network path settings to stretch and automatically occupy the full column width, preventing them from being cut off on the right

### v2.5.2
- Changed the screensaver settings screen layout from portrait to landscape
- Expanded the Screensaver controls section in settings to include all 12 controls from the help overlay
- Added red warning message about potential slow indexing on first preview run
- Fixed inconsistent help overlay toggle behavior (H/? now behaves like I)
- Reverted the "Please wait, files are indexing" preview-mode overlay to maintain codebase simplicity

### v2.5.1
- Displayed a black screen with "Please wait, files are indexing" message in Preview mode until the slideshow starts
- Settings dialog UI updates:
  - Renamed "Algorithm" to "Mode"
  - Renamed "Volume" to "Video volume"
  - Added "P - Pause slideshow" keyboard shortcut description
  - Adjusted wording of control descriptions
- Updated setup file naming format to `PhotoVideoScreensaver_<version>_setup.exe`

### v2.5.0
- Volume control is now per-application (VLC only) instead of system-wide
- Background music (Spotify, YouTube, etc.) continues unaffected while screensaver runs
- System master volume is no longer modified or restored on exit

### v2.4.2
- Fixed bug causing duplicate photos (loops) in Random mode.
- Fixed out-of-order toggling bug in Random (No Repeat) mode when background scans complete.

### v2.4.1
- Updated core LibVLCSharp components to 3.9.7.1
- Updated native VideoLAN.LibVLC.Windows binaries to 3.0.23.1
- Preserved Windows 7 compatibility

### v2.4.0
- F key: exit screensaver and open Explorer with current file highlighted
- System volume control via Core Audio API (volume setting = % of hardware max)
- Original system volume saved and restored on exit
- Complete uninstaller (removes all files, registry, and folders)
- Left/right click navigation, double-click exit
- Global mouse hook for wheel volume control over VLC window
- Cursor hidden system-wide
- Multi-monitor black-out for secondary screens
- Auto-skip broken media files
- NAS credential support with DPAPI encryption
- Network path (UNC) support in settings
- Memory-optimized image loading
- LibVLCSharp 3.9.6, .NET Framework 4.7.2

### v1.0.0 (original fork)
- Migrated from WPF MediaElement to LibVLCSharp
- Code quality fixes, async improvements, error logging
- Inno Setup installer

## Acknowledgements

Forked from [chrislott/Videosaver](https://github.com/chrislott/Videosaver), originally from [SourceForge](https://sourceforge.net/projects/videosaver/). Original work by [Michael Barnathan](https://sourceforge.net/u/metasquares/profile/) and Chris Lott. Earlier enhancements by @sergeiwork.

## License

Released under GPL v3.
