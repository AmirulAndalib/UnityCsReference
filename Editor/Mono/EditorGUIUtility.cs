// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditorInternal;
using UnityEngine.Events;
using UnityEngine.Internal;
using UnityEngine.Scripting;
using UnityEngineInternal;
using UnityEditor.StyleSheets;
using UnityEditor.Experimental;
using UnityEditor.SceneManagement;
using UnityEngine.Bindings;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using UnityObject = UnityEngine.Object;

namespace UnityEditor
{
    public sealed partial class EditorGUIUtility : GUIUtility
    {
        internal static void RegisterResourceForCleanupOnDomainReload(UnityObject obj)
        {
            AppDomain.CurrentDomain.DomainUnload += (object sender, EventArgs e) => { UnityObject.DestroyImmediate(obj); };
        }

        public class PropertyCallbackScope : IDisposable
        {
            Action<Rect, SerializedProperty> m_Callback;

            public PropertyCallbackScope(Action<Rect, SerializedProperty> callback)
            {
                m_Callback = callback;

                if (m_Callback != null)
                    EditorGUIUtility.beginProperty += callback;
            }

            public void Dispose()
            {
                if (m_Callback != null)
                    EditorGUIUtility.beginProperty -= m_Callback;
            }
        }

        public class IconSizeScope : GUI.Scope
        {
            private readonly Vector2 m_OriginalIconSize;

            public IconSizeScope(Vector2 iconSizeWithinScope)
            {
                m_OriginalIconSize = GetIconSize();
                SetIconSize(iconSizeWithinScope);
            }

            protected override void CloseScope()
            {
                SetIconSize(m_OriginalIconSize);
            }
        }

        internal static Material s_GUITextureBlit2SRGBMaterial;
        internal static Material GUITextureBlit2SRGBMaterial
        {
            get
            {
                if (!s_GUITextureBlit2SRGBMaterial)
                {
                    Shader shader = LoadRequired("SceneView/GUITextureBlit2SRGB.shader") as Shader;
                    s_GUITextureBlit2SRGBMaterial = new Material(shader);
                    s_GUITextureBlit2SRGBMaterial.hideFlags |= HideFlags.DontSaveInEditor;
                    RegisterResourceForCleanupOnDomainReload(s_GUITextureBlit2SRGBMaterial);
                }
                s_GUITextureBlit2SRGBMaterial.SetFloat("_ManualTex2SRGB", QualitySettings.activeColorSpace == ColorSpace.Linear ? 1.0f : 0.0f);
                return s_GUITextureBlit2SRGBMaterial;
            }
        }

        internal static Material s_GUITextureBlitSceneGUI;
        internal static Material GUITextureBlitSceneGUIMaterial
        {
            get
            {
                if (!s_GUITextureBlitSceneGUI)
                {
                    Shader shader = LoadRequired("SceneView/GUITextureBlitSceneGUI.shader") as Shader;
                    s_GUITextureBlitSceneGUI = new Material(shader);
                    s_GUITextureBlitSceneGUI.hideFlags |= HideFlags.DontSaveInEditor;
                    RegisterResourceForCleanupOnDomainReload(s_GUITextureBlitSceneGUI);
                }
                return s_GUITextureBlitSceneGUI;
            }
        }

        internal static int s_FontIsBold = -1;
        internal static int s_LastControlID = 0;
        private static float s_LabelWidth = 0f;

        private static ScalableGUIContent s_InfoIcon;
        private static ScalableGUIContent s_WarningIcon;
        private static ScalableGUIContent s_ErrorIcon;

        private static GUIStyle s_WhiteTextureStyle;
        private static GUIStyle s_BasicTextureStyle;

        static Hashtable s_TextGUIContents = new Hashtable();
        static Hashtable s_GUIContents = new Hashtable();
        static Hashtable s_IconGUIContents = new Hashtable();
        static Hashtable s_SkinnedIcons = new Hashtable();

        private static readonly GUIContent s_ObjectContent = new GUIContent();
        private static readonly GUIContent s_Text = new GUIContent();
        private static readonly GUIContent s_Image = new GUIContent();
        private static readonly GUIContent s_TextImage = new GUIContent();

        private static GUIContent s_SceneMismatch = TrTextContent("Scene mismatch (cross scene references not supported)");
        private static GUIContent s_TypeMismatch = TrTextContent("Type mismatch");

        internal static readonly SVC<Color> kViewBackgroundColor = new SVC<Color>("view", StyleCatalogKeyword.backgroundColor, GetDefaultBackgroundColor);

        /// The current UI scaling factor for high-DPI displays. For instance, 2.0 on a retina display

        public new static float pixelsPerPoint => GUIUtility.pixelsPerPoint;

        static EditorGUIUtility()
        {
            GUISkin.m_SkinChanged += SkinChanged;
            s_HasCurrentWindowKeyFocusFunc = HasCurrentWindowKeyFocus;
        }

        // this method gets called on right clicking a property regardless of GUI.enable value.
        internal static event Action<GenericMenu, SerializedProperty> contextualPropertyMenu;
        internal static event Action<Rect, SerializedProperty> beginProperty;

        internal static void BeginPropertyCallback(Rect totalRect, SerializedProperty property)
        {
            beginProperty?.Invoke(totalRect, property);
        }

        internal static void ContextualPropertyMenuCallback(GenericMenu gm, SerializedProperty prop)
        {
            if (contextualPropertyMenu != null)
            {
                if (gm.GetItemCount() > 0)
                    gm.AddSeparator("");
                contextualPropertyMenu(gm, prop);
            }
        }

        // returns position and size of the main Unity Editor window
        public static Rect GetMainWindowPosition()
        {
            foreach (var win in ContainerWindow.windows)
            {
                if (win.IsMainWindow())
                    return win.position;
            }
            return new Rect(0, 0, 1000, 600);
        }

        // sets position and size of the main Unity Editor window
        public static void SetMainWindowPosition(Rect position)
        {
            foreach (var win in ContainerWindow.windows)
            {
                if (win.IsMainWindow())
                {
                    win.position = position;
                    break;
                }
            }
        }

        internal static Rect GetCenteredWindowPosition(Rect parentWindowPosition, Vector2 size)
        {
            var pos = new Rect
            {
                x = 0,
                y = 0,
                width = Mathf.Min(size.x, parentWindowPosition.width * 0.90f),
                height = Mathf.Min(size.y, parentWindowPosition.height * 0.90f)
            };
            var w = (parentWindowPosition.width - pos.width) * 0.5f;
            var h = (parentWindowPosition.height - pos.height) * 0.5f;
            pos.x = parentWindowPosition.x + w;
            pos.y = parentWindowPosition.y + h;
            return pos;
        }

        internal static void RepaintCurrentWindow()
        {
            CheckOnGUI();
            GUIView.current.Repaint();
        }

        internal static bool HasCurrentWindowKeyFocus()
        {
            CheckOnGUI();
            return GUIView.current != null && GUIView.current.hasFocus;
        }

        public static Rect PointsToPixels(Rect rect)
        {
            GUIUtility.WarnOnGUI();
            var cachedPixelsPerPoint = pixelsPerPoint;
            rect.x *= cachedPixelsPerPoint;
            rect.y *= cachedPixelsPerPoint;
            rect.width *= cachedPixelsPerPoint;
            rect.height *= cachedPixelsPerPoint;
            return rect;
        }

        public static Rect PixelsToPoints(Rect rect)
        {
            var cachedInvPixelsPerPoint = 1f / pixelsPerPoint;
            rect.x *= cachedInvPixelsPerPoint;
            rect.y *= cachedInvPixelsPerPoint;
            rect.width *= cachedInvPixelsPerPoint;
            rect.height *= cachedInvPixelsPerPoint;
            return rect;
        }

        public static Vector2 PointsToPixels(Vector2 position)
        {
            GUIUtility.WarnOnGUI();
            var cachedPixelsPerPoint = pixelsPerPoint;
            position.x *= cachedPixelsPerPoint;
            position.y *= cachedPixelsPerPoint;
            return position;
        }

