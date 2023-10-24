// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using Unity.Properties;
using UnityEngine.Pool;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Represents the root <see cref="VisualElement"/> of UXML file.
    /// </summary>
    /// <remarks>
    /// A TemplateContainer instance is created by Unity to represent the root of the UXML file and acts as the parent for all elements in the file.
    /// Users typically don't create TemplateContainer objects directly.
    /// When using <see cref="VisualTreeAsset.Instantiate()"/>, a TemplateContainer instance is returned to you to represent the root of the hierarchy.
    /// When using UXML templates, a TemplateContainer is generated for the template instance and inserted into the hierarchy of the parent UXML file.
    /// </remarks>
    public class TemplateContainer : BindableElement
    {
        internal static readonly DataBindingProperty templateIdProperty = nameof(templateId);
        internal static readonly DataBindingProperty templateSourceProperty = nameof(templateSource);

        /// <summary>
        /// Instantiates and clones a <see cref="TemplateContainer"/> using the data read from a UXML file.
        /// </summary>
        public new class UxmlFactory : UxmlFactory<TemplateContainer, UxmlTraits>
        {
            internal const string k_ElementName = "Instance";

            public override string uxmlName => k_ElementName;

            public override string uxmlQualifiedName => uxmlNamespace + "." + uxmlName;
        }

        /// <summary>
        /// Defines <see cref="UxmlTraits"/> for the <see cref="TemplateContainer"/>.
        /// </summary>
        public new class UxmlTraits : BindableElement.UxmlTraits
        {
            internal const string k_TemplateAttributeName = "template";

            UxmlStringAttributeDescription m_Template = new UxmlStringAttributeDescription { name = k_TemplateAttributeName, use = UxmlAttributeDescription.Use.Required };

            /// <summary>
            /// Returns an empty enumerable, as template instances do not have children.
            /// </summary>
            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield break; }
            }

            /// <summary>
            /// Initialize <see cref="TemplateContainer"/> properties using values from the attribute bag.
            /// </summary>
            /// <param name="ve">The object to initialize.</param>
            /// <param name="bag">The attribute bag.</param>
            /// <param name="cc">The creation context; unused.</param>
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);

                TemplateContainer templateContainer = ((TemplateContainer)ve);
                templateContainer.templateId = m_Template.GetValueFromBag(bag, cc);
                VisualTreeAsset vta = cc.visualTreeAsset?.ResolveTemplate(templateContainer.templateId);

                if (vta == null)
                    templateContainer.Add(new Label(string.Format("Unknown Template: '{0}'", templateContainer.templateId)));
                else
                {
                    using var traitsOverridesHandle = ListPool<CreationContext.AttributeOverrideRange>.Get(out var traitsOverrideRanges);
                    if (null != cc.attributeOverrides)
                        traitsOverrideRanges.AddRange(cc.attributeOverrides);
                    var attributeOverrides = (bag as TemplateAsset)?.attributeOverrides;
                    if (null != attributeOverrides)
                        traitsOverrideRanges.Add(new CreationContext.AttributeOverrideRange(cc.visualTreeAsset, attributeOverrides));

                    vta.CloneTree(ve, cc.slotInsertionPoints, traitsOverrideRanges);
                }

                if (vta == null)
                    Debug.LogErrorFormat("Could not resolve template with name '{0}'", templateContainer.templateId);
            }
        }

        /// <summary>
        /// The local ID of the template in the parent UXML file (RO).
        /// </summary>
        /// <remarks>This value is null, unless the TemplateContainer represents a UXML template within another UXML file.</remarks>
        [CreateProperty(ReadOnly = true)]
        public string templateId { get; private set; }
        private VisualElement m_ContentContainer;

        private VisualTreeAsset m_TemplateSource;

        /// <summary>
        /// Stores the template asset reference, if the generated element is cloned from a VisualTreeAsset as a
        /// Template declaration inside another VisualTreeAsset.
        /// </summary>
        [CreateProperty(ReadOnly = true)]
        public VisualTreeAsset templateSource
        {
            get => m_TemplateSource;
            internal set => m_TemplateSource = value;
        }

        /// <undoc/>
        public TemplateContainer() : this(null) {}

        /// <undoc/>
        public TemplateContainer(string templateId)
        {
            this.templateId = templateId;
            m_ContentContainer = this;
        }

        public override VisualElement contentContainer
        {
            get { return m_ContentContainer; }
        }

        internal void SetContentContainer(VisualElement content)
        {
            m_ContentContainer = content;
        }
    }
}
