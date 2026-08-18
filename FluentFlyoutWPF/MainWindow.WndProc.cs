// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private async Task<bool> WaitForExplorerReadyAsync(int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero &&
                GetWindowRect(taskbar, out NativeMethods.RECT rect) &&
                rect.Right > rect.Left &&
                rect.Bottom > rect.Top)
                return true;

            await Task.Delay(200);
        }
        return false;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_SHELLHOOK && wParam == HSHELL_APPCOMMAND)
        {
            int highWord = (int)(lParam >> 16);
            int cmd    = highWord & 0x0FFF;
            int device = highWord & 0xF000;

            bool isMediaCommand = cmd switch
            {
                APPCOMMAND_MEDIA_PLAY_PAUSE    => true,
                APPCOMMAND_MEDIA_NEXTTRACK     => true,
                APPCOMMAND_MEDIA_PREVIOUSTRACK => true,
                APPCOMMAND_MEDIA_STOP          => true,
                _                              => false
            };

            bool isVolumeCommand = false;
            if (!isMediaCommand && !SettingsManager.Current.MediaFlyoutVolumeKeysExcluded)
            {
                isVolumeCommand = cmd switch
                {
                    APPCOMMAND_VOLUME_MUTE => true,
                    APPCOMMAND_VOLUME_DOWN => true,
                    APPCOMMAND_VOLUME_UP   => true,
                    _                      => false
                };
            }

            if ((!isMediaCommand && !isVolumeCommand) || device != FAPPCOMMAND_KEY)
                return 0;

            bool result = TryShowMediaFlyoutDebounced();
            if (result) handled = true;
        }
        else if (msg == WM_SHELLHOOK)
        {
            int code = (int)wParam;
            if (code == HSHELL_WINDOWCREATED || code == HSHELL_WINDOWDESTROYED || code == HSHELL_REDRAW || code == HSHELL_WINDOWACTIVATED || code == HSHELL_RUDEAPPACTIVATED)
            {
                TriggerTaskbarPositionUpdateDebounced();
            }
        }
        else if (msg == WM_TASKBARCREATED)
        {
            Logger.Warn("Explorer restart detected (TaskbarCreated)");
            ExplorerRestarting = true;

            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (await WaitForExplorerReadyAsync())
                    {
                        ExplorerRestarting = false;
                        Logger.Info("Explorer stabilized, resuming taskbar integration");
                        RecreateTrayIconSafely();
                    }
                    else
                    {
                        Logger.Warn("Explorer did not stabilize within timeout; keeping integration disabled");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Explorer recovery failed");
                }
            }, DispatcherPriority.Background);

            handled = true;
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            if (lParam == IntPtr.Zero) return 0;

            string? changedSetting = Marshal.PtrToStringUni(lParam);
            if (changedSetting != "ImmersiveColorSet" && changedSetting != "WindowsThemeElement")
                return 0;

            Logger.Info($"System setting changed: {changedSetting}");

            try
            {
                ThemeManager.UpdateTaskbarWidget();
                WindowBlurHelper.AdjustBlurOpacityForAllWindows(SettingsManager.Current.AcrylicBlurOpacity);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply theme changes to taskbar widgets or Acrylic windows");
            }
        }

        return 0;
    }

    private void RecreateTrayIconSafely()
    {
        try
        {
            nIcon.Visibility = Visibility.Collapsed;
            if (!SettingsManager.Current.NIconHide)
            {
                nIcon.Visibility = Visibility.Visible;
                nIcon.Register();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate tray icon safely");
        }
    }

    private long _lastTaskbarUpdateTime = 0;

    private void TriggerTaskbarPositionUpdateDebounced()
    {
        long currentTime = Environment.TickCount64;
        if ((currentTime - _lastTaskbarUpdateTime) < 250) // 250ms debounce
            return;

        _lastTaskbarUpdateTime = currentTime;
        taskbarWindow?.Dispatcher.BeginInvoke(() =>
        {
            taskbarWindow?.UpdatePosition();
        }, DispatcherPriority.Background);
    }
}