        public static Vector2 PixelsToPoints(Vector2 position)
        {
            var cachedInvPixelsPerPoint = 1f / pixelsPerPoint;
            position.x *= cachedInvPixelsPerPoint;
            position.y *= cachedInvPixelsPerPoint;
            return position;
        }

        // Given a rectangle, GUI style and a list of items, lay them out sequentially;
        // left to right, top to bottom.
        public static List<Rect> GetFlowLayoutedRects(Rect rect, GUIStyle style, float horizontalSpacing, float verticalSpacing, List<string> items)
        {
            var result = new List<Rect>(items.Count);
            var curPos = rect.position;
            foreach (string item in items)
            {
                var gc = TempContent(item);
                var itemSize = style.CalcSize(gc);
                var itemRect = new Rect(curPos, itemSize);

                // Reached right side, go to next row
                if (curPos.x + itemSize.x + horizontalSpacing >= rect.xMax)
                {
                    curPos.x = rect.x;
                    curPos.y += itemSize.y + verticalSpacing;
                    itemRect.position = curPos;
                }
                result.Add(itemRect);

                // Move next item to the left
                curPos.x += itemSize.x + horizontalSpacing;
            }

            return result;
        }

        internal class SkinnedColor
        {
            Color normalColor;
            Color proColor;

            public SkinnedColor(Color color, Color proColor)
            {
                normalColor = color;
                this.proColor = proColor;
            }

            public SkinnedColor(Color color)
            {
                normalColor = color;
                proColor = color;
            }

            public Color color
            {
                get { return isProSkin ? proColor : normalColor; }

                set
                {
                    if (isProSkin)
                        proColor = value;
                    else
                        normalColor = value;
                }
            }

            public static implicit operator Color(SkinnedColor colorSkin)
            {
                return colorSkin.color;
            }
        }

        private delegate bool HeaderItemDelegate(Rect rectangle, UnityObject[] targets);
        private static List<HeaderItemDelegate> s_EditorHeaderItemsMethods = null;
        internal static Rect DrawEditorHeaderItems(Rect rectangle, UnityObject[] targetObjs, float spacing = 0)
        {
            if (targetObjs.Length == 0 || (targetObjs.Length == 1 && targetObjs[0].GetType() == typeof(System.Object)))
                return rectangle;

            if (comparisonViewMode != ComparisonViewMode.None)
                return rectangle;

            if (s_EditorHeaderItemsMethods == null)
            {
                List<Type> targetObjTypes = new List<Type>();
                var type = targetObjs[0].GetType();
                while (type.BaseType != null)
                {
                    targetObjTypes.Add(type);
                    type = type.BaseType;
                }

                AttributeHelper.MethodInfoSorter methods = AttributeHelper.GetMethodsWithAttribute<EditorHeaderItemAttribute>(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Func<EditorHeaderItemAttribute, bool> filter = (a) => targetObjTypes.Any(c => a.TargetType == c);
                var methodInfos = methods.FilterAndSortOnAttribute(filter, (a) => a.callbackOrder);
                s_EditorHeaderItemsMethods = new List<HeaderItemDelegate>();
                foreach (MethodInfo methodInfo in methodInfos)
                {
                    s_EditorHeaderItemsMethods.Add((HeaderItemDelegate)Delegate.CreateDelegate(typeof(HeaderItemDelegate), methodInfo));
                }
            }

            float spacingToRemove = 0;
            foreach (HeaderItemDelegate @delegate in s_EditorHeaderItemsMethods)
            {
                if (@delegate(rectangle, targetObjs))
                {
                    rectangle.x -= rectangle.width + spacing;
                    spacingToRemove = rectangle.width + spacing;
                }
            }
            rectangle.x += spacingToRemove; // the spacing after a delegate is used to position the next element to draw but the last one is not used so we must remove it before exiting the method

            return rectangle;
        }

        /// <summary>
        /// Use this container and helper class when implementing lock behaviour on a window when also using an <see cref="ActiveEditorTracker"/>.
        /// </summary>
        [Serializable]
        internal class EditorLockTrackerWithActiveEditorTracker : EditorLockTracker
        {
            internal override bool isLocked
            {
                get
                {
                    if (m_Tracker != null)
                    {
                        base.isLocked = m_Tracker.isLocked;
                        return m_Tracker.isLocked;
                    }
                    return base.isLocked;
                }
                set
                {
                    if (m_Tracker != null)
                    {
                        m_Tracker.isLocked = value;
                    }
                    base.isLocked = value;
                }
            }

            [SerializeField, HideInInspector]
            ActiveEditorTracker m_Tracker;

            internal ActiveEditorTracker tracker
            {
                get { return m_Tracker; }
                set
                {
                    m_Tracker = value;
                    if (m_Tracker != null)
                    {
                        isLocked = m_Tracker.isLocked;
                    }
                }
            }
        }

        /// <summary>
        /// Use this container and helper class when implementing lock behaviour on a window.
        /// </summary>
        [Serializable]
        internal class EditorLockTracker
        {
            [Serializable] public class LockStateEvent : UnityEvent<bool> {}
            [HideInInspector]
            internal LockStateEvent lockStateChanged = new LockStateEvent();

            const string k_LockMenuText = "Lock";
            static readonly GUIContent k_LockMenuGUIContent =  TextContent(k_LockMenuText);

            /// <summary>
            /// don't set or get this directly unless from within the <see cref="isLocked"/> property,
            /// as that property also keeps track of the potentially existing tracker in <see cref="EditorLockTrackerWithActiveEditorTracker"/>
            /// </summary>
            [SerializeField, HideInInspector]
            bool m_IsLocked;
            PingData m_Ping = new PingData();

            internal virtual bool isLocked
            {
                get
                {
                    return m_IsLocked;
                }
                set
                {
                    bool wasLocked = m_IsLocked;
                    m_IsLocked = value;

                    if (wasLocked != m_IsLocked)
                    {
                        lockStateChanged.Invoke(m_IsLocked);
                    }
                }
            }

            internal virtual void AddItemsToMenu(GenericMenu menu, bool disabled = false)
            {
                if (disabled)
                {
                    menu.AddDisabledItem(k_LockMenuGUIContent);
                }
                else
                {
                    menu.AddItem(k_LockMenuGUIContent, isLocked, FlipLocked);
                }
            }

            internal virtual void PingIcon()
            {
                m_Ping.isPinging = true;

                if (m_Ping.m_PingStyle == null)
                {
                    m_Ping.m_PingStyle = new GUIStyle("TV Ping");

                    // The default padding is too high for such a small icon and causes the animation to become offset to the left.
                    m_Ping.m_PingStyle.padding = new RectOffset(8, 0, 0, 0);
                }
            }

            internal virtual void StopPingIcon()
            {
                m_Ping.isPinging = false;
            }

            internal bool ShowButton(Rect position, GUIStyle lockButtonStyle, bool disabled = false)
            {
                using (new EditorGUI.DisabledScope(disabled))
                {
                    EditorGUI.BeginChangeCheck();
                    bool newLock = GUI.Toggle(position, isLocked, GUIContent.none, lockButtonStyle);

                    if (m_Ping.isPinging && Event.current.type == EventType.Layout)
                    {
                        m_Ping.m_ContentRect = position;
                        m_Ping.m_ContentRect.width *= 2f;
                        m_Ping.m_AvailableWidth = GUIView.current.position.width;

                        m_Ping.m_ContentDraw = r =>
                        {
                            GUI.Toggle(r, newLock, GUIContent.none, lockButtonStyle);
                        };
                    }

                    m_Ping.HandlePing();

                    if (EditorGUI.EndChangeCheck())
                    {
                        if (newLock != isLocked)
                        {
                            FlipLocked();
                            m_Ping.isPinging = false;
                        }
                    }
                }
                return m_Ping.isPinging;
            }

            void FlipLocked()
            {
                isLocked = !isLocked;
            }
        }

        // Get a texture from its source filename
        public static Texture2D FindTexture(string name)
        {
            return FindTextureByName(name);
        }

        // Get texture from managed type
        internal static Texture2D FindTexture(Type type)
        {
            return FindTextureByType(type);
        }

        public static GUIContent TrTextContent(string key, string text, string tooltip, Texture icon)
        {
            GUIContent gc = (GUIContent)s_GUIContents[key];
            if (gc == null)
            {
                gc = new GUIContent(L10n.Tr(text));
                if (tooltip != null)
                {
                    gc.tooltip = L10n.Tr(tooltip);
                }
                if (icon != null)
                {
                    gc.image = icon;
                }
                s_GUIContents[key] = gc;
            }
            return gc;
        }

        public static GUIContent TrTextContent(string text, string tooltip = null, Texture icon = null)
        {
            string key = string.Format("{0}|{1}", text ?? "", tooltip ?? "");
            return TrTextContent(key, text, tooltip, icon);
        }

        public static GUIContent TrTextContent(string text, string tooltip, string iconName)
        {
            string key = iconName == null ? string.Format("{0}|{1}", text ?? "", tooltip ?? "") :
                string.Format("{0}|{1}|{2}|{3}", text ?? "", tooltip ?? "", iconName, pixelsPerPoint);
            return TrTextContent(key, text, tooltip, LoadIconRequired(iconName));
        }

        public static GUIContent TrTextContent(string text, Texture icon)
        {
            return TrTextContent(text, null, icon);
        }

        public static GUIContent TrTextContentWithIcon(string text, Texture icon)
        {
            return TrTextContent(text, null, icon);
        }

        public static GUIContent TrTextContentWithIcon(string text, string iconName)
        {
            return TrTextContent(text, null, iconName);
        }

        public static GUIContent TrTextContentWithIcon(string text, string tooltip, string iconName)
        {
            return TrTextContent(text, tooltip, iconName);
        }

        public static GUIContent TrTextContentWithIcon(string text, string tooltip, Texture icon)
        {
            return TrTextContent(text, tooltip, icon);
        }

        public static GUIContent TrTextContentWithIcon(string text, string tooltip, MessageType messageType)
        {
            return TrTextContent(text, tooltip, GetHelpIcon(messageType));
        }

        public static GUIContent TrTextContentWithIcon(string text, MessageType messageType)
        {
            return TrTextContentWithIcon(text, null, messageType);
        }

        internal static Texture2D LightenTexture(Texture2D texture)
        {
            if (!texture)
                return texture;
            Texture2D outTexture = new Texture2D(texture.width, texture.height);
            var outColorArray = outTexture.GetPixels();

            var colorArray = texture.GetPixels();
            for (var i = 0; i < colorArray.Length; ++i)
                outColorArray[i] = LightenColor(colorArray[i]);

            outTexture.hideFlags = HideFlags.HideAndDontSave;
            outTexture.SetPixels(outColorArray);
            outTexture.Apply();

            return outTexture;
        }

        internal static Color LightenColor(Color color)
        {
            Color.RGBToHSV(color, out var h, out _, out _);
            var outColor = Color.HSVToRGB((h + 0.5f) % 1, 0f, 0.8f);
            outColor.a = color.a;
            return outColor;
        }

        public static GUIContent TrIconContent(string iconName, string tooltip = null)
        {
            return TrIconContent(iconName, tooltip, false);
        }

        internal static GUIContent TrIconContent(string iconName, string tooltip, bool lightenTexture)
        {
            string key = tooltip == null ? string.Format("{0}|{1}", iconName, pixelsPerPoint) :
                string.Format("{0}|{1}|{2}", iconName, tooltip, pixelsPerPoint);
            GUIContent gc = (GUIContent)s_IconGUIContents[key];
            if (gc != null)
            {
                return gc;
            }
            gc = new GUIContent();

            if (tooltip != null)
            {
                gc.tooltip = L10n.Tr(tooltip);
            }
            gc.image = LoadIconRequired(iconName);
            if (lightenTexture && gc.image is Texture2D tex2D)
                gc.image = LightenTexture(tex2D);
            s_IconGUIContents[key] = gc;
            return gc;
        }

        public static GUIContent TrIconContent(Texture icon, string tooltip = null)
        {
            GUIContent gc = (tooltip != null) ? (GUIContent)s_IconGUIContents[tooltip] : null;
            if (gc != null)
            {
                return gc;
            }
            gc = new GUIContent { image = icon };
            if (tooltip != null)
            {
                gc.tooltip = L10n.Tr(tooltip);
                s_IconGUIContents[tooltip] = gc;
            }

            return gc;
        }

        [ExcludeFromDocs]
        public static GUIContent TrTempContent(string t)
        {
            return TempContent(L10n.Tr(t));
        }

        [ExcludeFromDocs]
        public static GUIContent[] TrTempContent(string[] texts)
        {
            GUIContent[] retval = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++)
                retval[i] = new GUIContent(L10n.Tr(texts[i]));
            return retval;
        }

