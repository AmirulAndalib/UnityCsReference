// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Assertions;
using UnityEngine.Pool;

namespace UnityEngine.UIElements
{
    [Serializable]
    internal class TemplateAsset : VisualElementAsset //<TemplateContainer>
    {
        [SerializeField]
        private string m_TemplateAlias;

        public string templateAlias
        {
            get { return m_TemplateAlias; }
            set { m_TemplateAlias = value; }
        }

        [Serializable]
        public struct AttributeOverride
        {
            public string m_ElementName;
            public string m_AttributeName;
            public string m_Value;
        }
        [SerializeField]
        private List<AttributeOverride> m_AttributeOverrides;
        public List<AttributeOverride> attributeOverrides
        {
            get { return m_AttributeOverrides == null ? (m_AttributeOverrides = new List<AttributeOverride>()) : m_AttributeOverrides; }
            set { m_AttributeOverrides = value; }
        }

        public bool hasAttributeOverride => m_AttributeOverrides is {Count: > 0};

        [Serializable]
        public struct UxmlSerializedDataOverride
        {
            public int m_ElementId;
            [SerializeReference]
            public UxmlSerializedData m_SerializedData;
        }

        [SerializeField] private List<UxmlSerializedDataOverride> m_SerializedDataOverride;
        public List<UxmlSerializedDataOverride> serializedDataOverrides
        {
            get => m_SerializedDataOverride ??= new List<UxmlSerializedDataOverride>();
            set => m_SerializedDataOverride = value;
        }

        internal override VisualElement Instantiate(CreationContext cc)
        {
            var tc = (TemplateContainer)base.Instantiate(cc);
            if (tc.templateSource == null)
            {
                // If the template is defined with the path attribute instead of src it may not be resolved at import time
                // due to import order. This is because the dependencies are not declared with the path attribute
                // since resource folders makes it not possible to obtain non ambiguous asset paths.
                // In that case try to resolve it here at runtime.
                tc.templateSource = cc.visualTreeAsset?.ResolveTemplate(tc.templateId);
                if (tc.templateSource == null)
                {
                    tc.Add(new Label($"Unknown Template: '{tc.templateId}'"));
                    return tc;
                }
            }

            // Gather the overrides in hierarchical order where overrides coming from the parent VisualTreeAsset will appear in the lists below before the overrides coming from the nested
            // VisualTreeAssets. The overrides will be processed in reverse order.
            using var traitsOverridesHandle = ListPool<CreationContext.AttributeOverrideRange>.Get(out var traitsOverrideRanges);
            using var dataOverridesHandle = ListPool<CreationContext.SerializedDataOverrideRange>.Get(out var serializedDataOverrideRanges);

            // Populate traits attribute overrides. This will be used in two contexts:
            // 1- When an element does not use the Uxml Serialization feature and relies on the Uxml Factory/Traits system.
            // 2- When an element is using the Uxml Serialization, we'll use these overrides to partially override the UxmlSerializedData.
            if (null != cc.attributeOverrides)
                traitsOverrideRanges.AddRange(cc.attributeOverrides);
            if (null != attributeOverrides)
                traitsOverrideRanges.Add(new CreationContext.AttributeOverrideRange(cc.visualTreeAsset, attributeOverrides));

            // Populate the serialized data overrides.
            if (null != cc.serializedDataOverrides)
                serializedDataOverrideRanges.AddRange(cc.serializedDataOverrides);
            if (null != serializedDataOverrides)
                serializedDataOverrideRanges.Add(new CreationContext.SerializedDataOverrideRange(cc.visualTreeAsset, serializedDataOverrides));

            tc.templateSource.CloneTree(tc, new CreationContext(cc.slotInsertionPoints, traitsOverrideRanges, serializedDataOverrideRanges, null, null));

            return tc;
        }

        [SerializeField]
        private List<VisualTreeAsset.SlotUsageEntry> m_SlotUsages;

        internal List<VisualTreeAsset.SlotUsageEntry> slotUsages
        {
            get { return m_SlotUsages; }
            set { m_SlotUsages = value; }
        }

        public TemplateAsset(string templateAlias, string fullTypeName)
            : base(fullTypeName)
        {
            Assert.IsFalse(string.IsNullOrEmpty(templateAlias), "Template alias must not be null or empty");
            m_TemplateAlias = templateAlias;
        }

        public void AddSlotUsage(string slotName, int resId)
        {
            if (m_SlotUsages == null)
                m_SlotUsages = new List<VisualTreeAsset.SlotUsageEntry>();
            m_SlotUsages.Add(new VisualTreeAsset.SlotUsageEntry(slotName, resId));
        }
    }
}
