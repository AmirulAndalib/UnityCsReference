// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEditor.PackageManager.UI
{
    internal enum PackageState
    {
        UpToDate,
        Installed,
        ImportAvailable,
        Custom,
        [Obsolete("use PackageState.Custom instead.")]
        InDevelopment = Custom,
        Outdated,
        InProgress,
        Error
    }
}
