// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Windows;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private int previousMediaPropertyThumbnail = 0;

    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
        // sometimes mediaSession.ControlSession can be null
        if (mediaSession.ControlSession == null)
            return;

#if DEBUG
        Logger.Debug("Media property changed: " + mediaProperties.Title + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif
        var currentActiveSession = GetActiveMediaSession();
        if (currentActiveSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        // Use the passed mediaProperties directly if the active session is the one that changed to avoid redundant blocking COM calls
        var songInfo = (currentActiveSession.Id == mediaSession.Id)
            ? mediaProperties
            : TryGetMediaProperties(currentActiveSession.ControlSession);

        if (songInfo == null)
            return;

        var playbackInfo = currentActiveSession.ControlSession.GetPlaybackInfo();
        string title = songInfo.Title;
        string artist = FormatArtists(songInfo);

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        ImageSource? displayImage = thumbnail;
        if (displayImage == null && mediaSession != null)
        {
            (_, ImageSource? appIcon) = MediaPlayerData.GetAndCacheMediaPlayerData(mediaSession.Id);
            displayImage = appIcon;
        }

        string check = title + artist + playbackInfo.PlaybackStatus;
        int checkThumbnail = BitmapHelper.CurrentHashCode;
        bool onlyThumbnailChanged = false;
        if (previousMediaProperty == check)
        {
            onlyThumbnailChanged = true;
            if (previousMediaPropertyThumbnail == checkThumbnail)
                return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        BitmapHelper.GetDominantColors(1);

        taskbarWindow?.UpdateUi(title, artist, displayImage, playbackInfo.PlaybackStatus, playbackInfo.Controls);

        PauseOtherMediaSessionsIfNeeded(mediaSession);

        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning())
        {
            void createNewNextUpWindow()
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        nextUpWindow = new NextUpWindow(title, artist, displayImage);
                        currentTitle = title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null;
                    }
                });
            }

            if (nextUpWindow == null && !IsVisible && currentTitle != title)
            {
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null && !onlyThumbnailChanged)
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow != null)
                    {
                        WindowHelper.SetVisibility(nextUpWindow, false);
                        nextUpWindow.Close();
                    }
                });
                createNewNextUpWindow();
            }
            else if (nextUpWindow != null)
            {
                Dispatcher.Invoke(() => nextUpWindow?.UpdateThumbnail(displayImage));
            }
        }

        if (IsVisible)
        {
            var focusedSession = GetActiveMediaSession();
            if (focusedSession != null)
            {
                HandlePlayBackState(focusedSession.ControlSession.GetPlaybackInfo()?.PlaybackStatus);
                UpdateUI(focusedSession);
            }
        }
    }

    private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
#if DEBUG
        Logger.Debug("Playback state changed: " + mediaSession.Id + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif
        PauseOtherMediaSessionsIfNeeded(mediaSession);

        var focusedSession = GetActiveMediaSession();
        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var tbSongInfo = TryGetMediaProperties(focusedSession.ControlSession);
        if (tbSongInfo != null)
        {
            string title = tbSongInfo.Title;
            string artist = FormatArtists(tbSongInfo);
            var tbThumbnail = BitmapHelper.GetThumbnail(tbSongInfo.Thumbnail);
            ImageSource? tbDisplayImage = tbThumbnail;
            if (tbDisplayImage == null && focusedSession != null)
            {
                (_, ImageSource? appIcon) = MediaPlayerData.GetAndCacheMediaPlayerData(focusedSession.Id);
                tbDisplayImage = appIcon;
            }
            BitmapHelper.GetDominantColors(1);
            var tbPlayback = focusedSession.ControlSession.GetPlaybackInfo();
            taskbarWindow?.UpdateUi(title, artist, tbDisplayImage, tbPlayback?.PlaybackStatus, tbPlayback?.Controls);
        }

        if (IsVisible)
        {
            UpdateUI(focusedSession);
            HandlePlayBackState(playbackInfo?.PlaybackStatus);
        }
    }

    private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        _lastSelfUpdateTimestamp = DateTime.Now;

        if (GetActiveMediaSession() is not { } session || session.Id != mediaSession.Id) return;

        if (_seekBarEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive || _isDragging) return;
                UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
                HandlePlayBackState(session.ControlSession.GetPlaybackInfo().PlaybackStatus);
            });
        }
    }

    private void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
    {
#if DEBUG
        Logger.Debug("Session closed: " + (mediaSession.Id).ToString());
#endif
        UpdateTaskbar();
    }
}
