// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Internal;

namespace UnityEditor.Licensing.UI.Events.Handlers
{
[ExcludeFromDocs]
public abstract class INotificationHandler
{
    public virtual void Handle(bool isHumanControllingUs)
    {
        if (isHumanControllingUs)
        {
            HandleUI();
        }
        else
        {
            HandleBatchmode();
        }
    }

    public abstract void HandleUI();

    public abstract void HandleBatchmode();
}
}
