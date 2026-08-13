// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media.Animation;
using static FluentFlyout.Classes.NativeMethods;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    /// <summary>
    /// Returns the animation duration in milliseconds based on the user's speed setting.
    /// </summary>
    public static int GetDuration()
    {
        return SettingsManager.Current.FlyoutAnimationSpeed switch
        {
            0 => 0,   // off
            1 => 150, // 0.5x
            2 => 300, // 1x
            3 => 450, // 1.5x
            4 => 600, // 2x
            _ => 900  // 3x
        };
    }

    /// <summary>
    /// Returns the easing function based on the user's easing style setting.
    /// </summary>
    public EasingFunctionBase GetEasingStyle(bool easeOut)
    {
        EasingMode easingMode = easeOut ? EasingMode.EaseOut : EasingMode.EaseIn;
        return SettingsManager.Current.FlyoutAnimationEasingStyle switch
        {
            // 0 is linear, null — handled by callers
            1 => new SineEase { EasingMode = easingMode },
            2 => new QuadraticEase { EasingMode = easingMode },
            _ => new CubicEase { EasingMode = easingMode },
        };
    }

    private MonitorUtil.MonitorInfo GetSelectedMonitor()
    {
        return MonitorUtil.GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
    }

    /// <summary>
    /// Computes the final resting position (left, top) for a window based on the current
    /// position setting and the selected monitor's work area.
    /// </summary>
    private (double left, double top) GetFinalPosition(Rect windowRect, Rect workArea)
    {
        int position = SettingsManager.Current.Position;
        double left = position switch
        {
            (int)FlyoutPosition.BottomLeft  or (int)FlyoutPosition.TopLeft  => workArea.Left + 16,
            (int)FlyoutPosition.BottomRight or (int)FlyoutPosition.TopRight => workArea.Left + workArea.Width - windowRect.Width - 16,
            _ => workArea.Left + workArea.Width / 2 - windowRect.Width / 2
        };
        double top = position switch
        {
            (int)FlyoutPosition.BottomLeft  or (int)FlyoutPosition.BottomRight => workArea.Top + workArea.Height - windowRect.Height - 16,
            (int)FlyoutPosition.BottomCenter => workArea.Top + workArea.Height - windowRect.Height - 80,
            _ => workArea.Top + 16
        };
        return (left, top);
    }

    public void OpenAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null, MicaWindow? aboveReference = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;

        // prevent flickering
        WindowHelper.SetVisibility(window, false);

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, workArea.Left, workArea.Top);
        var windowRect = WindowHelper.GetPlacement(window);

        double window_left;
        bool noAnimation = SettingsManager.Current.FlyoutAnimationSpeed == 0;

        // If a reference window is provided and visible, position the window next to it
        if (aboveReference != null && aboveReference.IsVisible)
        {
            double refWidth  = aboveReference.Width  * monitor.dpiX / 96.0;
            double refHeight = aboveReference.Height * monitor.dpiY / 96.0;
            var refRect = new Rect(0, 0, refWidth, refHeight);
            var (refLeft, refTop) = GetFinalPosition(refRect, workArea);

            window_left = refLeft + refWidth / 2 - windowRect.Width / 2;
            bool isTop = SettingsManager.Current.Position is
                (int)FlyoutPosition.TopLeft or (int)FlyoutPosition.TopCenter or (int)FlyoutPosition.TopRight;

            double aboveTop = isTop ? refTop + refHeight + 8 : refTop - windowRect.Height - 8;

            moveAnimation.To = aboveTop;
            moveAnimation.From = noAnimation ? aboveTop : (isTop ? aboveTop - 20 : aboveTop + 20);
        }
        else if (!alwaysBottom)
        {
            // Use GetFinalPosition for all 6 positions — no more duplicated else if chain
            _position = SettingsManager.Current.Position;
            var (finalLeft, finalTop) = GetFinalPosition(
                new Rect(0, 0, windowRect.Width, windowRect.Height), workArea);

            bool isTop = _position is
                (int)FlyoutPosition.TopLeft or (int)FlyoutPosition.TopCenter or (int)FlyoutPosition.TopRight;

            // BottomCenter (position 1) has a special offset to clear the taskbar
            double offset = _position == (int)FlyoutPosition.BottomCenter ? 0 : (isTop ? -20 : 4);

            window_left = finalLeft;
            moveAnimation.To = finalTop;
            moveAnimation.From = noAnimation ? finalTop : finalTop + offset;
        }
        else
        {
            // alwaysBottom: bottom-center of screen
            window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
            moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
            moveAnimation.From = noAnimation
                ? moveAnimation.To
                : workArea.Top + workArea.Height - windowRect.Height + 4;
        }

        // Set initial position in raw coordinates
        WindowHelper.SetPosition(window, window_left, moveAnimation.From!.Value);

        // Convert to DPI-aware coordinates for Window.Top
        moveAnimation.From *= 96.0 / monitor.dpiY;
        moveAnimation.To   *= 96.0 / monitor.dpiY;

        int msDuration = GetDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        if (!noAnimation) opacityAnimation.From = 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0)
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = GetEasingStyle(true);

        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
        WindowHelper.SetVisibility(window, true);
        WindowHelper.SetTopmost(window);
    }

    public void CloseAnimation(MicaWindow window, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers.Actions[0] as BeginStoryboard;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;
        var windowRect = WindowHelper.GetPlacement(window);

        // Use the window's actual current position as the animation start
        moveAnimation.From = windowRect.Top;

        if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
        {
            bool isTopHalf = windowRect.Top + windowRect.Height / 2 < workArea.Top + workArea.Height / 2;
            moveAnimation.To = windowRect.Top + (isTopHalf ? -20 : 20);
        }

        moveAnimation.From *= 96.0 / monitor.dpiY;
        if (moveAnimation.To != null)
            moveAnimation.To *= 96.0 / monitor.dpiY;

        int msDuration = GetDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        opacityAnimation.From = 1;
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.To = 0;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0)
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else
            moveAnimation.EasingFunction = opacityAnimation.EasingFunction = GetEasingStyle(false);

        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
    }
}
