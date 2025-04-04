// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEngine.Assertions;
using UnityEngine.Pool;
using UnityEngine.UIElements.StyleSheets;

namespace Unity.UI.Builder
{
    internal static class StyleSheetExtensions
    {
        public static bool IsUnityEditorStyleSheet(this StyleSheet styleSheet)
        {
            if ((UIElementsEditorUtility.IsCommonDarkStyleSheetLoaded() && styleSheet == UIElementsEditorUtility.GetCommonDarkStyleSheet())
                || (UIElementsEditorUtility.IsCommonLightStyleSheetLoaded() && styleSheet == UIElementsEditorUtility.GetCommonLightStyleSheet()))
                return true;
            return false;
        }

        public static StyleSheet DeepCopy(this StyleSheet styleSheet)
        {
            if (styleSheet == null)
                return null;

            var newStyleSheet = StyleSheetUtilities.CreateInstance();

            styleSheet.DeepOverwrite(newStyleSheet);

            return newStyleSheet;
        }

        public static void DeepOverwrite(this StyleSheet styleSheet, StyleSheet other)
        {
            if (other == null)
                return;

            var json = JsonUtility.ToJson(styleSheet);
            JsonUtility.FromJsonOverwrite(json, other);

            other.FixRuleReferences();

            other.name = styleSheet.name;
        }

        public static void FixRuleReferences(this StyleSheet styleSheet)
        {
            // This is very important. When moving selectors around via drag/drop in
            // the StyleSheets pane, the nextInTable references can become corrupt.
            // Most corruption is corrected in the StyleSheet.SetupReferences() call
            // made below. However, the ends of each chain are not set in
            // SetupReferences() because it assumes always starting with a clean
            // StyleSheets. Therefore, you can have these end selectors point to an
            // existing selector and style resolution entering a infinite loop.
            //
            // Case 1274584
            if (styleSheet.complexSelectors != null)
                foreach (var selector in styleSheet.complexSelectors)
                    selector.nextInTable = null;

            // Force call to StyleSheet.SetupReferences().
            styleSheet.rules = styleSheet.rules;
        }

        internal static List<string> GetSelectorStrings(this StyleSheet styleSheet)
        {
            var list = new List<string>();

            foreach (var complexSelector in styleSheet.complexSelectors)
            {
                var str = StyleSheetToUss.ToUssSelector(complexSelector);
                list.Add(str);
            }

            return list;
        }

        internal static string GenerateUSS(this StyleSheet styleSheet)
        {
            string result = null;
            try
            {
                result = StyleSheetToUss.ToUssString(styleSheet);
            }
            catch (Exception ex)
            {
                if (!styleSheet.name.Contains(BuilderConstants.InvalidUXMLOrUSSAssetNameSuffix))
                {
                    var message = string.Format(BuilderConstants.InvalidUSSDialogMessage, styleSheet.name);
                    BuilderDialogsUtility.DisplayDialog(BuilderConstants.InvalidUSSDialogTitle, message);
                    styleSheet.name = styleSheet.name + BuilderConstants.InvalidUXMLOrUSSAssetNameSuffix;
                }
                else
                {
                    var name = styleSheet.name.Replace(BuilderConstants.InvalidUXMLOrUSSAssetNameSuffix, string.Empty);
                    var message = string.Format(BuilderConstants.InvalidUSSDialogMessage, name);
                    Builder.ShowWarning(message);
                }
                Debug.LogError(ex.Message + "\n" + ex.StackTrace);
            }
            return result;
        }

        internal static StyleComplexSelector FindSelector(this StyleSheet styleSheet, string selectorStr)
        {
            // Remove extra whitespace.
            var selectorSplit = selectorStr.Split(' ');
            selectorStr = String.Join(" ", selectorSplit);

            foreach (var complexSelector in styleSheet.complexSelectors)
            {
                var str = StyleSheetToUss.ToUssSelector(complexSelector);

                if (str == selectorStr)
                    return complexSelector;
            }

            return null;
        }

