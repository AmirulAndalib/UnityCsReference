// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using VirtualTexturing = UnityEngine.Rendering.VirtualTexturing;

namespace UnityEditor
{
    internal class PreviewHelpers
    {
        //This assumes NPOT RenderTextures since Unity 4.3 has this as a requirment already.
        internal static void AdjustWidthAndHeightForStaticPreview(int textureWidth, int textureHeight, ref int width, ref int height)
        {
            int orgWidth = width;
            int orgHeight = height;

            if (textureWidth <= width && textureHeight <= height)
            {
                // For textures smaller than our wanted width and height we use the textures size
                // to prevent excessive magnification artifacts (as seen in the Asset Store).
                width = textureWidth;
                height = textureHeight;
            }
            else
            {
                // For textures larger than our wanted width and height we ensure to
                // keep aspect ratio of the texture and fit it to best match our wanted width and height.
                float relWidth = width / (float)textureWidth;
                float relHeight = height / (float)textureHeight;

                float scale = Mathf.Min(relHeight, relWidth);

                width = Mathf.RoundToInt(textureWidth * scale);
                height = Mathf.RoundToInt(textureHeight * scale);
            }

            // Ensure we have not scaled size below 2 pixels
            width = Mathf.Clamp(width, 2, orgWidth);
            height = Mathf.Clamp(height, 2, orgHeight);
        }
    }

    internal class TextureMipLevels
    {
        public Texture2D texture { get; }
        public int lastMipmapLevel;
        public bool loadAllMips;

        public TextureMipLevels(Texture2D _texture)
        {
            texture = _texture;
            if (texture)
            {
                lastMipmapLevel = texture.loadedMipmapLevel;
                loadAllMips = texture.loadAllMips;
            }
        }
    }

    [CustomEditor(typeof(Texture2D))]
    [CanEditMultipleObjects]
    internal class TextureInspector : Editor
    {
        class Styles
        {
            public GUIContent smallZoom, largeZoom;
            public GUIStyle toolbarButton, previewSlider, previewSliderThumb, previewLabel, mipLevelLabel;

            public readonly GUIContent[] previewButtonContents =
            {
                EditorGUIUtility.TrIconContent("PreTexRGB"),
                EditorGUIUtility.TrIconContent("PreTexR"),
                EditorGUIUtility.TrIconContent("PreTexG"),
                EditorGUIUtility.TrIconContent("PreTexB"),
                EditorGUIUtility.TrIconContent("PreTexA")
            };

            public readonly GUIContent wrapModeLabel = EditorGUIUtility.TrTextContent("Wrap Mode");
            public readonly GUIContent wrapU = EditorGUIUtility.TrTextContent("U axis");
            public readonly GUIContent wrapV = EditorGUIUtility.TrTextContent("V axis");
            public readonly GUIContent wrapW = EditorGUIUtility.TrTextContent("W axis");

            public readonly GUIContent[] wrapModeContents =
            {
                EditorGUIUtility.TrTextContent("Repeat"),
                EditorGUIUtility.TrTextContent("Clamp"),
                EditorGUIUtility.TrTextContent("Mirror"),
                EditorGUIUtility.TrTextContent("Mirror Once"),
                EditorGUIUtility.TrTextContent("Per-axis")
            };
            public readonly int[] wrapModeValues =
            {
                (int)TextureWrapMode.Repeat,
                (int)TextureWrapMode.Clamp,
                (int)TextureWrapMode.Mirror,
                (int)TextureWrapMode.MirrorOnce,
                -1
            };

            public Styles()
            {
                smallZoom = EditorGUIUtility.IconContent("PreTextureMipMapLow");
                largeZoom = EditorGUIUtility.IconContent("PreTextureMipMapHigh");

                toolbarButton = "toolbarbutton";
                previewSlider = "preSlider";
                previewSliderThumb = "preSliderThumb";
                previewLabel = "toolbarLabel";

                mipLevelLabel = "PreOverlayLabel";
                mipLevelLabel.alignment = TextAnchor.UpperCenter;
                mipLevelLabel.padding.top = 5;
            }
        }
        static Styles s_Styles;

        internal enum PreviewMode
        {
            RGB,
            R,
            G,
            B,
            A,
        }

        internal PreviewMode m_PreviewMode = PreviewMode.RGB;
        public bool showAlpha
        {
            get { return m_PreviewMode == PreviewMode.A; }
        }

        // Plain Texture
        protected SerializedProperty m_WrapU;
        protected SerializedProperty m_WrapV;
        protected SerializedProperty m_WrapW;
        protected SerializedProperty m_FilterMode;
        protected SerializedProperty m_Aniso;

        [SerializeField]
        protected Vector2 m_Pos;

        [SerializeField]
        float m_MipLevel = 0;

        [SerializeField]
        protected float m_ExposureSliderValue = 0.0f;

        protected float m_ExposureSliderMax = 16f; // this value can be altered by the user

        CubemapPreview m_CubemapPreview = new CubemapPreview();
        Texture3DPreview m_Texture3DPreview;
        protected Texture2DArrayPreview m_Texture2DArrayPreview = new Texture2DArrayPreview();

        List<TextureMipLevels> m_TextureMipLevels = new List<TextureMipLevels>();

        public static bool IsNormalMap(Texture t)
        {
            TextureUsageMode mode = TextureUtil.GetUsageMode(t);
            return TextureUtil.IsNormalMapUsageMode(mode);
        }

        protected virtual void OnEnable()
        {
            Initialize();
        }

