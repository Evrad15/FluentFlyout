// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluentFlyout.Controls;

public partial class MarqueeTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueeTextBlock),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextChanged));

    public static readonly DependencyProperty TextOpacityProperty =
        DependencyProperty.Register(
            nameof(TextOpacity),
            typeof(double),
            typeof(MarqueeTextBlock),
            new PropertyMetadata(1.0, OnTextOpacityChanged));

    private string _activeText = string.Empty;
    private double _activeAvailableWidth = 0;
    private bool _isMarqueeActive = false;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double TextOpacity
    {
        get => (double)GetValue(TextOpacityProperty);
        set => SetValue(TextOpacityProperty, value);
    }

    public MarqueeTextBlock()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarqueeTextBlock control)
        {
            if (control.DisplayTextBlock != null)
            {
                control.DisplayTextBlock.Text = control.Text ?? string.Empty;
            }
            control.ResetAndEvaluateMarquee(force: true);
        }
    }

    private static void OnTextOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DisplayTextBlock != null)
        {
            DisplayTextBlock.Text = Text ?? string.Empty;
        }
        ResetAndEvaluateMarquee(force: true);
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopMarquee();
    }

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResetAndEvaluateMarquee(force: false);
    }

    public void ResetAndEvaluateMarquee(bool force = false)
    {
        if (DisplayTextBlock != null && DisplayTextBlock.Text != (Text ?? string.Empty))
        {
            DisplayTextBlock.Text = Text ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(Text) || Text == "-")
        {
            StopMarquee();
            _activeText = string.Empty;
            return;
        }

        double availableWidth = ActualWidth > 0 ? ActualWidth : Width;
        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            availableWidth = 130; // Default width
        }

        // If nothing changed and marquee is already actively running or stopped, don't restart it unnecessarily
        if (!force && _activeText == Text && Math.Abs(_activeAvailableWidth - availableWidth) < 1.0)
        {
            return;
        }

        _activeText = Text;
        _activeAvailableWidth = availableWidth;

        try
        {
            var fontFamily = FontFamily ?? DisplayTextBlock?.FontFamily ?? new FontFamily("Segoe UI Variable Text, Segoe UI");
            var typeface = new Typeface(fontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
            double fontSize = FontSize > 0 ? FontSize : (DisplayTextBlock?.FontSize > 0 ? DisplayTextBlock.FontSize : 12);

            double dpi = 1.0;
            try
            {
                dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }
            catch
            {
                dpi = 1.0;
            }

            var formattedText = new FormattedText(
                Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                dpi);

            double textWidth = formattedText.WidthIncludingTrailingWhitespace;

            if (DisplayTextBlock != null)
            {
                DisplayTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (DisplayTextBlock.DesiredSize.Width > 0)
                {
                    textWidth = Math.Max(textWidth, DisplayTextBlock.DesiredSize.Width);
                }
            }

            double overflow = textWidth - availableWidth;

            if (overflow > 4) // Only scroll if text exceeds container width by more than 4px
            {
                StartMarquee(overflow);
            }
            else
            {
                StopMarquee();
            }
        }
        catch
        {
            StopMarquee();
        }
    }

    private void StartMarquee(double overflowDistance)
    {
        StopMarquee();

        _isMarqueeActive = true;

        double targetOffset = -Math.Ceiling(overflowDistance + 4);
        double scrollSpeed = 24.0; // Pixels per second for pleasant, readable scrolling
        double scrollDurationSeconds = Math.Max(overflowDistance / scrollSpeed, 1.6);
        double returnDurationSeconds = Math.Max(overflowDistance / 28.0, 1.4);

        TimeSpan initialPause = TimeSpan.FromSeconds(1.8); // Hold at start
        TimeSpan scrollDuration = TimeSpan.FromSeconds(scrollDurationSeconds); // Scroll to end
        TimeSpan endPause = TimeSpan.FromSeconds(1.8); // Hold at end
        TimeSpan returnDuration = TimeSpan.FromSeconds(returnDurationSeconds); // Smooth glide back to start

        var keyFramesAnimation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        // Keyframe 1: Start at 0
        keyFramesAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.Zero));

        // Keyframe 2: Hold at 0 during initial pause so user can read the beginning
        keyFramesAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, initialPause));

        // Keyframe 3: Scroll smoothly forward to the end of the text
        TimeSpan scrollEndTime = initialPause + scrollDuration;
        keyFramesAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(targetOffset, scrollEndTime));

        // Keyframe 4: Hold at the end during end pause so user can read the title end
        TimeSpan holdEndTime = scrollEndTime + endPause;
        keyFramesAnimation.KeyFrames.Add(new DiscreteDoubleKeyFrame(targetOffset, holdEndTime));

        // Keyframe 5: Smoothly glide back to the beginning (va-et-vient ping-pong)
        TimeSpan loopEndTime = holdEndTime + returnDuration;
        keyFramesAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0, loopEndTime, new KeySpline(0.25, 0.1, 0.25, 1.0)));

        TextTransform.BeginAnimation(TranslateTransform.XProperty, keyFramesAnimation);
    }

    public void StopMarquee()
    {
        _isMarqueeActive = false;
        TextTransform.BeginAnimation(TranslateTransform.XProperty, null);
        TextTransform.X = 0;
    }
}