        [ExcludeFromDocs]
        public static GUIContent[] TrTempContent(string[] texts, string[] tooltips)
        {
            GUIContent[] retval = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++)
                retval[i] = new GUIContent(L10n.Tr(texts[i]), L10n.Tr(tooltips[i]));
            return retval;
        }

        internal static GUIContent TrIconContent<T>(string tooltip = null) where T : UnityObject
        {
            return TrIconContent(FindTexture(typeof(T)), tooltip);
        }

        public static float singleLineHeight => EditorGUI.kSingleLineHeight;
        public static float standardVerticalSpacing => EditorGUI.kControlVerticalSpacing;

        internal static SliderLabels sliderLabels = new SliderLabels();

        internal static GUIContent TextContent(string textAndTooltip)
        {
            if (textAndTooltip == null)
                textAndTooltip = "";

            string key = textAndTooltip;

            GUIContent gc = (GUIContent)s_TextGUIContents[key];
            if (gc == null)
            {
                string[] strings = GetNameAndTooltipString(textAndTooltip);
                gc = new GUIContent(strings[1]);

                if (strings[2] != null)
                {
                    gc.tooltip = strings[2];
                }
                s_TextGUIContents[key] = gc;
            }
            return gc;
        }

        internal static GUIContent TextContentWithIcon(string textAndTooltip, string icon)
        {
            if (textAndTooltip == null)
                textAndTooltip = "";

            if (icon == null)
                icon = "";

            string key = string.Format("{0}|{1}|{2}", textAndTooltip, icon, pixelsPerPoint);

            GUIContent gc = (GUIContent)s_TextGUIContents[key];
            if (gc == null)
            {
                string[] strings = GetNameAndTooltipString(textAndTooltip);
                gc = new GUIContent(strings[1]) { image = LoadIconRequired(icon) };

                // We want to catch missing icons so we can fix them (therefore using LoadIconRequired)

                if (strings[2] != null)
                {
                    gc.tooltip = strings[2];
                }
                s_TextGUIContents[key] = gc;
            }
            return gc;
        }

        private static Color GetDefaultBackgroundColor()
        {
            float kViewBackgroundIntensity = isProSkin ? 0.22f : 0.76f;
            return new Color(kViewBackgroundIntensity, kViewBackgroundIntensity, kViewBackgroundIntensity, 1f);
        }

        // [0] original name, [1] localized name, [2] localized tooltip
        internal static string[] GetNameAndTooltipString(string nameAndTooltip)
        {
            string[] retval = new string[3];

            string[] s1 = nameAndTooltip.Split('|');

            switch (s1.Length)
            {
                case 0:
                    retval[0] = "";
                    retval[1] = "";
                    break;
                case 1:
                    retval[0] = s1[0].Trim();
                    retval[1] = retval[0];
                    break;
                case 2:
                    retval[0] = s1[0].Trim();
                    retval[1] = retval[0];
                    retval[2] = s1[1].Trim();
                    break;
                default:
                    Debug.LogError("Error in Tooltips: Too many strings in line beginning with '" + s1[0] + "'");
                    break;
            }
            return retval;
        }

