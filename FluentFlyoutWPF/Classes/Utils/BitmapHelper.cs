// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace FluentFlyout.Classes.Utils;

internal static class BitmapHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // LRU cache implementation for caching thumbnails and their dominant colors
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lruList = [];
        private readonly object _sync = new();

        private sealed class CacheEntry(TKey key, TValue value)
        {
            public TKey Key { get; } = key;
            public TValue Value { get; set; } = value;
        }

        public LruCache(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    existing.Value.Value = value;
                    _lruList.Remove(existing);
                    _lruList.AddFirst(existing);
                    return;
                }

                var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
                _lruList.AddFirst(node);
                _map[key] = node;

                if (_map.Count <= _capacity)
                    return;

                var leastRecent = _lruList.Last;
                if (leastRecent == null)
                    return;

                _lruList.RemoveLast();
                _map.Remove(leastRecent.Value.Key);
            }
        }
    }

    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private const int _cacheEntryLimit = 5;

    // cached thumbnails to prevent reprocessing
    private static readonly LruCache<int, BitmapImage> _thumbnailCache = new(_cacheEntryLimit);

    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<int, List<SolidColorBrush>> _dominantColorsCache = new(_cacheEntryLimit);

    private static int _currentHashCode = 0;
    private static readonly AsyncLocal<int> _currentHashCodeContext = new();

    public static int CurrentHashCode => _currentHashCodeContext.Value != 0 ? _currentHashCodeContext.Value : _currentHashCode;

    // current or latest dominant colors
    private static List<SolidColorBrush>? _currentDominantColors;

    public static List<SolidColorBrush> SavedDominantColors
    {
        get => _currentDominantColors ??= [];
    }

    public static int ComputeFastHash(byte[] bytes)
    {
        unchecked
        {
            int hash = (int)2166136261;
            hash = (hash ^ bytes.Length) * 16777619;
            int step = Math.Max(1, bytes.Length / 64);
            for (int i = 0; i < bytes.Length; i += step)
            {
                hash = (hash ^ bytes[i]) * 16777619;
            }
            return hash;
        }
    }

    public static int GetStableThumbnailHash(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null)
            return 0;

        return CurrentHashCode;
    }

    internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference? thumbnail, int maxThumbnailSize = _maxThumbnailSize)
    {
        if (thumbnail == null)
        {
            _currentHashCode = 0;
            _currentHashCodeContext.Value = 0;
            return null;
        }

        try
        {
            using var raStream = thumbnail.OpenReadAsync().GetAwaiter().GetResult();
            using var stream = raStream.AsStreamForRead();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            if (bytes.Length == 0)
            {
                _currentHashCode = 0;
                _currentHashCodeContext.Value = 0;
                return null;
            }

            int hashCode = ComputeFastHash(bytes);

            if (_thumbnailCache.TryGetValue(hashCode, out var cachedImage) && cachedImage != null)
            {
                _currentHashCode = hashCode;
                _currentHashCodeContext.Value = hashCode;
                return cachedImage;
            }

            ms.Position = 0;
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = maxThumbnailSize;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();

            _thumbnailCache.Set(hashCode, image);
            _currentHashCode = hashCode;
            _currentHashCodeContext.Value = hashCode;
            return image;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to load thumbnail");
            return null;
        }
    }

    internal static CroppedBitmap? CropToSquare(BitmapSource? sourceImage)
    {
        if (sourceImage == null)
            return null;

        int size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
        int x = (sourceImage.PixelWidth - size) / 2;
        int y = (sourceImage.PixelHeight - size) / 2;

        var rect = new Int32Rect(x, y, size, size);

        // create a CroppedBitmap (this is a lightweight object)
        var croppedBitmap = new CroppedBitmap(sourceImage, rect);

        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    /// <summary>
    /// Gets dominant colors from last cached Bitmap from GetThumbnail method.
    /// K-means clustering for multiple colors, histogram peak for single color.
    /// </summary>
    /// <param name="colorCount">Amount of colors needed</param>
    /// <param name="maxIterations">Amount of k-means iterations (more = higher accuracy)</param>
    /// <returns>List of dominant colors from cached Bitmap as SolidColorBrush</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int maxIterations = 15, bool forceAlbumArt = true)
    {
        int hashCode = _currentHashCodeContext.Value != 0 ? _currentHashCodeContext.Value : _currentHashCode;

        if (hashCode == 0)
        {
            if (_currentDominantColors != null && _currentDominantColors.Count > 0)
                return colorCount == 1 ? [_currentDominantColors[0]] : _currentDominantColors;

            // control color (buttons, etc.)
            var accent = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorSecondary");
            if (!accent.IsFrozen)
                accent = accent.Clone();
            accent.Freeze();

            // accent color (for non-control elements)
            var accent2 = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            if (!accent2.IsFrozen)
                accent2 = accent2.Clone();
            accent2.Freeze();

            _currentDominantColors = [accent, accent2];
            return colorCount == 1 ? [accent] : _currentDominantColors;
        }

        if (!SettingsManager.Current.UseAlbumArtAsAccentColor && !forceAlbumArt)
        {
            if (_currentDominantColors != null && _currentDominantColors.Count > 0)
                return colorCount == 1 ? [_currentDominantColors[0]] : _currentDominantColors;

            var accent = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorSecondary");
            if (!accent.IsFrozen)
                accent = accent.Clone();
            accent.Freeze();

            var accent2 = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            if (!accent2.IsFrozen)
                accent2 = accent2.Clone();
            accent2.Freeze();

            _currentDominantColors = [accent, accent2];
            return colorCount == 1 ? [accent] : _currentDominantColors;
        }

        // start timing
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        try
        {
            // check if we've already calculated colors for this thumbnail
            if (_dominantColorsCache.TryGetValue(hashCode, out var cachedColors) && cachedColors != null && cachedColors.Count > 0)
            {
                _currentDominantColors = cachedColors;
                return colorCount == 1 ? [cachedColors[0]] : cachedColors;
            }

            // convert BitmapImage to BGRA byte array
            if (!_thumbnailCache.TryGetValue(hashCode, out var sourceBitmap) || sourceBitmap == null)
            {
                Logger.Warn($"Thumbnail cache miss while extracting dominant colors");
                return _currentDominantColors ?? [];
            }

            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = sourceBitmap;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;

            byte[] pixels = new byte[height * stride];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            // downsample pixels using non-allocating structs
            var rng = new Random();
            var samples = new List<RgbPixel>(pixels.Length / 40);

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 128) continue;
                if (rng.Next(10) != 0) continue; // sample ~10%

                samples.Add(new RgbPixel(r, g, b));
            }

            if (samples.Count == 0)
            {
                return _currentDominantColors ?? [];
            }

            int kCount = Math.Max(2, colorCount);

            // get random initial centroids for k-means
            var centroids = samples
                .OrderBy(_ => rng.Next())
                .Take(kCount)
                .Select(p => new double[] { p.R, p.G, p.B })
                .ToList();

            while (centroids.Count < kCount)
            {
                centroids.Add([samples[0].R, samples[0].G, samples[0].B]);
            }

            double[] sumR = new double[kCount];
            double[] sumG = new double[kCount];
            double[] sumB = new double[kCount];
            int[] counts = new int[kCount];

            // zero-allocation k-means iterations
            for (int iter = 0; iter < maxIterations; iter++)
            {
                Array.Clear(sumR, 0, kCount);
                Array.Clear(sumG, 0, kCount);
                Array.Clear(sumB, 0, kCount);
                Array.Clear(counts, 0, kCount);

                // assign pixels to nearest centroid and accumulate directly
                foreach (var pixel in samples)
                {
                    int best = 0;
                    double bestDist = double.MaxValue;

                    for (int i = 0; i < kCount; i++)
                    {
                        double dr = pixel.R - centroids[i][0];
                        double dg = pixel.G - centroids[i][1];
                        double db = pixel.B - centroids[i][2];
                        double dist = dr * dr + dg * dg + db * db;

                        if (dist < bestDist) { bestDist = dist; best = i; }
                    }

                    sumR[best] += pixel.R;
                    sumG[best] += pixel.G;
                    sumB[best] += pixel.B;
                    counts[best]++;
                }

                // recalculate centroids + check convergence
                bool converged = true;
                for (int i = 0; i < kCount; i++)
                {
                    if (counts[i] == 0) continue;

                    double newR = sumR[i] / counts[i];
                    double newG = sumG[i] / counts[i];
                    double newB = sumB[i] / counts[i];

                    double dr = newR - centroids[i][0];
                    double dg = newG - centroids[i][1];
                    double db = newB - centroids[i][2];

                    if (dr * dr + dg * dg + db * db > 1.0) converged = false;

                    centroids[i][0] = newR;
                    centroids[i][1] = newG;
                    centroids[i][2] = newB;
                }

                if (converged) break;
            }

            // Order centroids by cluster weight & chroma to pick the most vivid colors
            var orderedCentroids = centroids
                .Select((c, idx) =>
                {
                    double r = c[0] / 255.0;
                    double g = c[1] / 255.0;
                    double b = c[2] / 255.0;
                    double max = Math.Max(r, Math.Max(g, b));
                    double min = Math.Min(r, Math.Min(g, b));
                    double chroma = max - min;
                    double score = (counts[idx] / (double)samples.Count) * (1.0 + chroma * 2.0);
                    return new { Centroid = c, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .Select(x => x.Centroid)
                .ToList();

            var result = orderedCentroids.Select(c =>
            {
                byte r = (byte)Math.Clamp(c[0], 0, 255);
                byte g = (byte)Math.Clamp(c[1], 0, 255);
                byte b = (byte)Math.Clamp(c[2], 0, 255);

                double linR = ToLinear(r);
                double linG = ToLinear(g);
                double linB = ToLinear(b);
                double lum = 0.2126 * linR + 0.7152 * linG + 0.0722 * linB;

                // Gently lift pure black or extremely dark colors for visibility while retaining true hue & saturation
                if (lum < 0.10)
                {
                    double scale = 0.18 / Math.Max(0.001, lum);
                    linR = Math.Min(1.0, linR * scale);
                    linG = Math.Min(1.0, linG * scale);
                    linB = Math.Min(1.0, linB * scale);
                    r = ToGamma(linR);
                    g = ToGamma(linG);
                    b = ToGamma(linB);
                }

                return Color.FromArgb(255, r, g, b);
            }).ToList();

            // Ensure we have at least 2 distinct harmonious colors
            if (result.Count < 2 && result.Count > 0)
            {
                var c0 = result[0];
                result.Add(Color.FromArgb(255, (byte)(c0.R * 0.65), (byte)(c0.G * 0.65), (byte)(c0.B * 0.65)));
            }

            // convert to frozen brushes
            var brushes = result.Select(c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze(); // makes it immutable & thread-safe
                return brush;
            }).ToList();

            _currentDominantColors = brushes;

            // save brushes to cache with current hash as key
            _dominantColorsCache.Set(hashCode, _currentDominantColors);

#if DEBUG
            stopwatch.Stop();
            Logger.Debug($"Dominant color extraction took {stopwatch.Elapsed.TotalMilliseconds} ms");
#endif
            return colorCount == 1 ? [brushes[0]] : _currentDominantColors;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error extracting dominant colors");
            return [];
        }
    }

    private static double ToLinear(byte v)
        => Math.Pow(v / 255.0, 2.2);

    private static byte ToGamma(double v)
        => (byte)Math.Clamp(Math.Pow(v, 1.0 / 2.2) * 255.0, 0, 255);

    private readonly struct RgbPixel(byte r, byte g, byte b)
    {
        public readonly byte R = r;
        public readonly byte G = g;
        public readonly byte B = b;
    }
}