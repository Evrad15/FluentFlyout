// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Utils;
using MicaWPF.Core.Extensions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private void UpdateMediaFlyoutCloseButtonVisibility()
    {
        bool always = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
        bool compact = SettingsManager.Current.CompactLayout;
        MediaFlyoutCloseButton.Visibility = always && !compact ? Visibility.Visible : Visibility.Collapsed;
        ControlClose.Visibility           = always &&  compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUI(MediaSession mediaSession)
    {
        if (_layout            != SettingsManager.Current.CompactLayout          ||
            _shuffleEnabled    != SettingsManager.Current.ShuffleEnabled         ||
            _repeatEnabled     != SettingsManager.Current.RepeatEnabled          ||
            _playerInfoEnabled != SettingsManager.Current.PlayerInfoEnabled      ||
            _centerTitleArtist != SettingsManager.Current.CenterTitleArtist      ||
            _seekBarEnabled    != SettingsManager.Current.SeekbarEnabled         ||
            _alwaysDisplay     != SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            UpdateUILayout();

        // sometimes mediaSession.ControlSession can be null
        if (mediaSession.ControlSession == null)
            return;

        var controlSession = mediaSession.ControlSession;

        Dispatcher.Invoke(() =>
        {
            UpdateMediaFlyoutCloseButtonVisibility();
            this.EnableBackdrop();

            if (mediaSession == null)
            {
                SongTitle.Text = "No media playing";
                SongArtist.Text = string.Empty;
                SongImage.ImageSource = null;
                BackgroundGrid.Background = null;
                SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Stop16;
                ControlPlayPause.IsEnabled = false;
                ControlPlayPause.Opacity = 0.35;
                ControlBack.IsEnabled = ControlForward.IsEnabled = false;
                ControlBack.Opacity = ControlForward.Opacity = 0.35;
                SongInfoStackPanel.ToolTip = string.Empty;
                return;
            }

            var mediaProperties = controlSession.GetPlaybackInfo();
            if (mediaProperties != null)
            {
                SymbolPlayPause.Symbol = mediaProperties.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    ? Wpf.Ui.Controls.SymbolRegular.Pause16
                    : Wpf.Ui.Controls.SymbolRegular.Play16;

                ControlPlayPause.IsEnabled = mediaProperties.Controls.IsPlayEnabled || mediaProperties.Controls.IsPauseEnabled;
                ControlPlayPause.Opacity   = ControlPlayPause.IsEnabled ? 1 : 0.35;

                ControlBack.IsEnabled = ControlForward.IsEnabled = mediaProperties.Controls.IsNextEnabled;
                ControlBack.Opacity   = ControlForward.Opacity   = mediaProperties.Controls.IsNextEnabled ? 1 : 0.35;

                if (SettingsManager.Current.RepeatEnabled && !SettingsManager.Current.CompactLayout)
                {
                    ControlRepeat.Visibility = Visibility.Visible;
                    ControlRepeat.IsEnabled  = mediaProperties.Controls.IsRepeatEnabled;
                    ControlRepeat.Opacity    = mediaProperties.Controls.IsRepeatEnabled ? 1 : 0.35;
                    (SymbolRepeat.Symbol, SymbolRepeat.Opacity) = mediaProperties.AutoRepeatMode switch
                    {
                        global::Windows.Media.MediaPlaybackAutoRepeatMode.List  => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24,    1.0),
                        global::Windows.Media.MediaPlaybackAutoRepeatMode.Track => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124,       1.0),
                        _                                                        => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24, 0.5),
                    };
                }
                else ControlRepeat.Visibility = Visibility.Collapsed;

                if (SettingsManager.Current.ShuffleEnabled && !SettingsManager.Current.CompactLayout)
                {
                    ControlShuffle.Visibility = Visibility.Visible;
                    ControlShuffle.IsEnabled  = mediaProperties.Controls.IsShuffleEnabled;
                    ControlShuffle.Opacity    = mediaProperties.Controls.IsShuffleEnabled ? 1 : 0.35;
                    (SymbolShuffle.Symbol, SymbolShuffle.Opacity) = mediaProperties.IsShuffleActive == true
                        ? (Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24,    1.0)
                        : (Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24, 0.5);
                }
                else ControlShuffle.Visibility = Visibility.Collapsed;

                if (SettingsManager.Current.PlayerInfoEnabled && !SettingsManager.Current.CompactLayout)
                {
                    MediaIdStackPanel.Visibility = Visibility.Visible;
                    (string title, ImageSource? icon) = MediaPlayerData.GetAndCacheMediaPlayerData(mediaSession.Id);
                    MediaId.Text = title;
                    if (icon != null)
                    {
                        MediaIdIcon.Source     = icon;
                        MediaIdIcon.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        MediaIdIcon.Visibility = Visibility.Collapsed;
                    }
                }
                else MediaIdStackPanel.Visibility = Visibility.Collapsed;

                BackgroundImageStyle1.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 1 ? Visibility.Visible : Visibility.Collapsed;
                BackgroundImageStyle2.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 2 ? Visibility.Visible : Visibility.Collapsed;
                BackgroundImageStyle3.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 3 ? Visibility.Visible : Visibility.Collapsed;

                if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled != _acrylicEnabled
                    || SettingsManager.Current.AppTheme != _themeOption)
                {
                    _acrylicEnabled = SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled;
                    ToggleBlur();
                }
            }

            var songInfo = TryGetMediaProperties(controlSession);
            if (songInfo == null)
                return;

            SongTitle.Text  = songInfo.Title;
            SongArtist.Text = songInfo.Artist;
            var image = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
            SongImage.ImageSource = image;

            // Apply dominant-color gradient with animated crossfade (fork feature)
            var dominantColors = BitmapHelper.GetDominantColors(2, 15, true);
            if (dominantColors.Count > 0)
            {
                ControlPlayPause.Background = dominantColors[0];

                Color colorStart = dominantColors[0].Color;
                Color colorEnd   = dominantColors.Count > 1
                    ? dominantColors[1].Color
                    : Color.FromArgb(0, colorStart.R, colorStart.G, colorStart.B);

                var newBrush = new LinearGradientBrush(colorStart, colorEnd, new Point(0, 0), new Point(1, 1))
                {
                    Opacity = 0.85
                };

                ApplyGradientWithTransition(newBrush);
            }
            else
            {
                BackgroundGrid.Background = null;
            }

            // tooltip
            SongInfoStackPanel.ToolTip = string.Empty;
            if (!string.IsNullOrEmpty(songInfo.Title))  SongInfoStackPanel.ToolTip += songInfo.Title;
            if (!string.IsNullOrEmpty(songInfo.Artist)) SongInfoStackPanel.ToolTip += "\n\n" + songInfo.Artist;

            // background blurred image
            if (SettingsManager.Current.MediaFlyoutBackgroundBlur != 0)
            {
                var croppedImage = BitmapHelper.CropToSquare(image);
                switch (SettingsManager.Current.MediaFlyoutBackgroundBlur)
                {
                    case 1: BackgroundImageStyle1.Source = croppedImage; break;
                    case 2: BackgroundImageStyle2.Source = croppedImage; break;
                    case 3: BackgroundImageStyle3.Source = croppedImage; break;
                }
            }

            SongImagePlaceholder.Visibility = SongImage.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;

            if (_seekBarEnabled)
            {
                var timeline = controlSession.GetTimelineProperties();
                bool mediaSessionSupportsSeekbar = timeline.MaxSeekTime.TotalSeconds >= 1.0;

                if (_mediaSessionSupportsSeekbar != mediaSessionSupportsSeekbar)
                {
                    _mediaSessionSupportsSeekbar = mediaSessionSupportsSeekbar;
                    UpdateUILayout();
                    _isHiding = true;
                    ShowMediaFlyout();
                }

                if (mediaSessionSupportsSeekbar)
                {
                    Seekbar.Maximum = timeline.MaxSeekTime.TotalSeconds;
                    SeekbarMaxDuration.Text = timeline.MaxSeekTime.ToString(
                        timeline.MaxSeekTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                }
            }
        });
    }

    /// <summary>
    /// Applies a new gradient brush to BackgroundGrid with a smooth crossfade transition.
    /// </summary>
    private void ApplyGradientWithTransition(LinearGradientBrush newBrush)
    {
        // If no previous background or animation is disabled, apply immediately
        if (BackgroundGrid.Background == null || GetDuration() == 0)
        {
            BackgroundGrid.Background = newBrush;
            return;
        }

        // Snapshot old background onto the overlay layer and fade it out
        BackgroundGridOld.Background = BackgroundGrid.Background;
        BackgroundGridOld.Opacity = 1;
        BackgroundGrid.Background = newBrush;
        BackgroundGrid.Opacity = 0;

        int fadeDuration = Math.Max(200, GetDuration());
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(fadeDuration)) { EasingFunction = ease };
        var fadeIn  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeDuration)) { EasingFunction = ease };

        BackgroundGridOld.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        BackgroundGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void UpdateUILayout()
    {
        Dispatcher.Invoke(() =>
        {
            int extraWidth = SettingsManager.Current.RepeatEnabled   ? 36 : 0;
            extraWidth    += SettingsManager.Current.ShuffleEnabled  ? 36 : 0;
            extraWidth    += SettingsManager.Current.PlayerInfoEnabled ? 72 : 0;
            extraWidth     = Math.Max(extraWidth, 72); // minimum width

            int extraHeight = SettingsManager.Current.SeekbarEnabled && _mediaSessionSupportsSeekbar ? 36 : 0;

            if (SettingsManager.Current.CompactLayout)
            {
                Height = 60 + extraHeight;
                Width  = 400;
                BodyStackPanel.Orientation = Orientation.Horizontal;
                BodyStackPanel.Width = 300;
                ControlsStackPanel.Margin = new Thickness(0);
                ControlsStackPanel.Width  = 104;
                MediaIdStackPanel.Visibility = Visibility.Collapsed;
                SongImageBorder.Margin = new Thickness(0);
                SongImageBorder.Height = 36;
                SongInfoStackPanel.Margin = new Thickness(8, 0, 0, 0);
                SongInfoStackPanel.Width  = 182;
                if (SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    SongInfoStackPanel.Width  -= 36;
                    ControlsStackPanel.Width  += 44;
                }
            }
            else
            {
                Height = 112 + extraHeight;
                Width  = 310 - 72 + extraWidth;
                BodyStackPanel.Orientation = Orientation.Vertical;
                BodyStackPanel.Width = 194 - 72 + extraWidth;
                ControlsStackPanel.Margin = Margin = new Thickness(12, 8, 0, 0);
                ControlsStackPanel.Width  = 184 - 72 + extraWidth;
                MediaIdStackPanel.Visibility = Visibility.Visible;
                SongImageBorder.Margin = new Thickness(6);
                SongImageBorder.Height = 78;
                SongInfoStackPanel.Margin = new Thickness(12, 0, 0, 0);
                SongInfoStackPanel.Width  = 182 - 72 + extraWidth;
            }

            SongTitle.HorizontalAlignment  = SettingsManager.Current.CenterTitleArtist ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            SongArtist.HorizontalAlignment = SettingsManager.Current.CenterTitleArtist ? HorizontalAlignment.Center : HorizontalAlignment.Left;

            SeekbarWrapper.Visibility = SettingsManager.Current.SeekbarEnabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _layout            = SettingsManager.Current.CompactLayout;
        _repeatEnabled     = SettingsManager.Current.RepeatEnabled;
        _shuffleEnabled    = SettingsManager.Current.ShuffleEnabled;
        _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
        _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
        _seekBarEnabled    = SettingsManager.Current.SeekbarEnabled;
        _alwaysDisplay     = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;
        await activeSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;
        await activeSession.ControlSession.TryTogglePlayPauseAsync();
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;
        await activeSession.ControlSession.TrySkipNextAsync();
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        var mode = activeSession.ControlSession.GetPlaybackInfo().AutoRepeatMode;
        var (newSymbol, nextMode) = mode switch
        {
            global::Windows.Media.MediaPlaybackAutoRepeatMode.None  => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24,   global::Windows.Media.MediaPlaybackAutoRepeatMode.List),
            global::Windows.Media.MediaPlaybackAutoRepeatMode.List  => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124,     global::Windows.Media.MediaPlaybackAutoRepeatMode.Track),
            _                                                        => (Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24, global::Windows.Media.MediaPlaybackAutoRepeatMode.None),
        };
        SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = newSymbol);
        await activeSession.ControlSession.TryChangeAutoRepeatModeAsync(nextMode);
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null) return;

        bool isActive = activeSession.ControlSession.GetPlaybackInfo().IsShuffleActive == true;
        SymbolShuffle.Dispatcher.Invoke(() =>
            SymbolShuffle.Symbol = isActive
                ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24
                : Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24);
        await activeSession.ControlSession.TryChangeShuffleActiveAsync(!isActive);
    }
}
