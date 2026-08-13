// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Controls;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using MicaWPF.Controls;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;
using static WindowsMediaController.MediaManager;


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private int WM_TASKBARCREATED, WM_SHELLHOOK;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc _hookProc;

    private CancellationTokenSource cts; // to close the flyout after a certain time
    private long _lastFlyoutTime = 0;

    public readonly WindowsMediaController.MediaManager mediaManager = new();

    // for detecting changes in settings (lazy way)
    private int _position = SettingsManager.Current.Position;
    private bool _layout = SettingsManager.Current.CompactLayout;
    private bool _repeatEnabled = SettingsManager.Current.RepeatEnabled;
    private bool _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
    private bool _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
    private bool _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
    private bool _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
    private bool _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    private bool _mediaSessionSupportsSeekbar = false;
    private bool _acrylicEnabled = false;
    private int _themeOption = SettingsManager.Current.AppTheme;

    static Mutex singleton = new Mutex(true, "FluentFlyout");
    private NextUpWindow? nextUpWindow = null;
    private string currentTitle = "";

    private readonly int _seekbarUpdateInterval = 300;
    private readonly Timer _positionTimer;
    private bool _isActive;
    private bool _isDragging;
    private bool _isHiding = true;

    private LockWindow? lockWindow;
    private DateTime _lastSelfUpdateTimestamp = DateTime.MinValue;

    internal TaskbarWindow? taskbarWindow;

    private VolumeMixerWindow? volumeMixerWindow;

    internal static volatile bool ExplorerRestarting = false;

    public MainWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        if (!singleton.WaitOne(TimeSpan.Zero, true))
        {
            // Signal the existing instance to open settings
            Task.Run(() =>
            {
                try
                {
                    using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                    {
                        settingsEvent.Set();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to signal existing instance");
                }
            });

            Environment.Exit(0);
        }

        Logger.Info("Starting FluentFlyout MainWindow");

        // listen for the signal to open settings from another instance
        Task.Run(() =>
        {
            try
            {
                using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                {
                    while (true)
                    {
                        settingsEvent.WaitOne();
                        Application.Current.Dispatcher.Invoke(() => { SettingsWindow.ShowInstance(); });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Settings event listener error");
            }
        });

        try
        {
            SettingsManager.RestoreSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to restore settings: {ex.Message}");
            Logger.Error(ex, "Failed to restore settings");
        }

        DataContext = SettingsManager.Current;

        if (SettingsManager.Current.Startup == true)
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            string? executablePath = Environment.ProcessPath;
            if (key != null && executablePath != null)
                key.SetValue("FluentFlyout", executablePath);
        }

        if (!SettingsManager.Current.NIconHide)
            nIcon.Visibility = Visibility.Visible;

        cts = new CancellationTokenSource();

        mediaManager.Start();

        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -Width - 20;
        CustomWindowChrome.CaptionHeight = 0;

        mediaManager.OnAnyMediaPropertyChanged  += MediaManager_OnAnyMediaPropertyChanged;
        mediaManager.OnAnyPlaybackStateChanged  += CurrentSession_OnPlaybackStateChanged;
        mediaManager.OnAnyTimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        mediaManager.OnAnySessionClosed         += MediaManager_OnAnySessionClosed;

        WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
        WM_SHELLHOOK      = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(new WindowInteropHelper(this).Handle);

        _positionTimer = new Timer(SeekbarUpdateUi, null, Timeout.Infinite, Timeout.Infinite);
        if (_seekBarEnabled && GetActiveMediaSession() is { } session)
        {
            UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
        }

        string previousVersion = SettingsManager.Current.LastKnownVersion;
        _ = CheckForExperimentsOnStartupAsync(previousVersion);

        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();

            try
            {
                var version = Package.Current.Id.Version;
                SettingsManager.Current.LastKnownVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                SettingsManager.Current.LastKnownVersion = "debug";
            }

            Logger.Info($"Current version: {SettingsManager.Current.LastKnownVersion}");

            Notifications.ShowFirstOrUpdateNotification(previousVersion, SettingsManager.Current.LastKnownVersion);
            FlowDirection = SettingsManager.Current.FlowDirection;

            _ = CheckForUpdatesOnStartupAsync();
        });
    }

    private async Task CheckForExperimentsOnStartupAsync(string previousVersion)
    {
        await ExperimentsService.GetExperimentsAsync();
        OnboardingExperiment(previousVersion);
    }

    private void OnboardingExperiment(string previousVersion)
    {
        if (string.IsNullOrEmpty(previousVersion))
        {
            if (ExperimentsService.HasExperiments)
            {
                if (ExperimentsService.CheckUuidInExperiment("onboarding") == "A")
                    OnboardingWindow.ShowInstance();
                else
                {
                    SettingsWindow.ShowInstance();
                    _ = TelemetryService.SendTelemetryEventAsync("onboarding_completed", "onboarding");
                }
            }
            else
                OnboardingWindow.ShowInstance();
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateCheckerService.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);
            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion     = result.NewestVersion;
                UpdateState.Current.UpdateUrl         = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck   = result.CheckedAt;

                if (result.IsUpdateAvailable)
                    Notifications.ShowUpdateAvailableNotification(result.NewestVersion, result.UpdateUrl);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates on startup");
        }
    }

    public bool IsSessionAllowed(MediaSession? session)
    {
        if (session == null) return false;
        if (!SettingsManager.Current.AppFilteringEnabled) return true;

        string appId   = session.Id ?? string.Empty;
        string appName = MediaPlayerData.GetAndCacheMediaPlayerData(appId).Item1 ?? appId;

        if (SettingsManager.Current.AppFilteringMode == 0) // Blacklist
        {
            return !(SettingsManager.Current.BlockedApps?.Any(b =>
                appName.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                appId.Contains(b, StringComparison.OrdinalIgnoreCase)) == true);
        }
        else // Whitelist
        {
            return SettingsManager.Current.AllowedApps?.Any(a =>
                appName.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                appId.Contains(a, StringComparison.OrdinalIgnoreCase)) == true;
        }
    }

    public MediaSession? GetActiveMediaSession()
    {
        var validSessions = mediaManager.CurrentMediaSessions.Values.Where(IsSessionAllowed).ToList();
        if (validSessions.Count == 0) return null;

        var focused = mediaManager.GetFocusedSession();
        if (focused != null && validSessions.Any(s => s.Id == focused.Id))
            return focused;

        return validSessions.FirstOrDefault();
    }

    public void RefreshFilteredMedia()
    {
        UpdateTaskbar();

        if (IsVisible)
        {
            var activeSession = GetActiveMediaSession();
            UpdateUI(activeSession!);

            if (activeSession != null)
                HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
            else
                HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
        }
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
        GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "Failed to retrieve data from the player");
            return null;
        }
    }

    private void OpenSettings(object? sender, EventArgs e) => SettingsWindow.ShowInstance();

    private void ReportBug(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/unchihugo/FluentFlyout/issues/new/choose",
            UseShellExecute = true
        });
    }

    private void OpenRepository(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/unchihugo/FluentFlyout",
            UseShellExecute = true
        });
    }

    public void OpenLogsFolder(object? sender, EventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", FileSystemHelper.GetLogsPath());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
        }
    }

    public void UpdateTaskbar()
    {
        var activeSession = GetActiveMediaSession();
        if (!mediaManager.IsStarted || activeSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = TryGetMediaProperties(activeSession.ControlSession);
        if (songInfo == null) return;

        var playbackInfo = activeSession.ControlSession.GetPlaybackInfo();
        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
    }

    private void PauseOtherMediaSessionsIfNeeded(MediaSession mediaSession)
    {
        if (SettingsManager.Current.PauseOtherSessionsEnabled &&
            mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            PauseOtherSessions(mediaSession);
        }
    }

    private Task PauseOtherSessions(MediaSession currentMediaSession)
    {
        return Task.WhenAll(
            mediaManager.CurrentMediaSessions.Values.Select(session =>
            {
                if (session.Id != currentMediaSession.Id &&
                    session.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return session.ControlSession.TryPauseAsync().AsTask();

                return Task.CompletedTask;
            }));
    }

    internal void ToggleBlur()
    {
        if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled)
            WindowBlurHelper.EnableBlur(this);
        else
            WindowBlurHelper.DisableBlur(this);
    }

    private void CleanupResources()
    {
        try
        {
            mediaManager.OnAnyMediaPropertyChanged  -= MediaManager_OnAnyMediaPropertyChanged;
            mediaManager.OnAnyPlaybackStateChanged  -= CurrentSession_OnPlaybackStateChanged;
            mediaManager.OnAnyTimelinePropertyChanged -= MediaManager_OnAnyTimelinePropertyChanged;
            mediaManager.OnAnySessionClosed         -= MediaManager_OnAnySessionClosed;

            _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _positionTimer?.Dispose();
            cts?.Cancel();
            cts?.Dispose();

            TaskbarVisualizerControl.DisposeVisualizer();

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            DeregisterShellHookWindow(new WindowInteropHelper(this).Handle);

            if (lockWindow?.IsLoaded   == true) lockWindow.Close();
            if (nextUpWindow?.IsLoaded == true) nextUpWindow.Close();
            if (taskbarWindow?.IsLoaded == true) taskbarWindow.Close();
            if (volumeMixerWindow?.IsLoaded == true) volumeMixerWindow.Close();

            VolumeMixerWindow.ShowVolumeOsd();
            singleton?.Dispose();
            NLog.LogManager.Shutdown();
        }
        catch (ObjectDisposedException)
        {
            // harmless shutdown exceptions
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { CleanupResources(); }
        finally { base.OnClosed(e); }
    }

    private void MicaWindow_MouseEnter(object sender, MouseEventArgs e) => ShowMediaFlyout();

    private void NotifyIconQuit_Click(object sender, RoutedEventArgs e)
    {
        try { CleanupResources(); }
        finally { Application.Current.Shutdown(); }
    }

    private async void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateUILayout();
        ThemeManager.ApplySavedTheme();

        try
        {
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize tray icon");
        }

        try
        {
            await LicenseManager.Instance.InitializeAsync();
            SettingsManager.Current.IsPremiumUnlocked = LicenseManager.Instance.IsPremiumUnlocked;
            SettingsManager.Current.IsStoreVersion    = LicenseManager.Instance.IsStoreVersion;
            SettingsManager.SaveSettings();
            Logger.Info($"License synced on startup - Store: {SettingsManager.Current.IsStoreVersion}, Premium: {SettingsManager.Current.IsPremiumUnlocked}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize license");
        }

        await ExperimentsService.GetExperimentsAsync();

        BitmapHelper.GetDominantColors(1);
        taskbarWindow = new TaskbarWindow();
        UpdateTaskbar();
        volumeMixerWindow = new VolumeMixerWindow();
    }

    public void RecreateTaskbarWindow()
    {
        try
        {
            Logger.Info("Recreating Taskbar Widget window");
            try { taskbarWindow?.Close(); } catch { }
            taskbarWindow = null;
            taskbarWindow = new();
            UpdateTaskbar();
            Logger.Info("Taskbar Widget window recreated successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate Taskbar Widget window");
        }
    }

    private void nIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e)
    {
        if (SettingsManager.Current.NIconLeftClick == 0)
            OpenSettings(sender, e);
        else if (SettingsManager.Current.NIconLeftClick == 1)
            ShowMediaFlyout();
    }

    private void MediaFlyoutCloseButton_Click(object sender, RoutedEventArgs e)
    {
        ShowMediaFlyout(toggleMode: true);
    }
}
