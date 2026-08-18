// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using FluentFlyoutWPF.Classes;
using Windows.Media.Control;
using Wpf.Ui.Controls;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarWidgetControl.xaml
/// </summary>
public partial class TaskbarWidgetControl : UserControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;

    public TaskbarWidgetControl()
    {
        InitializeComponent();

        // Apply Windows theme colors (independent of the app theme setting)
        ApplyWindowsTheme();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
        };

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); ;

        // Initialize control order
        ReorderControls();
    }

    public void ReorderControls()
    {
        // Remove ControlsStackPanel from MainStackPanel
        MainStackPanel.Children.Remove(ControlsStackPanel);

        // Reorder based on position setting
        if (SettingsManager.Current.TaskbarWidgetControlsPosition == 0)
        {
            // Left: Controls, Image, Info
            MainStackPanel.Children.Insert(0, ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(2, 0, 6, 0); // for some reason margins are weird on left side
        }
        else
        {
            // Right: Image, Info, Controls
            MainStackPanel.Children.Add(ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(8, 0, 0, 0);
        }
    }

    public void SetVerticalMode(bool isVertical)
    {
        var counterRotate = isVertical ? new RotateTransform(-90) : null;

        SongImageBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;

        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
        }
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void ApplyWindowsTheme()
    {
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        var foreground = new SolidColorBrush(isDark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));

        SongTitle.Foreground = foreground;
        SongArtist.Foreground = foreground;
        PreviousButton.Foreground = foreground;
        PlayPauseButton.Foreground = foreground;
        NextButton.Foreground = foreground;
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        SolidColorBrush targetBackgroundBrush;
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        if (isDark)
        { // dark mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
        }
        else
        { // light mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
        }

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mainWindow == null) return;

        // toggle main flyout when clicked
        _mainWindow.ShowMediaFlyout(toggleMode: true, forceShow: true);
    }

    private void MainBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarWidgetMouseWheelVolume) return;

        if (e.Delta > 0)
        {
            AdjustActivePlayerVolume(_mainWindow, volumeUp: true);
            e.Handled = true;
        }
        else if (e.Delta < 0)
        {
            AdjustActivePlayerVolume(_mainWindow, volumeUp: false);
            e.Handled = true;
        }
    }

    internal static bool AdjustActivePlayerVolume(MainWindow? mainWindow, bool volumeUp, float step = 0.02f)
    {
        try
        {
            mainWindow ??= System.Windows.Application.Current.MainWindow as MainWindow;
            var activeSession = mainWindow?.GetActiveMediaSession();
            if (activeSession == null || string.IsNullOrWhiteSpace(activeSession.Id))
                return false;

            string mediaId = activeSession.Id;

            // Extract candidate tokens / process names from mediaId (e.g. "Spotify.exe", "SpotifyAB.SpotifyMusic_...!Spotify", "chrome.exe")
            var tokens = mediaId
                .Split(['.', '_', '!', ' ', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.Equals(t, "com", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(t, "exe", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(t, "app", StringComparison.OrdinalIgnoreCase)
                         && t.Length >= 2)
                .ToList();

            string simpleName = mediaId.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (!tokens.Contains(simpleName, StringComparer.OrdinalIgnoreCase))
                tokens.Add(simpleName);

            var targetPids = new HashSet<int>();
            foreach (var token in tokens)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(token))
                    {
                        targetPids.Add(p.Id);
                    }
                }
                catch { }
            }

            var defaultDevice = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();
            if (defaultDevice == null) return false;

            var sessionManager = defaultDevice.AudioSessionManager;
            var sessions = sessionManager.Sessions;

            bool sessionUpdated = false;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;

                int pid = (int)session.GetProcessID;
                if (pid == 0) continue;

                bool match = targetPids.Contains(pid);

                if (!match)
                {
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        string procName = proc.ProcessName;
                        if (tokens.Any(t => procName.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(procName, StringComparison.OrdinalIgnoreCase)))
                        {
                            match = true;
                        }
                    }
                    catch { }
                }

                if (match)
                {
                    float currentVol = session.SimpleAudioVolume.Volume;
                    float newVol = Math.Clamp(currentVol + (volumeUp ? step : -step), 0f, 1f);
                    session.SimpleAudioVolume.Volume = newVol;
                    if (newVol > 0 && session.SimpleAudioVolume.Mute)
                    {
                        session.SimpleAudioVolume.Mute = false;
                    }
                    sessionUpdated = true;
                }
            }

            return sessionUpdated;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to adjust active player volume");
            return false;
        }
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale, double? maxAvailableLogicalWidth = null)
    {
        // Standard compact width layout (95px text width) to ensure stable footprint and smooth marquee scrolling
        double defaultTextWidth = 95;
        double baseNonTextLogicalWidth = 55; // album art (36) + margins (4 + 6 + 4 + border padding ~5)
        double controlsLogicalWidth = SettingsManager.Current.TaskbarWidgetControlsEnabled ? 90 : 0; // 3 x 28px buttons + margin ~90

        double textWidth = defaultTextWidth;
        double minTextWidth = 30;

        if (maxAvailableLogicalWidth.HasValue)
        {
            double availableForText = maxAvailableLogicalWidth.Value - baseNonTextLogicalWidth - controlsLogicalWidth;
            if (availableForText < defaultTextWidth)
            {
                textWidth = Math.Max(minTextWidth, availableForText);
            }
        }

        double logicalWidth = baseNonTextLogicalWidth + textWidth + controlsLogicalWidth;

        if (Math.Abs(SongTitle.Width - textWidth) > 0.5) SongTitle.Width = textWidth;
        if (Math.Abs(SongArtist.Width - textWidth) > 0.5) SongArtist.Width = textWidth;
        if (Math.Abs(SongInfoStackPanel.Width - textWidth) > 0.5) SongInfoStackPanel.Width = textWidth;

        double logicalHeight = 40; // default height

        return (logicalWidth, logicalHeight);
    }

    public void UpdateUi(string title, string artist, ImageSource? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;

                Visibility = Visibility.Visible;
            });
            return;
        }

        _isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            _isPaused = true;
        }

        // adjust UI based on available controls and song metadata
        Dispatcher.Invoke(() =>
        {
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled && playbackControls != null)
            {
                PreviousButton.IsHitTestVisible = playbackControls.IsPreviousEnabled;
                PlayPauseButton.IsHitTestVisible = playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled;
                NextButton.IsHitTestVisible = playbackControls.IsNextEnabled;

                PreviousButton.Opacity = playbackControls.IsPreviousEnabled ? 1 : 0.5;
                PlayPauseButton.Opacity = (playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled) ? 1 : 0.5;
                NextButton.Opacity = playbackControls.IsNextEnabled ? 1 : 0.5;
            }
            else
            {
                PreviousButton.IsHitTestVisible = false;
                PlayPauseButton.IsHitTestVisible = false;
                NextButton.IsHitTestVisible = false;

                PreviousButton.Opacity = 0.5;
                NextButton.Opacity = 0.5;
                PlayPauseButton.Opacity = 0.5;
            }

            if (SongTitle.Text != title || SongArtist.Text != artist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }
            }

            SongTitle.Text = !string.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !string.IsNullOrEmpty(artist) ? artist : "-";

            // Update tooltip with song info
            SongInfoStackPanel.ToolTip = string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(title) ? title : string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            // change color of icon
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0 ?
                BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            SongImagePlaceholder.Foreground = brush;

            if (icon != null)
            {
                if (_isPaused && SettingsManager.Current.TaskbarWidgetShowPauseOverlay && !SettingsManager.Current.TaskbarWidgetControlsEnabled)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = !string.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed; // hide artist if it's not available
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            ControlsStackPanel.Visibility = SettingsManager.Current.TaskbarWidgetControlsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            Visibility = Visibility.Visible;
        });
    }

    public void UpdateThumbnail(ImageSource? icon)
    {
        Dispatcher.Invoke(() =>
        {
            if (icon != null)
            {
                if (_isPaused && SettingsManager.Current.TaskbarWidgetShowPauseOverlay && !SettingsManager.Current.TaskbarWidgetControlsEnabled)
                {
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2);
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0 ?
                BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            SongImagePlaceholder.Foreground = brush;
        });
    }

    private void AnimateEntrance()
    {
        try
        {
            int msDuration = MainWindow.GetDuration();
            if (msDuration <= 0) return;

            int animMs = Math.Min(msDuration, 200);

            // opacity and subtle left to right animation for SongInfoStackPanel (starts at 0.4 so text is immediately visible)
            DoubleAnimation opacityAnimation = new()
            {
                From = 0.4,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(animMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new()
            {
                From = -6,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(animMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform = new();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
    }

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TryTogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var focusedSession = _mainWindow.GetActiveMediaSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipNextAsync();
    }
}