// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Windows.Media.Control;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private void Seekbar_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        _isDragging = true;

        Slider slider = (Slider)sender;
        System.Windows.Point clickPosition = e.GetPosition(slider);
        double thumbWidth = slider.Template.FindName("Thumb", slider) is Thumb thumb ? thumb.ActualWidth : 0;
        double ratio = (clickPosition.X - thumbWidth / 2) / (slider.ActualWidth - thumbWidth);
        ratio = Math.Max(0, Math.Min(1, ratio));
        double targetSeconds = ratio * slider.Maximum;
        // Bug: if the position is 0, it will cause the position to not change when changing playback position
        if (targetSeconds == 0) targetSeconds = 1;
        Dispatcher.Invoke(() => Seekbar.Value = TimeSpan.FromSeconds(targetSeconds).TotalSeconds);
    }

    private async void Seekbar_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (GetActiveMediaSession() is { } session)
        {
            var seekPosition = TimeSpan.FromSeconds(Seekbar.Value);
            if (seekPosition == TimeSpan.Zero) seekPosition = TimeSpan.FromSeconds(1);
            await session.ControlSession.TryChangePlaybackPositionAsync(seekPosition.Ticks);
        }
        _isDragging = false;
    }

    private void Seekbar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isDragging) return;
        var timespan = TimeSpan.FromSeconds(e.NewValue);
        Dispatcher.Invoke(() =>
            SeekbarCurrentDuration.Text = timespan.ToString(timespan.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss"));
    }

    private void SeekbarUpdateUi(object? sender)
    {
        if (DateTime.Now.Subtract(_lastSelfUpdateTimestamp).TotalSeconds < 1) return;
        if (!_seekBarEnabled || Visibility != Visibility.Visible || _isDragging) return;
        if (GetActiveMediaSession() is not { } session) return;

        var timeline = session.ControlSession.GetTimelineProperties();
        var pos = timeline.Position + (DateTime.Now - timeline.LastUpdatedTime.DateTime);
        if (pos > timeline.EndTime)
        {
            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        UpdateSeekbarCurrentDuration(pos);
    }

    private void UpdateSeekbarCurrentDuration(TimeSpan pos)
    {
        Dispatcher.Invoke(() =>
        {
            Seekbar.Value = pos.TotalSeconds;
            SeekbarCurrentDuration.Text = pos.ToString(pos.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        });
    }

    private void HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        if (status == null) return;

        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            if (_isActive) return;
            _isActive = true;
            _positionTimer.Change(0, _seekbarUpdateInterval);
        }
        else
        {
            if (!_isActive) return;
            _isActive = false;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }
}
