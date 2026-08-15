// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF;

public partial class MainWindow
{
    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        if (curModule == null)
        {
            Logger.Warn("Failed to set keyboard hook - FluentFlyout will now rely on WndProc only");
            return IntPtr.Zero;
        }
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_KEYUP))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            bool mediaKeysPressed = vkCode == VK_MEDIA_PLAY_PAUSE
                                 || vkCode == VK_MEDIA_NEXT_TRACK
                                 || vkCode == VK_MEDIA_PREV_TRACK
                                 || vkCode == VK_MEDIA_STOP;

            bool volumeKeysPressed = vkCode == VK_VOLUME_MUTE
                                  || vkCode == VK_VOLUME_DOWN
                                  || vkCode == VK_VOLUME_UP;

            if (mediaKeysPressed || volumeKeysPressed)
            {
                bool result = false;
                if (mediaKeysPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeysPressed))
                    result = TryShowMediaFlyoutDebounced();

                if (SettingsManager.Current.VolumeControlEnabled)
                {
                    volumeMixerWindow?.ViewModel.SyncMasterFromDevice();
                    volumeMixerWindow?.ShowFlyout();
                }

                if (!result)
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (SettingsManager.Current.LockKeysEnabled
                && !FullscreenDetector.IsFullscreenApplicationRunning()
                && wParam == WM_KEYUP)
            {
                if (vkCode == VK_CAPS_LOCK && SettingsManager.Current.LockKeysCapsEnabled)
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_CapsLock").ToString(), Keyboard.IsKeyToggled(Key.CapsLock));
                }
                else if (vkCode == VK_NUM_LOCK && SettingsManager.Current.LockKeysNumEnabled)
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_NumLock").ToString(), Keyboard.IsKeyToggled(Key.NumLock));
                }
                else if (vkCode == VK_SCROLL_LOCK && SettingsManager.Current.LockKeysScrollEnabled)
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout(FindResource("LockWindow_ScrollLock").ToString(), Keyboard.IsKeyToggled(Key.Scroll));
                }
                else if (vkCode == VK_INSERT && SettingsManager.Current.LockKeysInsertEnabled)
                {
                    lockWindow ??= new LockWindow();
                    lockWindow.ShowLockFlyout("Insert", Keyboard.IsKeyToggled(Key.Insert));
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // show the media flyout with debounce to prevent hangs with rapid key presses
    private bool TryShowMediaFlyoutDebounced()
    {
        long currentTime = Environment.TickCount64;
        if ((currentTime - _lastFlyoutTime) < 500) // 500ms debounce
            return false;

        _lastFlyoutTime = currentTime;
        ShowMediaFlyout();
        return true;
    }

    public async void ShowMediaFlyout(bool toggleMode = false, bool forceShow = false)
    {
        var activeSession = GetActiveMediaSession();
        if (activeSession == null ||
            (!forceShow && !SettingsManager.Current.MediaFlyoutEnabled) ||
            FullscreenDetector.IsFullscreenApplicationRunning())
            return;

        // If in toggle mode and flyout is visible, close it
        if (toggleMode && Visibility == Visibility.Visible && !_isHiding)
        {
            CloseAnimation(this);
            _isHiding = true;
            cts.Cancel();
            await Task.Delay(GetDuration());
            if (_isHiding)
            {
                Hide();
                if (_seekBarEnabled)
                    HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            }
            return;
        }

        UpdateUI(activeSession);
        if (_seekBarEnabled)
            HandlePlayBackState(activeSession.ControlSession.GetPlaybackInfo().PlaybackStatus);

        if (nextUpWindow != null)
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        if (_isHiding)
        {
            _isHiding = false;
            OpenAnimation(this);
        }

        cts.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Visibility = Visibility.Visible;
        WindowHelper.SetTopmost(this);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);

                bool mouseOverMedia  = WindowHelper.IsMouseOverWindow(this);
                bool mouseOverVolume = SettingsManager.Current.VolumeControlAboveMediaFlyout
                    && SettingsManager.Current.VolumeControlEnabled
                    && volumeMixerWindow != null
                    && volumeMixerWindow.IsVisible
                    && WindowHelper.IsMouseOverWindow(volumeMixerWindow);

                if (!mouseOverMedia && !mouseOverVolume && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    await Task.Delay(SettingsManager.Current.Duration, token);

                    mouseOverMedia  = WindowHelper.IsMouseOverWindow(this);
                    mouseOverVolume = SettingsManager.Current.VolumeControlAboveMediaFlyout
                        && SettingsManager.Current.VolumeControlEnabled
                        && volumeMixerWindow != null
                        && volumeMixerWindow.IsVisible
                        && WindowHelper.IsMouseOverWindow(volumeMixerWindow);

                    if (!mouseOverMedia && !mouseOverVolume)
                    {
                        CloseAnimation(this);
                        _isHiding = true;
                        await Task.Delay(GetDuration());
                        if (!_isHiding) return;
                        Hide();
                        if (_seekBarEnabled)
                            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // task was canceled, do nothing
        }
    }
}
