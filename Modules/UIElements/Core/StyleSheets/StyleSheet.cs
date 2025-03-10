// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using UnityEngine.Bindings;
using TableType = System.Collections.Generic.Dictionary<string, UnityEngine.UIElements.StyleComplexSelector>;
using UnityEngine.UIElements.StyleSheets;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// Style sheets are applied to visual elements in order to control the layout and visual appearance of the user interface.
    /// </summary>
    /// <remarks>
    /// The <see cref="StyleSheet"/> class holds the imported data of USS files in your project.
    /// Once loaded, a style sheet can be attached to a <see cref="VisualElement"/> object to affect the element itself and its descendants.
    /// </remarks>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// public class MyEditorWindow : EditorWindow
    /// {
    ///     void OnEnable()
    ///     {
    ///         rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/styles.uss"));
    ///     }
    /// }
    /// ]]>
    /// </code>
    /// </example>
    [HelpURL("UIE-USS")]
    [Serializable]
    public class StyleSheet : ScriptableObject
    {
        [SerializeField]
        bool m_ImportedWithErrors;

        /// <summary>
        /// Whether there were errors encountered while importing the StyleSheet
        /// </summary>
        public bool importedWithErrors
        {
            get { return m_ImportedWithErrors; }
            internal set { m_ImportedWithErrors = value; }
        }

        [SerializeField]
        bool m_ImportedWithWarnings;

        /// <summary>
        /// Whether there were warnings encountered while importing the StyleSheet
        /// </summary>
        public bool importedWithWarnings
        {
            get { return m_ImportedWithWarnings; }
            internal set { m_ImportedWithWarnings = value; }
        }

        [SerializeField]
        StyleRule[] m_Rules;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal StyleRule[] rules
        {
            get { return m_Rules; }
            set
            {
                m_Rules = value;
                SetupReferences();
            }
        }

        [SerializeField]
        StyleComplexSelector[] m_ComplexSelectors;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal StyleComplexSelector[] complexSelectors
        {
            get { return m_ComplexSelectors; }
            set
            {
                m_ComplexSelectors = value;
                SetupReferences();
            }
        }

        // Only the importer should write to these fields
        // Normal usage should only go through ReadXXX methods
        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal float[] floats;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal Dimension[] dimensions;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal Color[] colors;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal string[] strings;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal Object[] assets;

        [Serializable]
        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal struct ImportStruct
        {
            public StyleSheet styleSheet;
            public string[] mediaQueries;
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        [SerializeField]
        internal ImportStruct[] imports;

        [SerializeField]
        List<StyleSheet> m_FlattenedImportedStyleSheets;
        internal List<StyleSheet> flattenedRecursiveImports
        {
            [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
            get { return m_FlattenedImportedStyleSheets; }
        }

        [SerializeField]
        private int m_ContentHash;

        /// <summary>
        /// A hash value computed from the stylesheet content.
        /// </summary>
        public int contentHash
        {
            get { return m_ContentHash; }
            set { m_ContentHash = value; }
        }

        [SerializeField]
        internal ScalableImage[] scalableImages;

        // This enum is used to retrieve a given "TableType" at specific index in the related array
        internal enum OrderedSelectorType
        {
            None = -1,
            Name = 0,
            Type = 1,
            Class = 2,
            Length = 3 // Used to initialize the array
        }

        [NonSerialized]
        internal TableType[] tables;

        [NonSerialized] internal int nonEmptyTablesMask;

        [NonSerialized] internal StyleComplexSelector firstRootSelector;

        [NonSerialized] internal StyleComplexSelector firstWildCardSelector;

        [NonSerialized]
        private bool m_IsDefaultStyleSheet;

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal bool isDefaultStyleSheet
        {
            get { return m_IsDefaultStyleSheet; }
            set
            {
                m_IsDefaultStyleSheet = value;
                if (flattenedRecursiveImports != null)
                {
                    foreach (var importedStyleSheet in flattenedRecursiveImports)
                    {
                        importedStyleSheet.isDefaultStyleSheet = value;
                    }
                }
            }
        }

        static string kCustomPropertyMarker = "--";

        bool TryCheckAccess<T>(T[] list, StyleValueType type, StyleValueHandle handle, out T value)
        {
            bool result = false;
            value = default(T);

            if (handle.valueType == type && handle.valueIndex >= 0 && handle.valueIndex < list.Length)
            {
                value = list[handle.valueIndex];
                result = true;
            }
            else
            {
                Debug.LogErrorFormat(this, "Trying to read value of type {0} while reading a value of type {1}", type, handle.valueType);
            }
            return result;
        }

        T CheckAccess<T>(T[] list, StyleValueType type, StyleValueHandle handle)
        {
            T value = default(T);
            if (handle.valueType != type)
            {
                Debug.LogErrorFormat(this, "Trying to read value of type {0} while reading a value of type {1}", type, handle.valueType);
            }
            else if (list == null || handle.valueIndex < 0 || handle.valueIndex >= list.Length)
            {
                Debug.LogError("Accessing invalid property", this);
            }
            else
            {
                value = list[handle.valueIndex];
            }
            return value;
        }

        internal virtual void OnEnable()
        {
            SetupReferences();
        }

        internal void FlattenImportedStyleSheetsRecursive()
        {
            m_FlattenedImportedStyleSheets = new List<StyleSheet>();
            FlattenImportedStyleSheetsRecursive(this);
        }

        void FlattenImportedStyleSheetsRecursive(StyleSheet sheet)
        {
            if (sheet.imports == null)
                return;

            for (var i = 0; i < sheet.imports.Length; i++)
            {
                var importedStyleSheet = sheet.imports[i].styleSheet;
                if (importedStyleSheet == null)
                    continue;

                importedStyleSheet.isDefaultStyleSheet = isDefaultStyleSheet;
                FlattenImportedStyleSheetsRecursive(importedStyleSheet);
                m_FlattenedImportedStyleSheets.Add(importedStyleSheet);
            }
        }

        void SetupReferences()
        {
            if (complexSelectors == null || rules == null)
                return;

            // Setup rules and properties for var
            foreach (var rule in rules)
            {
                foreach (var property in rule.properties)
                {
                    if (CustomStartsWith(property.name, kCustomPropertyMarker))
                    {
                        ++rule.customPropertiesCount;
                        property.isCustomProperty = true;
                    }

                    foreach (var handle in property.values)
                    {
                        if (handle.IsVarFunction())
                        {
                            property.requireVariableResolve = true;
                            break;
                        }
                    }
                }
            }

            for (int i = 0, count = complexSelectors.Length; i < count; i++)
            {
                complexSelectors[i].CachePseudoStateMasks(this);
            }

            tables = new TableType[(int)OrderedSelectorType.Length];
            tables[(int)OrderedSelectorType.Name] = new TableType(StringComparer.Ordinal);
            tables[(int)OrderedSelectorType.Type] = new TableType(StringComparer.Ordinal);
            tables[(int)OrderedSelectorType.Class] = new TableType(StringComparer.Ordinal);

            nonEmptyTablesMask = 0;

            firstRootSelector = null;
            firstWildCardSelector = null;

            for (int i = 0; i < complexSelectors.Length; i++)
            {
                // Here we set-up runtime-only pointers
                StyleComplexSelector complexSel = complexSelectors[i];

                if (complexSel.ruleIndex < rules.Length)
                {
                    complexSel.rule = rules[complexSel.ruleIndex];
                }

                complexSel.CalculateHashes();

                complexSel.orderInStyleSheet = i;

                StyleSelector lastSelector = complexSel.selectors[complexSel.selectors.Length - 1];
                StyleSelectorPart part = lastSelector.parts[0];

                string key = part.value;

                OrderedSelectorType tableToUse = OrderedSelectorType.None;

                switch (part.type)
                {
                    case StyleSelectorType.Class:
                        tableToUse = OrderedSelectorType.Class;
                        break;
                    case StyleSelectorType.ID:
                        tableToUse = OrderedSelectorType.Name;
                        break;
                    case StyleSelectorType.Type:
                        key = part.value;
                        tableToUse = OrderedSelectorType.Type;
                        break;

                    case StyleSelectorType.Wildcard:
                        if (firstWildCardSelector != null)
                            complexSel.nextInTable = firstWildCardSelector;
                        firstWildCardSelector = complexSel;
                        break;

                    case StyleSelectorType.PseudoClass:
                        // :root selector are put separately because they apply to very few elements
                        if ((lastSelector.pseudoStateMask & (int)PseudoStates.Root) != 0)
                        {
                            if (firstRootSelector != null)
                                complexSel.nextInTable = firstRootSelector;
                            firstRootSelector = complexSel;
                        }
                        // in this case we assume a wildcard selector
                        // since a selector such as ":selected" applies to all elements
                        else
                        {
                            if (firstWildCardSelector != null)
                                complexSel.nextInTable = firstWildCardSelector;
                            firstWildCardSelector = complexSel;
                        }
                        break;
                    default:
                        Debug.LogError($"Invalid first part type {part.type}", this);
                        break;
                }

                if (tableToUse != OrderedSelectorType.None)
                {
                    StyleComplexSelector previous;
                    TableType table = tables[(int)tableToUse];
                    if (table.TryGetValue(key, out previous))
                    {
                        complexSel.nextInTable = previous;
                    }
                    nonEmptyTablesMask |= (1 << (int)tableToUse);
                    table[key] = complexSel;
                }
            }
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal StyleValueKeyword ReadKeyword(StyleValueHandle handle)
        {
            return (StyleValueKeyword)handle.valueIndex;
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal float ReadFloat(StyleValueHandle handle)
        {
            // Handle dimension for properties with optional unit
            if (handle.valueType == StyleValueType.Dimension)
            {
                Dimension dimension = CheckAccess(dimensions, StyleValueType.Dimension, handle);
                return dimension.value;
            }
            return CheckAccess(floats, StyleValueType.Float, handle);
        }

        internal bool TryReadFloat(StyleValueHandle handle, out float value)
        {
            if (TryCheckAccess(floats, StyleValueType.Float, handle, out value))
                return true;

            // Handle dimension for properties with optional unit
            Dimension dimensionValue;
            bool isDimension = TryCheckAccess(dimensions, StyleValueType.Float, handle, out dimensionValue);
            value = dimensionValue.value;
            return isDimension;
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal Dimension ReadDimension(StyleValueHandle handle)
        {
            // If the value is 0 (without unit) it's stored as a float
            if (handle.valueType == StyleValueType.Float)
            {
                float value = CheckAccess(floats, StyleValueType.Float, handle);
                return new Dimension(value, Dimension.Unit.Unitless);
            }
            return CheckAccess(dimensions, StyleValueType.Dimension, handle);
        }

        internal bool TryReadDimension(StyleValueHandle handle, out Dimension value)
        {
            if (TryCheckAccess(dimensions, StyleValueType.Dimension, handle, out value))
                return true;

            // If the value is 0 (without unit) it's stored as a float
            float floatValue = 0f;
            bool isFloat = TryCheckAccess(floats, StyleValueType.Float, handle, out floatValue);
            value = new Dimension(floatValue, Dimension.Unit.Unitless);
            return isFloat;
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal Color ReadColor(StyleValueHandle handle)
        {
            return CheckAccess(colors, StyleValueType.Color, handle);
        }

        internal bool TryReadColor(StyleValueHandle handle, out Color value)
        {
            return TryCheckAccess(colors, StyleValueType.Color, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadString(StyleValueHandle handle)
        {
            return CheckAccess(strings, StyleValueType.String, handle);
        }

        internal bool TryReadString(StyleValueHandle handle, out string value)
        {
            return TryCheckAccess(strings, StyleValueType.String, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadEnum(StyleValueHandle handle)
        {
            return CheckAccess(strings, StyleValueType.Enum, handle);
        }

        internal bool TryReadEnum(StyleValueHandle handle, out string value)
        {
            return TryCheckAccess(strings, StyleValueType.Enum, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadVariable(StyleValueHandle handle)
        {
            return CheckAccess(strings, StyleValueType.Variable, handle);
        }

        internal bool TryReadVariable(StyleValueHandle handle, out string value)
        {
            return TryCheckAccess(strings, StyleValueType.Variable, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadResourcePath(StyleValueHandle handle)
        {
            return CheckAccess(strings, StyleValueType.ResourcePath, handle);
        }

        internal bool TryReadResourcePath(StyleValueHandle handle, out string value)
        {
            return TryCheckAccess(strings, StyleValueType.ResourcePath, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal Object ReadAssetReference(StyleValueHandle handle)
        {
            return CheckAccess(assets, StyleValueType.AssetReference, handle);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadMissingAssetReferenceUrl(StyleValueHandle handle)
        {
            return CheckAccess(strings, StyleValueType.MissingAssetReference, handle);
        }

        internal bool TryReadAssetReference(StyleValueHandle handle, out Object value)
        {
            return TryCheckAccess(assets, StyleValueType.AssetReference, handle, out value);
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal StyleValueFunction ReadFunction(StyleValueHandle handle)
        {
            return (StyleValueFunction)handle.valueIndex;
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal string ReadFunctionName(StyleValueHandle handle)
        {
            if (handle.valueType != StyleValueType.Function)
            {
                Debug.LogErrorFormat(this, $"Trying to read value of type {StyleValueType.Function} while reading a value of type {handle.valueType}");
                return string.Empty;
            }

            var svf = (StyleValueFunction)handle.valueIndex;
            return svf.ToUssString();
        }

        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal ScalableImage ReadScalableImage(StyleValueHandle handle)
        {
            return CheckAccess(scalableImages, StyleValueType.ScalableImage, handle);
        }

        private static bool CustomStartsWith(string originalString, string pattern)
        {
            int originalLength = originalString.Length;
            int patternLength = pattern.Length;
            int originalPos = 0;
            int patternPos = 0;

            while (originalPos < originalLength && patternPos < patternLength && originalString[originalPos] == pattern[patternPos])
            {
                originalPos++;
                patternPos++;
            }

            return (patternPos == patternLength && originalLength >= patternLength) || (originalPos == originalLength && patternLength >= originalLength);
        }
    }
}