        internal static void RemoveSelector(
            this StyleSheet styleSheet, StyleComplexSelector selector, string undoMessage = null)
        {
            if (selector == null)
                return;

            // Undo/Redo
            if (string.IsNullOrEmpty(undoMessage))
                undoMessage = "Delete UI Style Selector";
            Undo.RegisterCompleteObjectUndo(styleSheet, undoMessage);

            var selectorList = styleSheet.complexSelectors.ToList();
            selectorList.Remove(selector);
            styleSheet.complexSelectors = selectorList.ToArray();
        }

        internal static void RemoveSelector(
            this StyleSheet styleSheet, string selectorStr, string undoMessage = null)
        {
            var selector = styleSheet.FindSelector(selectorStr);
            if (selector == null)
                return;

            RemoveSelector(styleSheet, selector, undoMessage);
        }

        public static int AddRule(this StyleSheet styleSheet)
        {
            var rule = new StyleRule { line = -1 };
            rule.properties = new StyleProperty[0];

            // Add rule to StyleSheet.
            var rulesList = styleSheet.rules.ToList();
            var index = rulesList.Count;
            rulesList.Add(rule);
            styleSheet.rules = rulesList.ToArray();

            return index;
        }

        public static StyleRule GetRule(this StyleSheet styleSheet, int index)
        {
            if (styleSheet.rules.Count() <= index)
                return null;

            return styleSheet.rules[index];
        }

        public static StyleComplexSelector AddSelector(
            this StyleSheet styleSheet, string complexSelectorStr, string undoMessage = null)
        {
            if (!SelectorUtility.TryCreateSelector(complexSelectorStr, out var complexSelector, out var error))
            {
                Builder.ShowWarning(error);
                return null;
            }

            return styleSheet.AddSelector(complexSelector, undoMessage);
        }

        public static StyleComplexSelector AddSelector(
            this StyleSheet styleSheet, StyleComplexSelector complexSelector, string undoMessage = null)
        {
            // Undo/Redo
            if (string.IsNullOrEmpty(undoMessage))
                undoMessage = "New UI Style Selector";
            Undo.RegisterCompleteObjectUndo(styleSheet, undoMessage);

            // Add rule to StyleSheet.
            var rulesList = styleSheet.rules.ToList();
            rulesList.Add(complexSelector.rule);
            styleSheet.rules = rulesList.ToArray();
            complexSelector.ruleIndex = styleSheet.rules.Length - 1;

            // Add complex selector to list in stylesheet.
            var complexSelectorsList = styleSheet.complexSelectors.ToList();
            complexSelectorsList.Add(complexSelector);
            styleSheet.complexSelectors = complexSelectorsList.ToArray();
            styleSheet.SetTemporaryContentHash();

            return complexSelector;
        }

        public static void TransferRulePropertiesToSelector(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, StyleSheet fromStyleSheet, StyleRule fromRule)
        {
            // We're going to iterate on a copy of the properties list, because each property that will be transferred
            // will be removed from the rule.
            using var _ = ListPool<StyleProperty>.Get(out var properties);
            properties.AddRange(fromRule.properties);

            foreach (var property in properties)
            {
                toStyleSheet.TransferPropertyToSelector(toSelector, fromStyleSheet, fromRule, property);
            }
        }

        public static void TransferPropertyToSelector(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, StyleSheet fromStyleSheet, StyleRule fromRule, StyleProperty property)
        {
            // remove the property if it exists in the destination
            toStyleSheet.RemoveProperty(toSelector.rule, property.name);

            var toProperty = toStyleSheet.AddProperty(toSelector, property.name);
            StyleSheetUtility.TransferStylePropertyHandles(fromStyleSheet, property, toStyleSheet, toProperty);

            fromStyleSheet.RemoveProperty(fromRule, property);
        }

        public static void TransferPropertyToSelector(this StyleSheet styleSheet, StyleComplexSelector toSelector, StyleRule fromRule, StyleProperty property)
        {
            var toProperty = styleSheet.AddProperty(toSelector, property.name);
            StyleSheetUtility.TransferStylePropertyHandles(styleSheet, property, styleSheet, toProperty);
            styleSheet.RemoveProperty(fromRule, property);
        }

        public static void DuplicatePropertyInSelector(this StyleSheet styleSheet, StyleComplexSelector selector, StyleProperty property, string name)
        {
            var toProperty = styleSheet.AddProperty(selector, name);
            StyleSheetUtility.TransferStylePropertyHandles(styleSheet, property, styleSheet, toProperty);
        }