        internal static Texture2D LoadIconRequired(string name)
        {
            Texture2D tex = LoadIcon(name);

            if (!tex)
                Debug.LogErrorFormat("Unable to load the icon: '{0}'.\nNote that either full project path should be used (with extension) " +
                    "or just the icon name if the icon is located in the following location: '{1}' (without extension, since png is assumed)",
                    name, EditorResources.editorDefaultResourcesPath + EditorResources.iconsPath);

            return tex;
        }

        // Automatically loads version of icon that matches current skin.
        // Equivalent to Texture2DNamed in ObjectImages.cpp
        [VisibleToOtherModules("UnityEditor.UIBuilderModule")]
        internal static Texture2D LoadIcon(string name)
        {
            return LoadIconForSkin(name, skinIndex);
        }

        static readonly List<string> k_UserSideSupportedImageExtensions = new List<string> {".png"};

        // Attempts to load a higher resolution icon if needed
        internal static Texture2D LoadGeneratedIconOrNormalIcon(string name)
        {
            Texture2D icon = null;
            if (GUIUtility.pixelsPerPoint > 1.0f)
            {
                var imageExtension = Path.GetExtension(name);
                if (k_UserSideSupportedImageExtensions.Contains(imageExtension))
                {
                    var newName = $"{Path.GetFileNameWithoutExtension(name)}@2x{imageExtension}";
                    var dirName = Path.GetDirectoryName(name);
                    if (!string.IsNullOrEmpty(dirName))
                        newName = $"{dirName}/{newName}";

                    icon = InnerLoadGeneratedIconOrNormalIcon(newName);
                }
                else
                {
                    icon = InnerLoadGeneratedIconOrNormalIcon(name + "@2x");
                }

                if (icon != null)
                    icon.pixelsPerPoint = 2.0f;
            }

            if (icon == null)
            {
                icon = InnerLoadGeneratedIconOrNormalIcon(name);
            }

            if (icon != null &&
                !Mathf.Approximately(icon.pixelsPerPoint, GUIUtility.pixelsPerPoint) && //scaling are different
                !Mathf.Approximately(GUIUtility.pixelsPerPoint % 1, 0)) //screen scaling is non-integer
            {
                icon.filterMode = FilterMode.Bilinear;
            }

            return icon;
        }

        // Takes a name that already includes d_ if dark skin version is desired.
        // Equivalent to Texture2DSkinNamed in ObjectImages.cpp
        static Texture2D InnerLoadGeneratedIconOrNormalIcon(string name)
        {
            Texture2D tex = Load(EditorResources.generatedIconsPath + name + ".asset") as Texture2D;

            if (!tex)
            {
                tex = Load(EditorResources.iconsPath + name + ".png") as Texture2D;
            }
            if (!tex)
            {
                tex = Load(name) as Texture2D; // Allow users to specify their own project path to an icon (e.g see EditorWindowTitleAttribute)
            }

            return tex;
        }

        internal static Texture2D LoadIconForSkin(string name, int in_SkinIndex)
        {
            if (String.IsNullOrEmpty(name))
                return null;

            if (in_SkinIndex == 0)
                return LoadGeneratedIconOrNormalIcon(name);

            //Remap file name for dark skin
            var newName = "d_" + Path.GetFileName(name);
            var dirName = Path.GetDirectoryName(name);
            if (!string.IsNullOrEmpty(dirName))
                newName = $"{dirName}/{newName}";

            Texture2D tex = LoadGeneratedIconOrNormalIcon(newName);
            if (!tex)
                tex = LoadGeneratedIconOrNormalIcon(name);
            return tex;
        }

        [UsedByNativeCode]
        internal static string GetIconPathFromAttribute(Type type)
        {
            if (Attribute.IsDefined(type, typeof(IconAttribute)))
            {
                var attributes = type.GetCustomAttributes(typeof(IconAttribute), true);
                for (int i = 0, c = attributes.Length; i < c; i++)
                    if (attributes[i] is IconAttribute)
                        return ((IconAttribute)attributes[i]).path;
            }
            return null;
        }

        internal static GUIContent IconContent<T>(string text = null) where T : UnityObject
        {
            return IconContent(FindTexture(typeof(T)), text);
        }

        [ExcludeFromDocs]
        public static GUIContent IconContent(string name)
        {
            return IconContent(name, null, true);
        }

        internal static GUIContent IconContent(string name, bool logError)
        {
            return IconContent(name, null, logError);
        }

        public static GUIContent IconContent(string name, [DefaultValue("null")] string text)
        {
            return IconContent(name, text, true);
        }

        internal static GUIContent IconContent(string name, [DefaultValue("null")] string text, bool logError)
        {
            GUIContent gc = (GUIContent)s_IconGUIContents[name];
            if (gc != null)
            {
                return gc;
            }

            gc = new GUIContent();

            if (text != null)
            {
                string[] strings = GetNameAndTooltipString(text);
                if (strings[2] != null)
                {
                    gc.tooltip = strings[2];
                }
            }
            gc.image = logError ? LoadIconRequired(name) : LoadIcon(name);

            s_IconGUIContents[name] = gc;
            return gc;
        }

        static GUIContent IconContent(Texture icon, string text)
        {
            GUIContent gc = text != null ? (GUIContent)s_IconGUIContents[text] : null;
            if (gc != null)
            {
                return gc;
            }
            gc = new GUIContent { image = icon };

            if (text != null)
            {
                string[] strings = GetNameAndTooltipString(text);
                if (strings[2] != null)
                {
                    gc.tooltip = strings[2];
                }
                s_IconGUIContents[text] = gc;
            }
            return gc;
        }

        // Is the user currently using the pro skin? (RO)
        public static bool isProSkin => skinIndex == 1;

        internal static void Internal_SwitchSkin()
        {
            skinIndex = 1 - skinIndex;
        }

        // Return a GUIContent object with the name and icon of an Object.
        public static GUIContent ObjectContent(UnityObject obj, Type type)
        {
            return ObjectContent(obj, type, ReferenceEquals(obj, null) ? 0 : obj.GetInstanceID());
        }

        internal static GUIContent ObjectContent(UnityObject obj, Type type, bool showNullIcon)
        {
            return ObjectContent(obj, type, ReferenceEquals(obj, null) ? 0 : obj.GetInstanceID(), showNullIcon);
        }

        internal static GUIContent ObjectContent(UnityObject obj, Type type, int instanceID, bool showNullIcon = true)
        {
            if (obj)
            {
                s_ObjectContent.text = GetObjectNameWithInfo(obj);
                s_ObjectContent.image = AssetPreview.GetMiniThumbnail(obj);
            }
            else if (type != null)
            {
                s_ObjectContent.text = GetTypeNameWithInfo(type.Name, instanceID);
                s_ObjectContent.image = showNullIcon ? AssetPreview.GetMiniTypeThumbnail(type) : null;
            }
            else
            {
                s_ObjectContent.text = "<no type>";
                s_ObjectContent.image = null;
            }
            return s_ObjectContent;
        }

