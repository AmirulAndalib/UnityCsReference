// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using TextElement = UnityEngine.UIElements.TextElement;
using TextGenerator = UnityEngine.TextCore.Text.TextGenerator;
using TextUtilities = UnityEngine.UIElements.TextUtilities;

namespace Unity.GraphToolsFoundation.Editor
{
    /// <summary>
    /// A VisualElement used as a container for <see cref="Port"/>s.
    /// </summary>
    class PortContainer : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<PortContainer> {}

        public static readonly string ussClassName = "ge-port-container";
        public static readonly string portCountClassNamePrefix = ussClassName.WithUssModifier("port-count-");

        static TextGenerator s_TextGenerator = new TextGenerator();
        string m_CurrentPortCountClassName;
        bool m_SetupLabelWidth;
        float m_MaxLabelWidth;
        bool m_SetCountModifierOnParent;

        /// <summary>
        /// Initializes a new instance of the <see cref="PortContainer"/> class.
        /// </summary>
        /// <param name="setupLabelWidth">Whether the label width should be computed based on the largest label.</param>
        /// <param name="maxInputLabelWidth">If <see cref="setupLabelWidth"/> is true, sets the maximum width for the labels.</param>
        /// <param name="setCountModifierOnParent">Whether to set the class for the modifier of the number of port on parent <see cref="VisualElement"/> too.</param>
        public PortContainer(bool setupLabelWidth, float maxLabelWidth, bool setCountModifierOnParent = false)
        {
            m_SetupLabelWidth = setupLabelWidth;
            m_MaxLabelWidth = maxLabelWidth;
            m_SetCountModifierOnParent = setCountModifierOnParent;
            AddToClassList(ussClassName);
            this.AddStylesheet_Internal("PortContainer.uss");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PortContainer"/> class.
        /// </summary>
        public PortContainer() : this(false, float.PositiveInfinity)
        {}

        /// <summary>
        /// Updates the ports in this container.
        /// </summary>
        /// <param name="ports"> The ports to update.</param>
        /// <param name="view"> The root view.</param>
        public void UpdatePorts(IEnumerable<PortModel> ports, RootView view)
        {
            var uiPorts = this.Query<Port>().ToList();
            var portViewModels = ports?.ToList() ?? new List<PortModel>();

            // Check if we should rebuild ports
            bool rebuildPorts = false;
            if (uiPorts.Count != portViewModels.Count)
            {
                rebuildPorts = true;
            }
            else
            {
                int i = 0;
                foreach (var portModel in portViewModels)
                {
                    if (!Equals(uiPorts[i].PortModel, portModel))
                    {
                        rebuildPorts = true;
                        break;
                    }

                    i++;
                }
            }

            if (rebuildPorts)
            {
                var children = Children().OfType<Port>().ToList();

                foreach (var port in children)
                {
                    Remove(port);
                    port.RemoveFromRootView();
                }

                foreach (var portModel in portViewModels)
                {
                    var ui = ModelViewFactory.CreateUI<Port>(view, portModel);
                    Debug.Assert(ui != null, "GraphElementFactory does not know how to create UI for " + portModel.GetType());
                    Add(ui);
                }
            }
            else
            {
                foreach (var port in uiPorts)
                {
                    port.UpdateFromModel();
                }
            }
            schedule.Execute(UpdateLayout).StartingIn(0);

            var newCountModifier = portCountClassNamePrefix + portViewModels.Count;
            if (m_SetCountModifierOnParent)
            {
                if (newCountModifier != m_CurrentPortCountClassName)
                {
                    parent.RemoveFromClassList(m_CurrentPortCountClassName);
                    parent.AddToClassList(newCountModifier);
                }
            }
            this.ReplaceAndCacheClassName(newCountModifier, ref m_CurrentPortCountClassName);
        }

        /// <summary>
        /// Updates the container layout, computing the needed label width if configured.
        /// </summary>
        public void UpdateLayout()
        {
            var uiPorts = this.Query<Port>().ToList();
            if (! uiPorts.Any())
                return;

            var nodeModel = uiPorts.First().PortModel.NodeModel;
            if (nodeModel == null)
                return;

            if (m_SetupLabelWidth)
            {
                float maxLabelWidth = 0;
                foreach (var port in uiPorts)
                {
                    if (!port.PortModel.IsConnected() && nodeModel is ICollapsible collapsibleNode && collapsibleNode.Collapsed)
                        continue;
                    var label = port.Label;
                    if (label != null && label.computedStyle.fontSize != 0)
                    {
                        float width = GetLabelTextWidth(label);
                        if (width > maxLabelWidth)
                            maxLabelWidth = width;
                    }
                }

                if (float.IsFinite(m_MaxLabelWidth))
                    maxLabelWidth = Mathf.Min(m_MaxLabelWidth, maxLabelWidth);

                foreach (var port in uiPorts)
                {
                    port.Label.style.minWidth = maxLabelWidth;
                    if (float.IsFinite(m_MaxLabelWidth))
                        port.Label.style.maxWidth = m_MaxLabelWidth;
                }
            }
        }

        static UnityEngine.TextCore.Text.TextGenerationSettings s_TextGenerationSettings = new();

        internal static float GetLabelTextWidth(TextElement element)
        {
            var style = element.computedStyle;

            s_TextGenerationSettings.textSettings = TextUtilities.GetTextSettingsFrom(element);

            FontAsset fontAsset = null;
            if (element.computedStyle.unityFontDefinition.fontAsset != null)
                fontAsset = element.computedStyle.unityFontDefinition.fontAsset;
            if (element.computedStyle.unityFontDefinition.font != null)
                fontAsset = s_TextGenerationSettings.textSettings.GetCachedFontAsset(element.computedStyle.unityFontDefinition.font, TextShaderUtilities.ShaderRef_MobileSDF);
            else if (element.computedStyle.unityFont != null)
                fontAsset = s_TextGenerationSettings.textSettings.GetCachedFontAsset(element.computedStyle.unityFont, TextShaderUtilities.ShaderRef_MobileSDF);

            if ( fontAsset == null)
                return 0;

            s_TextGenerationSettings.fontAsset = fontAsset;
            s_TextGenerationSettings.material = fontAsset.material;
            s_TextGenerationSettings.fontStyle = TextGeneratorUtilities.LegacyStyleToNewStyle(style.unityFontStyleAndWeight);
            s_TextGenerationSettings.textAlignment = UnityEngine.TextCore.Text.TextAlignment.MiddleLeft;
            s_TextGenerationSettings.wordWrap = false;
            s_TextGenerationSettings.overflowMode = TextOverflowMode.Overflow;
            s_TextGenerationSettings.inverseYAxis = true;
            s_TextGenerationSettings.text = element.text;
            s_TextGenerationSettings.screenRect = new Rect(0, 0, 32000, 32000);
            s_TextGenerationSettings.fontSize = element.computedStyle.fontSize.value;
            if (s_TextGenerationSettings.fontSize == 0)
                return 0;

            var size = s_TextGenerator.GetPreferredValues(s_TextGenerationSettings, TextHandle.textInfoCommon);

            return Mathf.Ceil(size.x);
        }
    }
}