        public static void AddVariable(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, StyleValueType valueType, string variableName, string value)
        {
            var property = toStyleSheet.AddProperty(toSelector, variableName);

            switch (valueType)
            {
                case StyleValueType.Float:
                    toStyleSheet.AddValue(property, float.Parse(value));
                    break;
                case StyleValueType.Dimension:
                    toStyleSheet.AddValue(property, new Dimension(float.Parse(value), Dimension.Unit.Pixel));
                    break;
                case StyleValueType.String:
                    toStyleSheet.AddValue(property, value);
                    break;
                case StyleValueType.Variable:
                    toStyleSheet.AddVariable(property, value);
                    break;
                case StyleValueType.Keyword:
                    toStyleSheet.AddValue(property, (StyleValueKeyword)Enum.Parse(typeof(StyleValueKeyword), value));
                    break;
                case StyleValueType.Enum:
                    toStyleSheet.AddValueAsEnum(property, value);
                    break;
            }

            property.isCustomProperty = true;
            toStyleSheet.SetTemporaryContentHash();
        }

        // Add color variable.
        public static void AddVariable(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, string variableName, Color value)
        {
            var property = toStyleSheet.AddProperty(toSelector, variableName);
            toStyleSheet.AddValue(property, value);
            property.isCustomProperty = true;
            toStyleSheet.SetTemporaryContentHash();
        }

        // Add image variable.
        public static void AddVariable(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, string variableName, UnityEngine.Object value)
        {
            var property = toStyleSheet.AddProperty(toSelector, variableName);
            toStyleSheet.AddValue(property, value);
            property.isCustomProperty = true;
            toStyleSheet.SetTemporaryContentHash();
        }

        // Add dimension variable.
        public static void AddVariable(this StyleSheet toStyleSheet, StyleComplexSelector toSelector, string variableName, Dimension value)
        {
            var property = toStyleSheet.AddProperty(toSelector, variableName);
            toStyleSheet.AddValue(property, value);
            property.isCustomProperty = true;
            toStyleSheet.SetTemporaryContentHash();
        }

        public static bool IsSelected(this StyleSheet styleSheet)
        {
            var selector = styleSheet.FindSelector(BuilderConstants.SelectedStyleSheetSelectorName);
            return selector != null;
        }

        static void SwallowStyleRule(
            StyleSheet toStyleSheet, StyleComplexSelector toSelector,
            StyleSheet fromStyleSheet, StyleComplexSelector fromSelector)
        {
            SwallowStyleRule(toStyleSheet, toSelector.rule, fromStyleSheet, fromSelector.rule);
        }

        public static void SwallowStyleRule(
            StyleSheet toStyleSheet, StyleRule toRule,
            StyleSheet fromStyleSheet, StyleRule fromRule)
        {
            // Add property values to sheet.
            foreach (var fromProperty in fromRule.properties)
            {
                var toProperty = toStyleSheet.AddProperty(toRule, fromProperty.name);
                StyleSheetUtility.TransferStylePropertyHandles(fromStyleSheet, fromProperty, toStyleSheet, toProperty);
            }
        }

        public static StyleComplexSelector Swallow(this StyleSheet toStyleSheet, StyleSheet fromStyleSheet, StyleComplexSelector fromSelector)
        {
            var toSelector = toStyleSheet.AddSelector(StyleSheetToUss.ToUssSelector(fromSelector));
            if (toSelector == null)
                return null;

            SwallowStyleRule(toStyleSheet, toSelector, fromStyleSheet, fromSelector);
            return toSelector;
        }

        public static void Swallow(this StyleSheet toStyleSheet, StyleSheet fromStyleSheet)
        {
            foreach (var fromSelector in fromStyleSheet.complexSelectors)
            {
                Swallow(toStyleSheet, fromStyleSheet, fromSelector);
            }
            toStyleSheet.contentHash = fromStyleSheet.contentHash;
        }

        public static void ClearUndo(this StyleSheet styleSheet)
        {
            if (styleSheet == null)
                return;

            Undo.ClearUndo(styleSheet);
        }

        public static void Destroy(this StyleSheet styleSheet)
        {
            if (styleSheet == null)
                return;

            ScriptableObject.DestroyImmediate(styleSheet);
        }
    }
}