        internal static GUIContent ObjectContent(UnityObject obj, Type type, SerializedProperty property, EditorGUI.ObjectFieldValidator validator = null)
        {
            if (validator == null)
                validator = EditorGUI.ValidateObjectFieldAssignment;

            GUIContent temp;

            // If obj or objType are both null, we have to rely on
            // property.objectReferenceStringValue to display None/Missing and the
            // correct type. But if not, EditorGUIUtility.ObjectContent is more reliable.
            // It can take a more specific object type specified as argument into account,
            // and it gets the icon at the same time.
            if (obj == null && type == null && property != null && property.isValid)
            {
                temp = TempContent(property.objectReferenceStringValue);
            }
            else
            {
                // In order for ObjectContext to be able to distinguish between None/Missing,
                // we need to supply an instanceID. For some reason, getting the instanceID
                // from property.objectReferenceValue is not reliable, so we have to
                // explicitly check property.objectReferenceInstanceIDValue if a property exists.
                if (property != null && property.isValid)
                    temp = ObjectContent(obj, type, property.objectReferenceInstanceIDValue, false);
                else
                    temp = ObjectContent(obj, type, false);
            }

            if (property != null && property.isValid)
            {
                if (obj != null)
                {
                    UnityObject[] references = { obj };
                    if (EditorSceneManager.preventCrossSceneReferences && EditorGUI.CheckForCrossSceneReferencing(obj, property.serializedObject.targetObject))
                    {
                        if (!EditorApplication.isPlaying)
                            temp = s_SceneMismatch;
                        else
                            temp.text = temp.text + string.Format(" ({0})", EditorGUI.GetGameObjectFromObject(obj).scene.name);
                    }
                    else if (validator(references, type, property, EditorGUI.ObjectFieldValidatorOptions.ExactObjectTypeValidation) == null)
                        temp = s_TypeMismatch;
                }
            }

            return temp;
        }

        internal static GUIContent TempContent(string t)
        {
            s_Text.image = null;
            s_Text.text = t;
            s_Text.tooltip = null;
            return s_Text;
        }

        internal static GUIContent TempContent(string text, string tip)
        {
            s_Text.image = null;
            s_Text.text = text;
            s_Text.tooltip = tip;
            return s_Text;
        }

        internal static GUIContent TempContent(Texture i)
        {
            s_Image.image = i;
            s_Image.text = null;
            s_Image.tooltip = null;
            return s_Image;
        }

        internal static GUIContent TempContent(string t, Texture i)
        {
            s_TextImage.image = i;
            s_TextImage.text = t;
            s_TextImage.tooltip = null;
            return s_TextImage;
        }

