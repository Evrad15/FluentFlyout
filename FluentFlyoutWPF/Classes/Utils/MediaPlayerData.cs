// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace FluentFlyout.Classes.Utils;

public static class MediaPlayerData
{
    private class CachedMediaPlayerInfo
    {
        public required string Title { get; set; }
        public ImageSource? Icon { get; set; }
        public int ProcessId { get; set; }
    }
    // cache for media player info to avoid redundant process lookups
    private static readonly Dictionary<string, CachedMediaPlayerInfo> mediaPlayerCache = [];

    // id variants of media players where the key is the mediaPlayerId and the value is the mediaPlayerCache key
    private static readonly Dictionary<string, string> mediaPlayerIdVariants = [];

    private static Process[]? cachedProcesses = null;
    private static DateTime lastCacheTime = DateTime.MinValue;
    private const int CACHE_DURATION_SECONDS = 5;
    private static readonly Lock _processCacheLock = new();

    public static (string, ImageSource?) GetAndCacheMediaPlayerData(string mediaPlayerId)
    {
        if (string.IsNullOrWhiteSpace(mediaPlayerId))
            return ("Media Player", null);

        if ((mediaPlayerCache.TryGetValue(mediaPlayerId, out var cachedInfo)
            || (mediaPlayerIdVariants.TryGetValue(mediaPlayerId, out var variantKey)
            && mediaPlayerCache.TryGetValue(variantKey, out cachedInfo)))
            && cachedInfo?.Icon != null)
        {
            return (cachedInfo.Title, cachedInfo.Icon);
        }

        string mediaTitle = mediaPlayerId;
        ImageSource? mediaIcon = null;

        // Split into informative tokens: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" -> ["SpotifyAB", "SpotifyMusic", "Spotify"]
        var tokens = mediaPlayerId
            .Split(['.', '_', '!', ' ', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.Equals(t, "com", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t, "github", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t, "exe", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(t, "app", StringComparison.OrdinalIgnoreCase)
                     && t.Length >= 2)
            .ToList();

        string simpleName = mediaPlayerId.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!tokens.Contains(simpleName, StringComparer.OrdinalIgnoreCase))
            tokens.Add(simpleName);

        // Fast path: try GetProcessesByName directly for candidate tokens
        foreach (var token in tokens)
        {
            try
            {
                var namedProcesses = Process.GetProcessesByName(token);
                foreach (var p in namedProcesses)
                {
                    try
                    {
                        var mainModule = p.MainModule;
                        if (mainModule == null) continue;

                        string path = mainModule.FileName;
                        string title = !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                            ? mainModule.FileVersionInfo.FileDescription
                            : (!string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.MainWindowTitle : p.ProcessName);

                        mediaIcon = GetIconFromPath(path);
                        if (mediaIcon != null)
                        {
                            mediaTitle = title;
                            var info = new CachedMediaPlayerInfo
                            {
                                Title = mediaTitle,
                                Icon = mediaIcon,
                                ProcessId = p.Id
                            };
                            mediaPlayerCache[mediaPlayerId] = info;
                            mediaPlayerCache[mediaTitle] = info;
                            return (mediaTitle, mediaIcon);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        Process[] processes;
        lock (_processCacheLock)
        {
            if (cachedProcesses == null || (DateTime.Now - lastCacheTime).TotalSeconds > CACHE_DURATION_SECONDS)
            {
                cachedProcesses = Process.GetProcesses();
                lastCacheTime = DateTime.Now;
            }
            processes = cachedProcesses;
        }

        foreach (var p in processes)
        {
            try
            {
                string procName = p.ProcessName;
                bool matches = tokens.Any(t =>
                    procName.Equals(t, StringComparison.OrdinalIgnoreCase)
                    || procName.Contains(t, StringComparison.OrdinalIgnoreCase)
                    || t.Contains(procName, StringComparison.OrdinalIgnoreCase));

                if (!matches && p.MainWindowHandle == IntPtr.Zero)
                    continue;

                var mainModule = p.MainModule;
                if (mainModule == null) continue;

                string path = mainModule.FileName;
                if (matches || tokens.Any(t => path.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    string title = !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                        ? mainModule.FileVersionInfo.FileDescription
                        : (!string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.MainWindowTitle : procName);

                    mediaIcon = GetIconFromPath(path);
                    if (mediaIcon != null)
                    {
                        mediaTitle = title;
                        var info = new CachedMediaPlayerInfo
                        {
                            Title = mediaTitle,
                            Icon = mediaIcon,
                            ProcessId = p.Id
                        };
                        mediaPlayerCache[mediaPlayerId] = info;
                        mediaPlayerCache[mediaTitle] = info;
                        return (mediaTitle, mediaIcon);
                    }
                }
            }
            catch { }
        }

        if (mediaIcon != null)
        {
            mediaPlayerCache[mediaPlayerId] = new CachedMediaPlayerInfo
            {
                Title = mediaTitle,
                Icon = mediaIcon,
                ProcessId = 0
            };
        }

        return (mediaTitle, mediaIcon);
    }

    /// <summary>
    /// Extracts the associated icon for a given process ID. Returns null if the process is inaccessible.
    /// </summary>
    public static ImageSource? GetAndCacheProcessIcon(int processId, string title)
    {
        try
        {
            if (title == "System sounds") return null;

            // search in cache
            foreach (var item in mediaPlayerCache.Values)
            {
                if (item.ProcessId == processId)
                {
                    return item.Icon;
                }
            }

            var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (path == null) return null;

            // store in cache for future lookups
            var icon = GetIconFromPath(path);
            if (icon != null)
            {
                mediaPlayerCache[title] = new CachedMediaPlayerInfo
                {
                    Title = title,
                    Icon = icon,
                    ProcessId = processId
                };
            }

            return icon;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? GetIconFromPath(string exePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            source.Freeze();

            return source;
        }
        catch
        {
            return null;
        }
    }
}