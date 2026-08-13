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

        var songInfo = TryGetMediaProperties(currentActiveSession.ControlSession);
        if (songInfo == null)
            return;

        var playbackInfo = currentActiveSession.ControlSession.GetPlaybackInfo();

        string check = songInfo.Title + songInfo.Artist + playbackInfo.PlaybackStatus;
        int checkThumbnail = BitmapHelper.GetStableThumbnailHash(songInfo.Thumbnail);
        bool onlyThumbnailChanged = false;
        if (previousMediaProperty == check)
        {
            onlyThumbnailChanged = true;
            if (previousMediaPropertyThumbnail == checkThumbnail)
                return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        BitmapHelper.GetDominantColors(1);

        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);

        PauseOtherMediaSessionsIfNeeded(mediaSession);

        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning())
        {
            void createNewNextUpWindow()
            {
                Dispatcher.Invoke(() =>
                {
                    if (nextUpWindow == null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        nextUpWindow = new NextUpWindow(songInfo.Title, songInfo.Artist, thumbnail);
                        currentTitle = songInfo.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null;
                    }
                });
            }

            if (nextUpWindow == null && !IsVisible && songInfo.Thumbnail != null && currentTitle != songInfo.Title)
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
            else if (nextUpWindow != null && songInfo.Thumbnail != null)
            {
                Dispatcher.Invoke(() => nextUpWindow?.UpdateThumbnail(thumbnail));
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
            var tbThumbnail = BitmapHelper.GetThumbnail(tbSongInfo.Thumbnail);
            BitmapHelper.GetDominantColors(1);
            var tbPlayback = focusedSession.ControlSession.GetPlaybackInfo();
            taskbarWindow?.UpdateUi(tbSongInfo.Title, tbSongInfo.Artist, tbThumbnail, tbPlayback?.PlaybackStatus, tbPlayback?.Controls);
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