        void CacheSerializedProperties()
        {
            m_WrapU = serializedObject.FindProperty("m_TextureSettings.m_WrapU");
            m_WrapV = serializedObject.FindProperty("m_TextureSettings.m_WrapV");
            m_WrapW = serializedObject.FindProperty("m_TextureSettings.m_WrapW");
            m_FilterMode = serializedObject.FindProperty("m_TextureSettings.m_FilterMode");
            m_Aniso = serializedObject.FindProperty("m_TextureSettings.m_Aniso");
        }

        void Initialize()
        {
            CacheSerializedProperties();
            RecordTextureMipLevels();
            SetMipLevelDefaultForVT();

            if(m_Texture3DPreview == null)
                m_Texture3DPreview = CreateInstance<Texture3DPreview>();

            if (IsTexture3D())
            {
                m_Texture3DPreview.Texture = target as Texture;
            }
            m_Texture3DPreview.OnEnable();
        }

        //VT textures can be very large and aren't in GPU memory yet. To avoid unnecessary streaming and cache use, we limit the default shown mip resolution.
        private void SetMipLevelDefaultForVT()
        {
            foreach (var t in targets)
            {
                var tex = t as Texture;
                if (EditorGUI.UseVTMaterial(tex))
                {
                    int mips = TextureUtil.GetMipmapCount(tex);
                    const int numMipsFor1K = 11;

                    if (mips > numMipsFor1K)
                    {
                        mipLevel = Mathf.Max(mipLevel, mips - numMipsFor1K); //set to 1024x1024 or less
                    }
                }
            }
        }

        public override void ReloadPreviewInstances()
        {
            SetMipLevelDefaultForVT();
        }

        private void RecordTextureMipLevels()
        {
            m_TextureMipLevels.Clear();
            foreach (var item in targets)
            {
                Texture2D texture = item as Texture2D;
                if (texture)
                {
                    m_TextureMipLevels.Add(new TextureMipLevels(texture));
                    texture.loadAllMips = true;
                }
            }
        }

        private void RestoreLastTextureMipLevels()
        {
            foreach (TextureMipLevels textureInfo in m_TextureMipLevels)
            {
                if (textureInfo.texture == null)
                    continue;

                textureInfo.texture.loadAllMips = textureInfo.loadAllMips;
            }
        }

        protected virtual void OnDisable()
        {
            RestoreLastTextureMipLevels();

            m_TextureMipLevels.Clear();

            m_CubemapPreview.OnDisable();
            m_Texture3DPreview.OnDisable();
            DestroyImmediate(m_Texture3DPreview);
        }

        public override bool RequiresConstantRepaint()
        {
            //Keep repainting if the texture is rendered with a virtual texturing material because we don't know when all texture tiles will be streamed in
            if (hasTargetUsingVTMaterial)
                return true;

            bool mipsHaveChanged = false;

            foreach (TextureMipLevels textureInfo in m_TextureMipLevels)
            {
                if (textureInfo.texture == null)
                    continue;

                // See if texture has new mips loaded
                if (textureInfo.texture.loadedMipmapLevel != textureInfo.lastMipmapLevel)
                {
                    textureInfo.lastMipmapLevel = textureInfo.texture.loadedMipmapLevel;

                    if (textureInfo.lastMipmapLevel == mipLevel)
                    {
                        // Don't early out- We need to finish the loop to update them all this frame
                        mipsHaveChanged = true;
                    }
                }
            }

            return mipsHaveChanged;
        }

        internal void SetCubemapIntensity(float intensity)
        {
            if (m_CubemapPreview != null)
                m_CubemapPreview.SetIntensity(intensity);
        }

        public float GetMipLevelForRendering()
        {
            if (target == null)
                return 0.0f;

            if (IsCubemap())
                return m_CubemapPreview.GetMipLevelForRendering(target as Texture);
            else
                return Mathf.Min(m_MipLevel, TextureUtil.GetMipmapCount(target as Texture) - 1);
        }

        public int GetMipmapLimit(Texture t)
        {
            switch (t)
            {
                case Texture2D tex:
                    return tex.activeMipmapLimit;
                case Texture2DArray tex:
                    return tex.activeMipmapLimit;
                default:
                    return 0;
            }
        }

        public float mipLevel
        {
            get
            {
                if (IsCubemap())
                    return m_CubemapPreview.mipLevel;
                else
                    return m_MipLevel;
            }
            set
            {
                m_CubemapPreview.mipLevel = value;
                m_MipLevel = value;
            }
        }

