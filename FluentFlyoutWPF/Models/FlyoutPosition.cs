// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Models;

/// <summary>
/// Defines the possible screen positions for the flyout window.
/// Stored as int in UserSettings for XML serialization compatibility.
/// </summary>
public enum FlyoutPosition
{
    BottomLeft   = 0,
    BottomCenter = 1,
    BottomRight  = 2,
    TopLeft      = 3,
    TopCenter    = 4,
    TopRight     = 5,
}