        internal static GUIContent[] TempContent(string[] texts)
        {
            GUIContent[] retval = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++)
                retval[i] = new GUIContent(texts[i]);
            return retval;
        }

        internal static GUIContent[] TempContent(string[] texts, string[] tooltips)
        {
            GUIContent[] retval = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++)
                retval[i] = new GUIContent(texts[i], tooltips[i]);
            return retval;
        }

        internal static bool HasHolddownKeyModifiers(Event evt)
        {
            return evt.shift | evt.control | evt.alt | evt.command;
        }

        // Does a given class have per-object thumbnails?
        public static bool HasObjectThumbnail(Type objType)
        {
            return objType != null && (objType.IsSubclassOf(typeof(Texture)) || objType == typeof(Texture) || objType == typeof(Sprite));
        }

        // Get the size that has been set using ::ref::SetIconSize.
        public static Vector2 GetIconSize()
        {
            //FIXME: this is how it really should be, but right now it seems to fail badly (unrelated null ref exceptions and then crash)
            return Internal_GetIconSize();
        }

        internal static Texture2D infoIcon
        {
            get
            {
                if (s_InfoIcon == null)
                    s_InfoIcon = new ScalableGUIContent("console.infoicon");
                return s_InfoIcon.image as Texture2D;
            }
        }
        internal static Texture2D warningIcon
        {
            get
            {
                if (s_WarningIcon == null)
                    s_WarningIcon = new ScalableGUIContent("console.warnicon");
                return s_WarningIcon.image as Texture2D;
            }
        }

        internal static Texture2D errorIcon
        {
            get
            {
                if (s_ErrorIcon == null)
                    s_ErrorIcon = new ScalableGUIContent("console.erroricon");
                return s_ErrorIcon.image as Texture2D;
            }
        }

        internal static Texture2D GetHelpIcon(MessageType type)
        {
            switch (type)
            {
                case MessageType.Info:
                    return infoIcon;
                case MessageType.Warning:
                    return warningIcon;
                case MessageType.Error:
                    return errorIcon;
            }
            return null;
        }

        // An invisible GUIContent that is not the same as GUIContent.none
        internal static GUIContent blankContent { get; } = new GUIContent(" ");

        internal static GUIStyle whiteTextureStyle => s_WhiteTextureStyle ??
        (s_WhiteTextureStyle = new GUIStyle {normal = {background = whiteTexture}});

        internal static GUIStyle GetBasicTextureStyle(Texture2D tex)
        {
            if (s_BasicTextureStyle == null)
                s_BasicTextureStyle = new GUIStyle();

            s_BasicTextureStyle.normal.background = tex;

            return s_BasicTextureStyle;
        }

        internal static void NotifyLanguageChanged(SystemLanguage newLanguage)
        {
            s_TextGUIContents = new Hashtable();
            s_GUIContents = new Hashtable();
            s_IconGUIContents = new Hashtable();
            L10n.ClearCache();
            EditorUtility.Internal_UpdateMenuTitleForLanguage(newLanguage);
            LocalizationDatabase.currentEditorLanguage = newLanguage;
            EditorTextSettings.UpdateLocalizationFontAsset();
            EditorApplication.RequestRepaintAllViews();
        }

        // Get one of the built-in GUI skins, which can be the game view, inspector or scene view skin as chosen by the parameter.
        public static GUISkin GetBuiltinSkin(EditorSkin skin)
        {
            return GUIUtility.GetBuiltinSkin((int)skin);
        }

        // Load a built-in resource that has to be there.
        public static UnityObject LoadRequired(string path)
        {
            var o = Load(path, typeof(UnityObject));
            if (!o)
                Debug.LogError("Unable to find required resource at " + path);
            return o;
        }

        // Load a built-in resource
        public static UnityObject Load(string path)
        {
            return Load(path, typeof(UnityObject));
        }

        [TypeInferenceRule(TypeInferenceRules.TypeReferencedBySecondArgument)]
        private static UnityObject Load(string filename, Type type)
        {
            var asset = EditorResources.Load(filename, type);
            if (asset != null)
                return asset;

            AssetBundle bundle = GetEditorAssetBundle();
            if (bundle == null)
            {
                // If in batch mode, loading any Editor UI items shouldn't be needed
                if (Application.isBatchMode)
                    return null;
                throw new NullReferenceException("Failure to load editor resource asset bundle.");
            }

            asset = bundle.LoadAsset(filename, type);
            if (asset != null)
            {
                asset.hideFlags |= HideFlags.HideAndDontSave;
                return asset;
            }

            return AssetDatabase.LoadAssetAtPath(filename, type);
        }

        public static void PingObject(UnityObject obj)
        {
            if (obj != null)
                PingObject(obj.GetInstanceID());
        }

        // Ping an object in a window like clicking it in an inspector
        public static void PingObject(int targetInstanceID)
        {
            foreach (SceneHierarchyWindow shw in SceneHierarchyWindow.GetAllSceneHierarchyWindows())
            {
                shw.FrameObject(targetInstanceID, true);
            }

            foreach (ProjectBrowser pb in ProjectBrowser.GetAllProjectBrowsers())
            {
                pb.FrameObject(targetInstanceID, true);
            }
        }

        // Same as PingObject, but renamed to avoid ambiguity when calling externally (i.e. using CallStaticMonoMethod)
        [RequiredByNativeCode]
        private static void PingObjectFromCPP(int targetInstanceID)
        {
            PingObject(targetInstanceID);
        }

        internal static void MoveFocusAndScroll(bool forward)
        {
            int prev = keyboardControl;
            Internal_MoveKeyboardFocus(forward);
            if (prev != keyboardControl)
                RefreshScrollPosition();
        }

        internal static void RefreshScrollPosition()
        {
            Rect r;

            if (Internal_GetKeyboardRect(keyboardControl, out r))
            {
                GUI.ScrollTo(r);
            }
        }

        internal static void ScrollForTabbing(bool forward)
        {
            Rect r;

            if (Internal_GetKeyboardRect(Internal_GetNextKeyboardControlID(forward), out r))
            {
                GUI.ScrollTo(r);
            }
        }

        internal static void ResetGUIState()
        {
            GUI.skin = null;
            GUI.backgroundColor = GUI.contentColor = Color.white;
            GUI.color = EditorApplication.isPlayingOrWillChangePlaymode ? HostView.kPlayModeDarken : Color.white;
            GUI.enabled = true;
            GUI.changed = false;
            EditorGUI.indentLevel = 0;
            EditorGUI.ClearStacks();
            fieldWidth = 0;
            labelWidth = 0;
            currentViewWidth = -1f;

            SetBoldDefaultFont(false);
            UnlockContextWidth();
            hierarchyMode = false;
            wideMode = false;
            comparisonViewMode = ComparisonViewMode.None;
            leftMarginCoord = 0;

            //Clear the cache, so it uses the global one
            ScriptAttributeUtility.propertyHandlerCache = null;
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("RenderGameViewCameras is no longer supported.Consider rendering cameras manually.", true)]
        public static void RenderGameViewCameras(Rect cameraRect, bool gizmos, bool gui) {}

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("RenderGameViewCameras is no longer supported.Consider rendering cameras manually.", true)]
        public static void RenderGameViewCameras(Rect cameraRect, Rect statsRect, bool gizmos, bool gui) {}

        // Called from C++ GetControlID method when run from the Editor.
        // Editor GUI needs some additional things to happen when calling GetControlID.
        // While this will also be called for runtime code running in Play mode in the Editor,
        // it won't have any effect. EditorGUIUtility.s_LastControlID will be set to the id,
        // but this is only used inside the handling of a single control
        // (see DoPropertyFieldKeyboardHandling).
        // EditorGUI.s_PrefixLabel.text will only be not null when EditorGUI.PrefixLabel
        // has been called without a specified controlID. The control following the PrefixLabel clears this.
        [RequiredByNativeCode]
        internal static void HandleControlID(int id)
        {
            s_LastControlID = id;
            EditorGUI.PrepareCurrentPrefixLabel(s_LastControlID);
        }

        public static bool editingTextField
        {
            get { return EditorGUI.RecycledTextEditor.s_ActuallyEditing; }
            set { EditorGUI.RecycledTextEditor.s_ActuallyEditing = value; }
        }

        internal static bool renameWasCompleted
        {
            get { return EditorGUI.RecycledTextEditor.s_EditingWasCompleted; }
            set { EditorGUI.RecycledTextEditor.s_EditingWasCompleted = value; }
        }

        public static bool textFieldHasSelection
        {
            get { return EditorGUI.s_RecycledEditor.hasSelection; }
        }

        // hierarchyMode changes how foldouts are drawn so the foldout triangle is drawn to the left,
        // outside the rect of the control, rather than inside the rect.
        // This way the text of the foldout lines up with the labels of other controls.
        // hierarchyMode is primarily enabled for editors in the Inspector.
        public static bool hierarchyMode { get; set; } = false;

        // wideMode is used when the Inspector is wide and uses a more tidy and vertically compact layout for certain controls.
        public static bool wideMode { get; set; } = false;

        internal enum ComparisonViewMode
        {
            None, Original, Modified
        }

        // ComparisonViewMode is used when editors are drawn in the context of showing differences between different objects.
        // Controls that must not be used in this context can be hidden or disabled.
        private static ComparisonViewMode s_ComparisonViewMode = ComparisonViewMode.None;
        internal static ComparisonViewMode comparisonViewMode
        {
            get { return s_ComparisonViewMode; }
            set { s_ComparisonViewMode = value; }
        }

        private static float s_LeftMarginCoord;
        internal static float leftMarginCoord
        {
            get { return s_LeftMarginCoord; }
            set { s_LeftMarginCoord = value; }
        }

        // Context width is used for calculating the label width for various editor controls.
        // In most cases the top level clip rect is a perfect context width.
        private static Stack<float> s_ContextWidthStack = new Stack<float>(10);

        private static float CalcContextWidth()
        {
            float output = GUIClip.GetTopRect().width;
            // If there's no top clip rect, fallback to using screen width.
            if (output < 1f || output >= 40000)
                output = currentViewWidth;

            return output;
        }

        internal static void LockContextWidth()
        {
            s_ContextWidthStack.Push(CalcContextWidth());
        }

        internal static void UnlockContextWidth()
        {
            if (s_ContextWidthStack.Count > 0)
            {
                s_ContextWidthStack.Pop();
            }
        }

        internal static float contextWidth
        {
            get
            {
                if (s_ContextWidthStack.Count > 0 && s_ContextWidthStack.Peek() > 0f)
                    return s_ContextWidthStack.Peek();

                return CalcContextWidth();
            }
        }

        private static float s_OverriddenViewWidth = -1f;
        public static float currentViewWidth
        {
            get
            {
                if (s_OverriddenViewWidth > 0f)
                    return s_OverriddenViewWidth;
                CheckOnGUI();
                return GUIView.current ? GUIView.current.position.width : 0;
            }
            internal set
            {
                s_OverriddenViewWidth = value;
            }
        }

        public static float labelWidth
        {
            get
            {
                if (s_LabelWidth > 0)
                    return s_LabelWidth;

                if (hierarchyMode)
                    return Mathf.Max(Mathf.Ceil(contextWidth * EditorGUI.kLabelWidthRatio) - EditorGUI.kLabelWidthMargin, EditorGUI.kMinLabelWidth);
                return 150;
            }
            set { s_LabelWidth = value; }
        }

        private static float s_FieldWidth = 0f;
        public static float fieldWidth
        {
            get
            {
                if (s_FieldWidth > 0)
                    return s_FieldWidth;

                return 50;
            }
            set { s_FieldWidth = value; }
        }

        // Make all ref::EditorGUI look like regular controls.
        private const string k_LookLikeControlsObsoleteMessage = "LookLikeControls and LookLikeInspector modes are deprecated.Use EditorGUIUtility.labelWidth and EditorGUIUtility.fieldWidth to control label and field widths.";
        [Obsolete(k_LookLikeControlsObsoleteMessage, false)]
        public static void LookLikeControls(float _labelWidth, float _fieldWidth)
        {
            fieldWidth = _fieldWidth;
            labelWidth = _labelWidth;
        }

        [ExcludeFromDocs, Obsolete(k_LookLikeControlsObsoleteMessage, false)] public static void LookLikeControls(float _labelWidth) { LookLikeControls(_labelWidth, 0); }
        [ExcludeFromDocs, Obsolete(k_LookLikeControlsObsoleteMessage, false)] public static void LookLikeControls() { LookLikeControls(0, 0); }

        // Make all ::ref::EditorGUI look like simplified outline view controls.
        [Obsolete("LookLikeControls and LookLikeInspector modes are deprecated.", false)]
        public static void LookLikeInspector()
        {
            fieldWidth = 0;
            labelWidth = 0;
        }

        [Obsolete("This field is no longer used by any builtin controls. If passing this field to GetControlID, explicitly use the FocusType enum instead.", false)]
        public static FocusType native = FocusType.Keyboard;

        internal static void SkinChanged()
        {
            EditorStyles.UpdateSkinCache();
        }

        internal static Rect DragZoneRect(Rect position, bool hasLabel = true)
        {
            return new Rect(position.x, position.y, hasLabel ? labelWidth : 0, position.height);
        }

        internal static void MoveArrayExpandedState(SerializedProperty elements, int oldActiveElement, int newActiveElement)
        {
            SerializedProperty prop1 = elements.GetArrayElementAtIndex(oldActiveElement);
            SerializedProperty prop2;
            int depth;
            List<bool> tempIsExpanded = ListPool<bool>.Get();
            var tempProp = prop1;
            tempIsExpanded.Add(prop1.isExpanded);
            bool clearGradientCache = false;
            int next = (oldActiveElement < newActiveElement) ? 1 : -1;

            for (int i = oldActiveElement + next;
                 (oldActiveElement < newActiveElement) ? i <= newActiveElement : i >= newActiveElement;
                 i += next)
            {
                prop2 = elements.GetArrayElementAtIndex(i);

                var cprop1 = prop1.Copy();
                var cprop2 = prop2.Copy();
                depth = Math.Min(cprop1.depth, cprop2.depth);
                while (cprop1.NextVisible(true) && cprop1.depth > depth && cprop2.NextVisible(true) && cprop2.depth > depth)
                {
                    if (cprop1.hasVisibleChildren && cprop2.hasVisibleChildren)
                    {
                        tempIsExpanded.Add(cprop1.isExpanded);
                        cprop1.isExpanded = cprop2.isExpanded;
                    }
                }

                prop1.isExpanded = prop2.isExpanded;
                if (prop1.propertyType == SerializedPropertyType.Gradient)
                    clearGradientCache = true;
                prop1 = prop2;
            }

            prop1.isExpanded = tempIsExpanded[0];
            depth = Math.Min(prop1.depth, tempProp.depth);
            int k = 1;
            while (prop1.NextVisible(true) && prop1.depth > depth && tempProp.NextVisible(true) && tempProp.depth > depth)
            {
                if (prop1.hasVisibleChildren && tempProp.hasVisibleChildren && tempIsExpanded.Count > k)
                {
                    prop1.isExpanded = tempIsExpanded[k];
                    k++;
                }
            }
            ListPool<bool>.Release(tempIsExpanded);

            if (clearGradientCache)
                GradientPreviewCache.ClearCache();
        }

        internal static void SetBoldDefaultFont(bool isBold)
        {
            int wantsBold = isBold ? 1 : 0;
            if (wantsBold != s_FontIsBold)
            {
                SetDefaultFont(isBold ? EditorStyles.boldFont : EditorStyles.standardFont);
                s_FontIsBold = wantsBold;
            }
        }

        internal static bool GetBoldDefaultFont() { return s_FontIsBold == 1; }

        // Creates an event
        public static Event CommandEvent(string commandName)
        {
            Event e = new Event();
            Internal_SetupEventValues(e);
            e.type = EventType.ExecuteCommand;
            e.commandName = commandName;
            return e;
        }

        // Draw a color swatch.
        public static void DrawColorSwatch(Rect position, Color color)
        {
            DrawColorSwatch(position, color, true);
        }

        internal static void DrawColorSwatch(Rect position, Color color, bool showAlpha)
        {
            DrawColorSwatch(position, color, showAlpha, false);
        }

        internal static void DrawColorSwatch(Rect position, Color color, bool showAlpha, bool hdr)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color oldColor = GUI.color;
            Color oldBackgroundColor = GUI.backgroundColor;

            float a = GUI.enabled ? 1 : 2;

            GUI.color = EditorGUI.showMixedValue ? new Color(0.82f, 0.82f, 0.82f, a) * oldColor : new Color(color.r, color.g, color.b, a);
            if (hdr)
                GUI.color = GUI.color.gamma;
            GUI.backgroundColor = Color.white;

            GUIStyle gs = whiteTextureStyle;
            gs.Draw(position, false, false, false, false);

            // Render LDR -> HDR gradients on the sides when having HDR values (to let the user see what the normalized color looks like)
            if (hdr)
            {
                Color32 baseColor;
                float exposure;
                ColorMutator.DecomposeHdrColor(GUI.color.linear, out baseColor, out exposure);

                if (!Mathf.Approximately(exposure, 0f))
                {
                    float gradientWidth = position.width / 3f;
                    Rect leftRect = new Rect(position.x, position.y, gradientWidth, position.height);
                    Rect rightRect = new Rect(position.xMax - gradientWidth, position.y, gradientWidth,
                        position.height);

                    Color orgColor = GUI.color;
                    GUI.color = ((Color)baseColor).gamma;
                    GUIStyle basicStyle = GetBasicTextureStyle(whiteTexture);
                    basicStyle.Draw(leftRect, false, false, false, false);
                    basicStyle.Draw(rightRect, false, false, false, false);
                    GUI.color = orgColor;

                    basicStyle = GetBasicTextureStyle(ColorPicker.GetGradientTextureWithAlpha0To1());
                    basicStyle.Draw(leftRect, false, false, false, false);
                    basicStyle = GetBasicTextureStyle(ColorPicker.GetGradientTextureWithAlpha1To0());
                    basicStyle.Draw(rightRect, false, false, false, false);
                }
            }

            if (!EditorGUI.showMixedValue)
            {
                if (showAlpha)
                {
                    GUI.color = new Color(0, 0, 0, a);
                    float alphaHeight = Mathf.Clamp(position.height * .2f, 2, 20);
                    Rect alphaBarRect = new Rect(position.x, position.yMax - alphaHeight, position.width, alphaHeight);
                    gs.Draw(alphaBarRect, false, false, false, false);

                    GUI.color = new Color(1, 1, 1, a);
                    alphaBarRect.width *= Mathf.Clamp01(color.a);
                    gs.Draw(alphaBarRect, false, false, false, false);
                }
            }
            else
            {
                EditorGUI.BeginHandleMixedValueContentColor();
                gs.Draw(position, EditorGUI.mixedValueContent, false, false, false, false);
                EditorGUI.EndHandleMixedValueContentColor();
            }

            GUI.color = oldColor;
            GUI.backgroundColor = oldBackgroundColor;

            // HDR label overlay
            if (hdr)
            {
                GUI.Label(new Rect(position.x, position.y, position.width - 3, position.height), "HDR", EditorStyles.centeredGreyMiniLabel);
            }
        }

        internal static void DrawRegionSwatch(Rect position, SerializedProperty property, SerializedProperty property2, Color color, Color bgColor)
        {
            DrawCurveSwatchInternal(position, null, null, property, property2, color, bgColor, false, new Rect(), Color.clear, Color.clear);
        }

        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor)
        {
            DrawCurveSwatchInternal(position, curve, null, property, null, color, bgColor, false, new Rect(), Color.clear, Color.clear);
        }

        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor, Color topFillColor, Color bottomFillColor)
        {
            DrawCurveSwatchInternal(position, curve, null, property, null, color, bgColor, false, new Rect(), topFillColor, bottomFillColor);
        }

        // Draw a curve swatch.
        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor, Color topFillColor, Color bottomFillColor, Rect curveRanges)
        {
            DrawCurveSwatchInternal(position, curve, null, property, null, color, bgColor, true, curveRanges, topFillColor, bottomFillColor);
        }

        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor, Rect curveRanges)
        {
            DrawCurveSwatchInternal(position, curve, null, property, null, color, bgColor, true, curveRanges, Color.clear, Color.clear);
        }

        // Draw swatch with a filled region between two SerializedProperty curves.
        public static void DrawRegionSwatch(Rect position, SerializedProperty property, SerializedProperty property2, Color color, Color bgColor, Rect curveRanges)
        {
            DrawCurveSwatchInternal(position, null, null, property, property2, color, bgColor, true, curveRanges, Color.clear, Color.clear);
        }

        // Draw swatch with a filled region between two curves.
        public static void DrawRegionSwatch(Rect position, AnimationCurve curve, AnimationCurve curve2, Color color, Color bgColor, Rect curveRanges)
        {
            DrawCurveSwatchInternal(position, curve, curve2, null, null, color, bgColor, true, curveRanges, Color.clear, Color.clear);
        }

        private static void DrawCurveSwatchInternal(Rect position, AnimationCurve curve, AnimationCurve curve2, SerializedProperty property, SerializedProperty property2, Color color, Color bgColor, bool useCurveRanges, Rect curveRanges, Color topFillColor, Color bottomFillColor)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            int previewWidth = (int)position.width;
            int previewHeight = (int)position.height;
            int maxTextureDim = SystemInfo.maxTextureSize;

            bool stretchX = previewWidth > maxTextureDim;
            bool stretchY = previewHeight > maxTextureDim;
            if (stretchX)
                previewWidth = Mathf.Min(previewWidth, maxTextureDim);
            if (stretchY)
                previewHeight = Mathf.Min(previewHeight, maxTextureDim);

            // Draw background color
            Color oldColor = GUI.color;
            GUI.color = EditorApplication.isPlayingOrWillChangePlaymode ? bgColor * HostView.kPlayModeDarken : bgColor;
            GUIStyle gs = whiteTextureStyle;
            gs.Draw(position, false, false, false, false);
            GUI.color = oldColor;

            if (property != null && property.hasMultipleDifferentValues)
            {
                // No obvious way to show that curve field has mixed values so we just draw
                // the same content as for text fields since the user at least know what that means.
                EditorGUI.BeginHandleMixedValueContentColor();
                GUI.Label(position, EditorGUI.mixedValueContent, "PreOverlayLabel");
                EditorGUI.EndHandleMixedValueContentColor();
            }
            else
            {
                Texture2D preview = null;
                if (property != null)
                {
                    if (property2 == null)
                        preview = useCurveRanges ? AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, property, color, topFillColor, bottomFillColor, curveRanges) : AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, property, color, topFillColor, bottomFillColor);
                    else
                        preview = useCurveRanges ? AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, property, property2, color, topFillColor, bottomFillColor, curveRanges) : AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, property, property2, color, topFillColor, bottomFillColor);
                }
                else if (curve != null)
                {
                    if (curve2 == null)
                        preview = useCurveRanges ? AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, curve, color, topFillColor, bottomFillColor, curveRanges) : AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, curve, color, topFillColor, bottomFillColor);
                    else
                        preview = useCurveRanges ? AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, curve, curve2, color, topFillColor, bottomFillColor, curveRanges) : AnimationCurvePreviewCache.GetPreview(previewWidth, previewHeight, curve, curve2, color, topFillColor, bottomFillColor);
                }
                gs = GetBasicTextureStyle(preview);

                if (!stretchX && preview)
                    position.width = preview.width;
                if (!stretchY && preview)
                    position.height = preview.height;

                gs.Draw(position, false, false, false, false);
            }
        }

        // Convert a color from RGB to HSV color space.
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("EditorGUIUtility.RGBToHSV is obsolete. Use Color.RGBToHSV instead (UnityUpgradable) -> [UnityEngine] UnityEngine.Color.RGBToHSV(*)", true)]
        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            Color.RGBToHSV(rgbColor, out H, out S, out V);
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("EditorGUIUtility.HSVToRGB is obsolete. Use Color.HSVToRGB instead (UnityUpgradable) -> [UnityEngine] UnityEngine.Color.HSVToRGB(*)", true)]
        public static Color HSVToRGB(float H, float S, float V)
        {
            return Color.HSVToRGB(H, S, V);
        }

        // Convert a set of HSV values to an RGB Color.
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("EditorGUIUtility.HSVToRGB is obsolete. Use Color.HSVToRGB instead (UnityUpgradable) -> [UnityEngine] UnityEngine.Color.HSVToRGB(*)", true)]
        public static Color HSVToRGB(float H, float S, float V, bool hdr)
        {
            return Color.HSVToRGB(H, S, V, hdr);
        }

        // Add a custom mouse pointer to a control
        public static void AddCursorRect(Rect position, MouseCursor mouse)
        {
            AddCursorRect(position, mouse, 0);
        }

        public static void AddCursorRect(Rect position, MouseCursor mouse, int controlID)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUIClip.Unclip(position);
                Rect clip = GUIClip.topmostRect;
                Rect clipped = Rect.MinMaxRect(Mathf.Max(r.x, clip.x), Mathf.Max(r.y, clip.y), Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));

                if (clipped.width <= 0 || clipped.height <= 0)
                    return;
                Internal_AddCursorRect(clipped, mouse, controlID);
            }
        }

        internal static Rect HandleHorizontalSplitter(Rect dragRect, float width, float minLeftSide, float minRightSide)
        {
            // Add a cursor rect indicating we can drag this area
            if (Event.current.type == EventType.Repaint)
                AddCursorRect(dragRect, MouseCursor.SplitResizeLeftRight);

            // Drag splitter
            float deltaX = EditorGUI.MouseDeltaReader(dragRect, true).x;
            if (deltaX != 0f)
                dragRect.x += deltaX;
            float newX = Mathf.Clamp(dragRect.x, minLeftSide, width - minRightSide);

            // We might need to move the splitter position if our area/window size has changed
            if (dragRect.x > width - minRightSide)
                newX = width - minRightSide;

            dragRect.x = Mathf.Clamp(newX, minLeftSide, width - minRightSide);
            return dragRect;
        }

        internal static void DrawHorizontalSplitter(Rect dragRect)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color orgColor = GUI.color;
            Color tintColor = (isProSkin) ? new Color(0.12f, 0.12f, 0.12f, 1.333f) : new Color(0.6f, 0.6f, 0.6f, 1.333f);
            GUI.color = GUI.color * tintColor;
            Rect splitterRect = new Rect(dragRect.x - 1, dragRect.y, 1, dragRect.height);
            GUI.DrawTexture(splitterRect, whiteTexture);
            GUI.color = orgColor;
        }

        internal static EventType magnifyGestureEventType => (EventType)1000;
        internal static EventType swipeGestureEventType => (EventType)1001;
        internal static EventType rotateGestureEventType => (EventType)1002;

        public static void ShowObjectPicker<T>(UnityObject obj, bool allowSceneObjects, string searchFilter, int controlID) where T : UnityObject
        {
            Type objType = typeof(T);
            //case 1113046: Delay the show method when it is called while other object picker is closing
            if (Event.current?.commandName == "ObjectSelectorClosed")
                EditorApplication.delayCall += () => SetupObjectSelector(obj, objType, allowSceneObjects, searchFilter, controlID);
            else
                SetupObjectSelector(obj, objType, allowSceneObjects, searchFilter, controlID);
        }

        private static void SetupObjectSelector(UnityObject obj, Type objType, bool allowSceneObjects, string searchFilter, int controlID)
        {
            ObjectSelector.get.Show(obj, objType, null, allowSceneObjects);
            ObjectSelector.get.objectSelectorID = controlID;
            ObjectSelector.get.searchFilter = searchFilter;
        }

        public static UnityObject GetObjectPickerObject()
        {
            return ObjectSelector.GetCurrentObject();
        }

        public static int GetObjectPickerControlID()
        {
            return ObjectSelector.get.objectSelectorID;
        }

        internal static string GetHyperlinkColorForSkin()
        {
            return skinIndex == EditorResources.darkSkinIndex ? "#40a0ff" : "#0000FF";
        }

        // Enum for tracking what styles the editor uses
        internal enum EditorLook
        {
            // Hasn't been set
            Uninitialized = 0,
            // Looks like regular controls
            LikeControls = 1,
            // Looks like inspector
            LikeInspector = 2
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class BuiltinResource
    {
        public string m_Name;
        public int m_InstanceID;
    }

    internal struct SliderLabels
    {
        public void SetLabels(GUIContent _leftLabel, GUIContent _rightLabel)
        {
            if (Event.current.type == EventType.Repaint)
            {
                leftLabel = _leftLabel;
                rightLabel = _rightLabel;
            }
        }

        public bool HasLabels()
        {
            if (Event.current.type == EventType.Repaint)
            {
                return leftLabel != null && rightLabel != null;
            }
            return false;
        }

        public GUIContent leftLabel;
        public GUIContent rightLabel;
    }

    internal class GUILayoutFadeGroup : GUILayoutGroup
    {
        public float fadeValue;
        public bool wasGUIEnabled;
        public Color guiColor;

        public override void CalcHeight()
        {
            base.CalcHeight();
            minHeight *= fadeValue;
            maxHeight *= fadeValue;
        }
    }
}