        // Note: Even though this is a custom editor for Texture2D, the target may not be a Texture2D,
        // since other editors inherit from this one, such as ProceduralTextureInspector.
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            if (IsCubemapArray())
            {
                DoFilterModePopup();
            }
            else
            {
                DoWrapModePopup();
                DoFilterModePopup();
                DoAnisoLevelSlider();
            }

            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck())
                ApplySettingsToTextures();
        }

        internal override void PostSerializedObjectCreation()
        {
            base.PostSerializedObjectCreation();

            Initialize();
        }

        // wrap/filter/aniso editors will change serialized object
        // but in case of textures we need an extra step to ApplySettings (so rendering uses new values)
        // alas we cant have good things: it will be PITA to make sure we always call that after applying changes to serialized object
        // meaning that we need to work without relying on it, hence we do similar to TextureImporter:
        //   use TextureUtil methods to update texture settings from current values of serialized property
        // another possibility would be to do it separately for wrap/filter/aniso
        //   alas for wrapmode i dont see how it can be done clearly and leaving it out seems a bit weird

        protected void ApplySettingsToTextures()
        {
            bool anisoDiffer = m_Aniso.hasMultipleDifferentValues, filterDiffer = m_FilterMode.hasMultipleDifferentValues;
            bool wrapDiffer = m_WrapU.hasMultipleDifferentValues || m_WrapV.hasMultipleDifferentValues || m_WrapW.hasMultipleDifferentValues;

            foreach (Texture tex in targets)
            {
                if (!anisoDiffer)
                    TextureUtil.SetAnisoLevelNoDirty(tex, m_Aniso.intValue);

                if (!filterDiffer)
                    TextureUtil.SetFilterModeNoDirty(tex, (FilterMode)m_FilterMode.intValue);

                if (!wrapDiffer)
                    TextureUtil.SetWrapModeNoDirty(tex, (TextureWrapMode)m_WrapU.intValue, (TextureWrapMode)m_WrapV.intValue, (TextureWrapMode)m_WrapW.intValue);
            }
        }

        static void WrapModeAxisPopup(GUIContent label, SerializedProperty wrapProperty)
        {
            // In texture importer settings, serialized properties for wrap modes can contain -1, which means "use default".
            var wrap = (TextureWrapMode)Mathf.Max(wrapProperty.intValue, 0);
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginProperty(rect, label, wrapProperty);
            wrap = (TextureWrapMode)EditorGUI.EnumPopup(rect, label, wrap);
            EditorGUI.EndProperty();
            if (EditorGUI.EndChangeCheck())
            {
                wrapProperty.intValue = (int)wrap;
            }
        }

        private static bool IsAnyTextureObjectUsingPerAxisWrapMode(Object[] objects, bool isVolumeTexture)
        {
            foreach (var o in objects)
            {
                int u = 0, v = 0, w = 0;
                // the objects can be Textures themselves, or texture-related importers
                if (o is Texture)
                {
                    var ti = (Texture)o;
                    u = (int)ti.wrapModeU;
                    v = (int)ti.wrapModeV;
                    w = (int)ti.wrapModeW;
                }
                if (o is TextureImporter)
                {
                    var ti = (TextureImporter)o;
                    u = (int)ti.wrapModeU;
                    v = (int)ti.wrapModeV;
                    w = (int)ti.wrapModeW;
                }
                if (o is IHVImageFormatImporter)
                {
                    var ti = (IHVImageFormatImporter)o;
                    u = (int)ti.wrapModeU;
                    v = (int)ti.wrapModeV;
                    w = (int)ti.wrapModeW;
                }
                u = Mathf.Max(0, u);
                v = Mathf.Max(0, v);
                w = Mathf.Max(0, w);
                if (u != v)
                {
                    return true;
                }
                if (isVolumeTexture)
                {
                    if (u != w || v != w)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // showPerAxisWrapModes is state of whether "Per-Axis" mode should be active in the main dropdown.
        // It is set automatically if wrap modes in UVW are different, or if user explicitly picks "Per-Axis" option -- when that one is picked,
        // then it should stay true even if UVW wrap modes will initially be the same.
        // enforcePerAxis is used to always show all axis option. In some cases (Preset), we want to show each property separately to make sure users can act on one or another with the context menu.
        // Note: W wrapping mode is only shown when isVolumeTexture is true.
        internal static void WrapModePopup(SerializedProperty wrapU, SerializedProperty wrapV, SerializedProperty wrapW, bool isVolumeTexture, ref bool showPerAxisWrapModes, bool enforcePerAxis)
        {
            if (s_Styles == null)
                s_Styles = new Styles();

            // In texture importer settings, serialized properties for things like wrap modes can contain -1;
            // that seems to indicate "use defaults, user has not changed them to anything" but not totally sure.
            // Show them as Repeat wrap modes in the popups.
            var wu = (TextureWrapMode)Mathf.Max(wrapU.intValue, 0);
            var wv = (TextureWrapMode)Mathf.Max(wrapV.intValue, 0);
            var ww = (TextureWrapMode)Mathf.Max(wrapW.intValue, 0);

            // automatically go into per-axis mode if values are already different
            if (wu != wv) showPerAxisWrapModes = true;
            if (isVolumeTexture)
            {
                if (wu != ww || wv != ww) showPerAxisWrapModes = true;
            }

            // It's not possible to determine whether any single texture in the whole selection is using per-axis wrap modes
            // just from SerializedProperty values. They can only tell if "some values in whole selection are different" (e.g.
            // wrap value on U axis is not the same among all textures), and can return value of "some" object in the selection
            // (typically based on object loading order). So in order for more intuitive behavior with multi-selection,
            // we go over the actual objects when there's >1 object selected and some wrap modes are different.
            if (!showPerAxisWrapModes)
            {
                if (wrapU.hasMultipleDifferentValues || wrapV.hasMultipleDifferentValues || (isVolumeTexture && wrapW.hasMultipleDifferentValues))
                {
                    if (IsAnyTextureObjectUsingPerAxisWrapMode(wrapU.serializedObject.targetObjects, isVolumeTexture))
                    {
                        showPerAxisWrapModes = true;
                    }
                }
            }

            int value = showPerAxisWrapModes || enforcePerAxis ? -1 : (int)wu;

            // main wrap mode popup
            if (enforcePerAxis)
            {
                EditorGUILayout.LabelField(s_Styles.wrapModeLabel);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = !showPerAxisWrapModes && (wrapU.hasMultipleDifferentValues || wrapV.hasMultipleDifferentValues || (isVolumeTexture && wrapW.hasMultipleDifferentValues));
                value = EditorGUILayout.IntPopup(s_Styles.wrapModeLabel, value, s_Styles.wrapModeContents, s_Styles.wrapModeValues);
                if (EditorGUI.EndChangeCheck() && value != -1)
                {
                    // assign the same wrap mode to all axes, and hide per-axis popups
                    wrapU.intValue = value;
                    wrapV.intValue = value;
                    wrapW.intValue = value;
                    showPerAxisWrapModes = false;
                }
                EditorGUI.showMixedValue = false;
            }

            // show per-axis popups if needed
            if (value == -1)
            {
                showPerAxisWrapModes = true;
                EditorGUI.indentLevel++;
                WrapModeAxisPopup(s_Styles.wrapU, wrapU);
                WrapModeAxisPopup(s_Styles.wrapV, wrapV);
                if (isVolumeTexture)
                {
                    WrapModeAxisPopup(s_Styles.wrapW, wrapW);
                }
                EditorGUI.indentLevel--;
            }
        }

        bool m_ShowPerAxisWrapModes = false;
        protected void DoWrapModePopup()
        {
            WrapModePopup(m_WrapU, m_WrapV, m_WrapW, IsTexture3D(), ref m_ShowPerAxisWrapModes, false);
        }

        protected void DoFilterModePopup()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = m_FilterMode.hasMultipleDifferentValues;
            FilterMode filter = (FilterMode)m_FilterMode.intValue;
            filter = (FilterMode)EditorGUILayout.EnumPopup(EditorGUIUtility.TempContent("Filter Mode"), filter);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                m_FilterMode.intValue = (int)filter;
        }

        protected void DoAnisoLevelSlider()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = m_Aniso.hasMultipleDifferentValues;
            int aniso = m_Aniso.intValue;
            aniso = EditorGUILayout.IntSlider("Aniso Level", aniso, 0, 16);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck())
                m_Aniso.intValue = aniso;
            DoAnisoGlobalSettingNote(aniso);
        }

        internal static void DoAnisoGlobalSettingNote(int anisoLevel)
        {
            if (anisoLevel > 1)
            {
                if (QualitySettings.anisotropicFiltering == AnisotropicFiltering.Disable)
                    EditorGUILayout.HelpBox("Anisotropic filtering is disabled for all textures in Quality Settings.", MessageType.Info);
                else if (QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable)
                    EditorGUILayout.HelpBox("Anisotropic filtering is enabled for all textures in Quality Settings.", MessageType.Info);
            }
        }

        protected bool IsCubemap()
        {
            var t = target as Texture;
            return t != null && t.dimension == UnityEngine.Rendering.TextureDimension.Cube;
        }

        bool IsCubemapArray()
        {
            var t = target as Texture;
            return t != null && t.dimension == UnityEngine.Rendering.TextureDimension.CubeArray;
        }

        bool IsTexture3D()
        {
            var t = target as Texture;
            return t != null && t.dimension == UnityEngine.Rendering.TextureDimension.Tex3D;
        }

        bool IsTexture2DArray()
        {
            var t = target as Texture;
            return t != null && t.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray;
        }

        protected float GetExposureValueForTexture(Texture t)
        {
            if (NeedsExposureControl(t))
            {
                return m_ExposureSliderValue;
            }
            return 0.0f;
        }

        // Native NeedsExposureControl doesn't work for RenderTextures (they are hardcoded to return kTexFormatUnknown).
        protected bool NeedsExposureControl(Texture t)
        {
            TextureUsageMode usageMode = TextureUtil.GetUsageMode(t);
            return GraphicsFormatUtility.IsHDRFormat(t.graphicsFormat) || TextureUtil.IsRGBMUsageMode(usageMode) || TextureUtil.IsDoubleLDRUsageMode(usageMode);
        }

        public override void OnPreviewSettings()
        {
            // TextureInspector code is reused for RenderTexture and Cubemap inspectors.
            // Make sure we can handle the situation where target is just a Texture and
            // not a Texture2D. It's also used for large popups for mini texture fields,
            // and while it's being shown the actual texture object might disappear --
            // make sure to handle null targets.
            Texture tex = target as Texture;

            bool alphaOnly = true;
            bool hasAlpha = false;
            bool needsExposureControl = false;
            int mipCount = 1;

            foreach (Texture t in targets)
            {
                if (t == null) // texture might have disappeared while we're showing this in a preview popup
                    continue;

                mipCount = Mathf.Max(mipCount, TextureUtil.GetMipmapCount(t));

                TextureUsageMode mode = TextureUtil.GetUsageMode(t);

                if (!GraphicsFormatUtility.IsAlphaOnlyFormat(t.graphicsFormat))
                    alphaOnly = false;

                if (GraphicsFormatUtility.HasAlphaChannel(t.graphicsFormat))
                {
                    if (mode == TextureUsageMode.Default) // all other texture usage modes don't displayable alpha
                        hasAlpha = true;
                }

                // 3D texture previewer doesn't support an exposure value.
                if (t.dimension != TextureDimension.Tex3D && NeedsExposureControl(t))
                    needsExposureControl = true;
            }

            if (needsExposureControl)
            {
                OnExposureSlider();
            }

            if (IsCubemap())
            {
                m_CubemapPreview.OnPreviewSettings(targets, mipCount, alphaOnly, hasAlpha);
                return;
            }

            if (IsTexture3D())
            {
                m_Texture3DPreview.OnPreviewSettings(targets);
                return;
            }

            if (IsTexture2DArray() && !SystemInfo.supports2DArrayTextures)
                return;

            if (s_Styles == null)
                s_Styles = new Styles();

            List<PreviewMode> previewCandidates = new List<PreviewMode>(5);
            previewCandidates.Add(PreviewMode.RGB);
            previewCandidates.Add(PreviewMode.R);
            previewCandidates.Add(PreviewMode.G);
            previewCandidates.Add(PreviewMode.B);
            previewCandidates.Add(PreviewMode.A);

            if (alphaOnly)
            {
                previewCandidates.Clear();
                previewCandidates.Add(PreviewMode.A);
                m_PreviewMode = PreviewMode.A;
            }
            else if (!hasAlpha)
            {
                previewCandidates.Remove(PreviewMode.A);
            }

            if (previewCandidates.Count > 1 && tex != null && !IsNormalMap(tex))
            {
                int selectedIndex = previewCandidates.IndexOf(m_PreviewMode);
                if (selectedIndex == -1)
                    selectedIndex = 0;

                if (previewCandidates.Contains(PreviewMode.RGB))
                    m_PreviewMode = GUILayout.Toggle(m_PreviewMode == PreviewMode.RGB, s_Styles.previewButtonContents[0], s_Styles.toolbarButton)
                        ? PreviewMode.RGB
                        : m_PreviewMode;
                if (previewCandidates.Contains(PreviewMode.R))
                    m_PreviewMode = GUILayout.Toggle(m_PreviewMode == PreviewMode.R, s_Styles.previewButtonContents[1], s_Styles.toolbarButton)
                        ? PreviewMode.R
                        : m_PreviewMode;
                if (previewCandidates.Contains(PreviewMode.G))
                    m_PreviewMode = GUILayout.Toggle(m_PreviewMode == PreviewMode.G, s_Styles.previewButtonContents[2], s_Styles.toolbarButton)
                        ? PreviewMode.G
                        : m_PreviewMode;
                if (previewCandidates.Contains(PreviewMode.B))
                    m_PreviewMode = GUILayout.Toggle(m_PreviewMode == PreviewMode.B, s_Styles.previewButtonContents[3], s_Styles.toolbarButton)
                        ? PreviewMode.B
                        : m_PreviewMode;
                if (previewCandidates.Contains(PreviewMode.A))
                    m_PreviewMode = GUILayout.Toggle(m_PreviewMode == PreviewMode.A, s_Styles.previewButtonContents[4], s_Styles.toolbarButton)
                        ? PreviewMode.A
                        : m_PreviewMode;
            }

            if (IsTexture2DArray())
            {
                m_Texture2DArrayPreview.OnPreviewSettings(tex);
            }

            if (mipCount > 1)
            {
                int mipmapLimit = GetMipmapLimit(target as Texture);
                GUILayout.Box(s_Styles.smallZoom, s_Styles.previewLabel);
                GUI.changed = false;

                int leftValue = mipCount - mipmapLimit - 1;
                if (m_MipLevel > leftValue)
                {
                    // Left value can change depending on the mipmap limit. Cap slider value appropriately.
                    m_MipLevel = leftValue;
                }
                m_MipLevel = Mathf.Round(GUILayout.HorizontalSlider(m_MipLevel, leftValue, 0, s_Styles.previewSlider, s_Styles.previewSliderThumb, GUILayout.MaxWidth(64)));

                //For now, we don't have mipmaps smaller than the tile size when using VT.
                if (EditorGUI.UseVTMaterial(tex))
                {
                    int numMipsOfTile = (int)Mathf.Log(VirtualTexturing.EditorHelpers.tileSize, 2) + 1;
                    m_MipLevel = Mathf.Min(m_MipLevel, Mathf.Max(mipCount - numMipsOfTile, 0));
                }

                GUILayout.Box(s_Styles.largeZoom, s_Styles.previewLabel);
            }
        }

        public void OnExposureSlider()
        {
            if (s_Styles == null)
                s_Styles = new Styles();
            m_ExposureSliderValue = EditorGUIInternal.ExposureSlider(m_ExposureSliderValue, ref m_ExposureSliderMax, s_Styles.previewSlider);
        }

        public override bool HasPreviewGUI()
        {
            return (target != null);
        }

        internal bool hasTargetUsingVTMaterial
        {
            get
            {
                foreach (var t in targets)
                    if (EditorGUI.UseVTMaterial(t as Texture))
                        return true;

                return false;
            }
        }


        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (Event.current.type == EventType.Repaint)
                background.Draw(r, false, false, false, false);

            // show texture
            Texture t = target as Texture;
            if (t == null) // texture might be gone by now, in case this code is used for floating texture preview
                return;

            GraphicsFormat format = t.graphicsFormat;
            if (!(GraphicsFormatUtility.IsIEEE754Format(format) || GraphicsFormatUtility.IsNormFormat(format)))
            {
                EditorGUI.HelpBox(r, "This preview only supports floating point or normalized formats.", MessageType.Warning);
                return;
            }

            // Render target must be created before we can display it (case 491797)
            RenderTexture rt = t as RenderTexture;
            if (rt != null)
            {
                if (rt.Create() == false)
                {
                    return;
                }
            }

            if (IsCubemap())
            {
                m_CubemapPreview.OnPreviewGUI(t, r, background, GetExposureValueForTexture(t));
                return;
            }

            if (IsTexture3D())
            {
                m_Texture3DPreview.Texture = t;
                m_Texture3DPreview.OnPreviewGUI(r, background);
                return;
            }

            if (IsTexture2DArray())
            {
                m_Texture2DArrayPreview.OnPreviewGUI(t, r, background, GetExposureValueForTexture(t), m_PreviewMode, m_MipLevel);
                return;
            }

            // target can report zero sizes in some cases just after a parameter change;
            // guard against that.
            int texWidth = Mathf.Max(t.width, 1);
            int texHeight = Mathf.Max(t.height, 1);

            float mipLevel = GetMipLevelForRendering();
            float zoomLevel = Mathf.Min(Mathf.Min(r.width / texWidth, r.height / texHeight), 1);
            Rect wantedRect = new Rect(r.x, r.y, texWidth * zoomLevel, texHeight * zoomLevel);
            PreviewGUI.BeginScrollView(r, m_Pos, wantedRect, "PreHorizontalScrollbar", "PreHorizontalScrollbarThumb");
            FilterMode oldFilter = t.filterMode;
            TextureUtil.SetFilterModeNoDirty(t, FilterMode.Point);
            Texture2D t2d = t as Texture2D;
            ColorWriteMask colorWriteMask = ColorWriteMask.All;

            switch (m_PreviewMode)
            {
                case PreviewMode.R:
                    colorWriteMask = ColorWriteMask.Red | ColorWriteMask.Alpha;
                    break;
                case PreviewMode.G:
                    colorWriteMask = ColorWriteMask.Green | ColorWriteMask.Alpha;
                    break;
                case PreviewMode.B:
                    colorWriteMask = ColorWriteMask.Blue | ColorWriteMask.Alpha;
                    break;
            }

            if (m_PreviewMode == PreviewMode.A)
            {
                EditorGUI.DrawTextureAlpha(wantedRect, t, ScaleMode.StretchToFill, 0, mipLevel);
            }
            else
            {
                if (t2d != null && t2d.alphaIsTransparency)
                    EditorGUI.DrawTextureTransparent(wantedRect, t, ScaleMode.StretchToFill, 0, mipLevel,
                        colorWriteMask, GetExposureValueForTexture(t));
                else
                    EditorGUI.DrawPreviewTexture(wantedRect, t, null, ScaleMode.StretchToFill, 0, mipLevel,
                        colorWriteMask, GetExposureValueForTexture(t));
            }

            // TODO: Less hacky way to prevent sprite rects to not appear in smaller previews like icons.
            if ((wantedRect.width > 32 && wantedRect.height > 32) && Event.current.type == EventType.Repaint)
            {
                string path = AssetDatabase.GetAssetPath(t);
                TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                SpriteMetaData[] spritesheet = textureImporter != null ? textureImporter.GetSpriteMetaDatas() : null;

                if (spritesheet != null && textureImporter.spriteImportMode == SpriteImportMode.Multiple)
                {
                    Rect screenRect = new Rect();
                    Rect sourceRect = new Rect();
                    GUI.CalculateScaledTextureRects(wantedRect, ScaleMode.StretchToFill, (float)t.width / (float)t.height, ref screenRect, ref sourceRect);

                    int origWidth = t.width;
                    int origHeight = t.height;
                    textureImporter.GetWidthAndHeight(ref origWidth, ref origHeight);
                    float definitionScale = (float)t.width / (float)origWidth;

                    HandleUtility.ApplyWireMaterial();
                    GL.PushMatrix();
                    GL.MultMatrix(Handles.matrix);
                    GL.Begin(GL.LINES);
                    GL.Color(new Color(1f, 1f, 1f, 0.5f));
                    foreach (SpriteMetaData sprite in spritesheet)
                    {
                        Rect spriteRect = sprite.rect;
                        Rect spriteScreenRect = new Rect();
                        spriteScreenRect.xMin = screenRect.xMin + screenRect.width * (spriteRect.xMin / t.width * definitionScale);
                        spriteScreenRect.xMax = screenRect.xMin + screenRect.width * (spriteRect.xMax / t.width * definitionScale);
                        spriteScreenRect.yMin = screenRect.yMin + screenRect.height * (1f - spriteRect.yMin / t.height * definitionScale);
                        spriteScreenRect.yMax = screenRect.yMin + screenRect.height * (1f - spriteRect.yMax / t.height * definitionScale);
                        DrawRect(spriteScreenRect);
                    }
                    GL.End();
                    GL.PopMatrix();
                }
            }

            TextureUtil.SetFilterModeNoDirty(t, oldFilter);

            int mipmapLimit = GetMipmapLimit(target as Texture);
            int cpuMipLevel = Mathf.Min(TextureUtil.GetMipmapCount(target as Texture) - 1, (int)mipLevel + mipmapLimit);
            m_Pos = PreviewGUI.EndScrollView();
            if (cpuMipLevel != 0)
            {
                GUIContent mipLevelTextContent = new GUIContent((cpuMipLevel != mipLevel)
                        ? string.Format("Mip {0}\nMip {1} on GPU (Texture Limit)", cpuMipLevel, mipLevel)
                        : string.Format("Mip {0}", mipLevel));
                Vector2 size = s_Styles.mipLevelLabel.CalcSize(mipLevelTextContent);
                if (size.x <= r.width)
                {
                    EditorGUI.DropShadowLabel(new Rect(r.x, r.y, r.width, size.y), mipLevelTextContent, s_Styles.mipLevelLabel);
                }
            }
        }

        private void DrawRect(Rect rect)
        {
            GL.Vertex(new Vector3(rect.xMin, rect.yMin, 0f));
            GL.Vertex(new Vector3(rect.xMax, rect.yMin, 0f));
            GL.Vertex(new Vector3(rect.xMax, rect.yMin, 0f));
            GL.Vertex(new Vector3(rect.xMax, rect.yMax, 0f));
            GL.Vertex(new Vector3(rect.xMax, rect.yMax, 0f));
            GL.Vertex(new Vector3(rect.xMin, rect.yMax, 0f));
            GL.Vertex(new Vector3(rect.xMin, rect.yMax, 0f));
            GL.Vertex(new Vector3(rect.xMin, rect.yMin, 0f));
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            if (!ShaderUtil.hardwareSupportsRectRenderTexture)
            {
                return null;
            }

            Texture texture = target as Texture;

            GraphicsFormat format = texture.graphicsFormat;
            if (!(GraphicsFormatUtility.IsIEEE754Format(format) || GraphicsFormatUtility.IsNormFormat(format)))
            {
                // Can't generate correct previews for non-float/norm formats. On Metal and Vulkan this even causes validation errors.
                return null;
            }

            if (IsCubemap())
            {
                return m_CubemapPreview.RenderStaticPreview(texture, width, height, GetExposureValueForTexture(texture));
            }

            if (IsTexture3D())
            {
                return m_Texture3DPreview.RenderStaticPreview(texture, width, height);
            }

            TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (textureImporter != null && textureImporter.textureType == TextureImporterType.Sprite && textureImporter.spriteImportMode == SpriteImportMode.Polygon)
            {
                // If the texture importer is a Sprite of primitive, use the sprite inspector for generating preview/icon.
                if (subAssets.Length > 0)
                {
                    Sprite sprite = subAssets[0] as Sprite;
                    if (sprite)
                        return SpriteInspector.BuildPreviewTexture(sprite, null, true, width, height);
                }
                else
                    return null;
            }

            PreviewHelpers.AdjustWidthAndHeightForStaticPreview(texture.width, texture.height, ref width, ref height);

            RenderTexture savedRT = RenderTexture.active;
            Rect savedViewport = ShaderUtil.rawViewportRect;

            var rt = texture as RenderTexture;
            if (rt != null)
            {
                rt.Create(); // Ensure RT is created. Otherwise the first attempted Blit will end up binding a dummy 2D Texture where it expects a 2D Texture Array. (validation errors observed on Vulkan/Metal)
            }

            RenderTexture tmp = RenderTexture.GetTemporary(
                width, height,
                0,
                SystemInfo.GetGraphicsFormat(DefaultFormat.LDR));
            Material mat = EditorGUI.GetMaterialForSpecialTexture(texture, null, QualitySettings.activeColorSpace == ColorSpace.Linear, false);
            if (mat != null)
                Graphics.Blit(texture, tmp, mat);
            else Graphics.Blit(texture, tmp);

            RenderTexture.active = tmp;
            Texture2D copy;
            Texture2D tex2d = target as Texture2D;
            if (tex2d != null && tex2d.alphaIsTransparency)
            {
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
            else
            {
                copy = new Texture2D(width, height, TextureFormat.RGB24, false);
            }
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply();
            RenderTexture.ReleaseTemporary(tmp);

            EditorGUIUtility.SetRenderTextureNoViewport(savedRT);
            ShaderUtil.rawViewportRect = savedViewport;

            return copy;
        }

        public override string GetInfoString()
        {
            // TextureInspector code is reused for RenderTexture and Cubemap inspectors.
            // Make sure we can handle the situation where target is just a Texture and
            // not a Texture2D.
            Texture t = target as Texture;
            Texture2D t2 = target as Texture2D;
            TextureImporter textureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            string info = t.width + "x" + t.height;

            bool showSize = true;
            bool isPackedSprite = textureImporter && textureImporter.qualifiesForSpritePacking;
            bool isNormalmap = IsNormalMap(t);
            bool stillNeedsCompression = textureImporter && textureImporter.textureStillNeedsToBeCompressed;
            bool isNPOT = t2 != null && TextureUtil.IsNonPowerOfTwo(t2);
            GraphicsFormat format = t.graphicsFormat;

            showSize = !stillNeedsCompression;
            if (isNPOT)
                info += " (NPOT)";
            if (stillNeedsCompression)
                info += " (Not yet compressed)";
            else
            {
                if (isNormalmap)
                {
                    switch (format)
                    {
                        case GraphicsFormat.RGBA_DXT5_SRGB:
                        case GraphicsFormat.RGBA_DXT5_UNorm:
                            info += "  DXTnm";
                            break;
                        case GraphicsFormat.R8G8B8A8_SRGB:
                        case GraphicsFormat.R8G8B8A8_UNorm:
                        case GraphicsFormat.B8G8R8A8_SRGB:
                        case GraphicsFormat.B8G8R8A8_UNorm:
                            info += "  Nm 32 bit";
                            break;
                        case GraphicsFormat.R4G4B4A4_UNormPack16:
                        case GraphicsFormat.B4G4R4A4_UNormPack16:
                            info += "  Nm 16 bit";
                            break;
                        default:
                            info += "  " + GraphicsFormatUtility.GetFormatString(format);
                            break;
                    }
                }
                else if (isPackedSprite)
                {
                    TextureFormat desiredFormat;
                    ColorSpace dummyColorSpace;
                    int dummyComressionQuality;
                    textureImporter.ReadTextureImportInstructions(EditorUserBuildSettings.activeBuildTarget, out desiredFormat, out dummyColorSpace, out dummyComressionQuality);

                    info += "\n " + GraphicsFormatUtility.GetFormatString(format) + "(Original) " + GraphicsFormatUtility.GetFormatString(desiredFormat) + "(Atlas)";
                }
                else
                    info += "  " + GraphicsFormatUtility.GetFormatString(format);
            }

            if (showSize)
                info += "\n" + EditorUtility.FormatBytes(TextureUtil.GetStorageMemorySizeLong(t));

            TextureUsageMode mode = TextureUtil.GetUsageMode(t);

            if (mode == TextureUsageMode.AlwaysPadded)
            {
                var glWidth = TextureUtil.GetGPUWidth(t);
                var glHeight = TextureUtil.GetGPUHeight(t);
                if (t.width != glWidth || t.height != glHeight)
                    info += string.Format("\nPadded to {0}x{1}", glWidth, glHeight);
            }
            else if (TextureUtil.IsRGBMUsageMode(mode))
            {
                info += "\nRGBM encoded";
            }
            else if (TextureUtil.IsDoubleLDRUsageMode(mode))
            {
                info += "\ndLDR encoded";
            }

            return info;
        }

        internal static float PreviewSettingsSlider(GUIContent content, float value, float min, float max, float sliderWidth, float floatFieldWidth, bool isInteger)
        {
            var labelWidth = EditorStyles.label.CalcSize(content).x + 2;
            var controlRect = EditorGUILayout.GetControlRect(GUILayout.Width(labelWidth + sliderWidth + floatFieldWidth));
            var controlId = GUIUtility.GetControlID(FocusType.Keyboard);

            var labelRect = new Rect(controlRect.position, new Vector2(labelWidth, controlRect.height));
            controlRect.x += labelRect.width;
            controlRect.width -= labelRect.width + 2;
            GUI.Label(labelRect, content);

            var sliderRect = new Rect(controlRect.position, new Vector2(sliderWidth, controlRect.height));
            controlRect.x += sliderRect.width + 2;
            controlRect.width -= sliderRect.width;
            value = GUI.Slider(sliderRect, value, 0, min, max, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, true, 0);
            if (isInteger)
                value = Mathf.Round(EditorGUI.DoIntField(EditorGUI.s_RecycledEditor, controlRect, labelRect, controlId, Mathf.RoundToInt(value), EditorGUI.kIntFieldFormatString, EditorStyles.numberField, false, 0));
            else
                value = EditorGUI.DoFloatField(EditorGUI.s_RecycledEditor, controlRect, labelRect, controlId, value, EditorGUI.kFloatFieldFormatString, EditorStyles.numberField, true);
            return Mathf.Clamp(value, min, max);
        }
    }
}


class PreviewGUI
{
    static int sliderHash = "Slider".GetHashCode();
    static Rect s_ViewRect, s_Position;
    static Vector2 s_ScrollPos;

    internal static void BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar)
    {
        s_ScrollPos = scrollPosition;
        s_ViewRect = viewRect;
        s_Position = position;
        GUIClip.Push(position, new Vector2(Mathf.Round(-scrollPosition.x - viewRect.x - (viewRect.width - position.width) * .5f), Mathf.Round(-scrollPosition.y - viewRect.y - (viewRect.height - position.height) * .5f)), Vector2.zero, false);
    }

    internal class Styles
    {
        public static GUIStyle preButton;
        public static void Init()
        {
            preButton = "toolbarbutton";
        }
    }

    public static int CycleButton(int selected, GUIContent[] options)
    {
        Styles.Init();
        return EditorGUILayout.CycleButton(selected, options, Styles.preButton);
    }

    public static Vector2 EndScrollView()
    {
        GUIClip.Pop();

        Rect clipRect = s_Position, position = s_Position, viewRect = s_ViewRect;

        Vector2 scrollPosition = s_ScrollPos;
        switch (Event.current.type)
        {
            case EventType.Layout:
                GUIUtility.GetControlID(sliderHash, FocusType.Passive);
                GUIUtility.GetControlID(sliderHash, FocusType.Passive);
                break;
            case EventType.Used:
                break;
            default:
                bool needsVerticalScrollbar = ((int)viewRect.width > (int)clipRect.width);
                bool needsHorizontalScrollbar = ((int)viewRect.height > (int)clipRect.height);
                int id = GUIUtility.GetControlID(sliderHash, FocusType.Passive);

                if (needsHorizontalScrollbar)
                {
                    GUIStyle horizontalScrollbar = "PreHorizontalScrollbar";
                    GUIStyle horizontalScrollbarThumb = "PreHorizontalScrollbarThumb";
                    float offset = (viewRect.width - clipRect.width) * .5f;
                    scrollPosition.x = GUI.Slider(new Rect(position.x, position.yMax - horizontalScrollbar.fixedHeight, clipRect.width - (needsVerticalScrollbar ? horizontalScrollbar.fixedHeight : 0) , horizontalScrollbar.fixedHeight),
                        scrollPosition.x, clipRect.width + offset, -offset, viewRect.width,
                        horizontalScrollbar, horizontalScrollbarThumb, true, id);
                }
                else
                {
                    // Get the same number of Control IDs so the ID generation for children don't depend on number of things above
                    scrollPosition.x = 0;
                }

                id = GUIUtility.GetControlID(sliderHash, FocusType.Passive);

                if (needsVerticalScrollbar)
                {
                    GUIStyle verticalScrollbar = "PreVerticalScrollbar";
                    GUIStyle verticalScrollbarThumb = "PreVerticalScrollbarThumb";
                    float offset = (viewRect.height - clipRect.height) * .5f;
                    scrollPosition.y = GUI.Slider(new Rect(clipRect.xMax - verticalScrollbar.fixedWidth, clipRect.y, verticalScrollbar.fixedWidth, clipRect.height),
                        scrollPosition.y, clipRect.height + offset, -offset, viewRect.height,
                        verticalScrollbar, verticalScrollbarThumb, false, id);
                }
                else
                {
                    scrollPosition.y = 0;
                }
                break;
        }

        return scrollPosition;
    }

    public static Vector2 Drag2D(Vector2 scrollPosition, Rect position)
    {
        int id = GUIUtility.GetControlID(sliderHash, FocusType.Passive);
        Event evt = Event.current;
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (position.Contains(evt.mousePosition) && position.width > 50)
                {
                    GUIUtility.hotControl = id;
                    evt.Use();
                    EditorGUIUtility.SetWantsMouseJumping(1);
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    scrollPosition -= evt.delta * (evt.shift ? 3 : 1) / Mathf.Min(position.width, position.height) * 140.0f;
                    evt.Use();
                    GUI.changed = true;
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                    GUIUtility.hotControl = 0;
                EditorGUIUtility.SetWantsMouseJumping(0);
                break;
        }
        return scrollPosition;
    }
}
