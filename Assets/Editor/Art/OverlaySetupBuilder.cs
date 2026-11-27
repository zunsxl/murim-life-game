using System;
using System.IO;
using Murim.View;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Murim.Editor.Art
{
    /// <summary>
    /// Batchmode and Editor menu setup for TMP Essential Resources, ART-011 font asset,
    /// ART-009 UI sprite reimport, and SystemOverlay.prefab (§3.5.3, §3.6.1, §4.9, §4.11).
    /// </summary>
    public static class OverlaySetupBuilder
    {
        private const string TmpEssentialPackagePath =
            "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        private const string SourceFontPath = "Assets/Resources/Art/UI/Fonts/LibreBaskerville-Regular.ttf";
        private const string PrefabDirectory = "Assets/Resources/Prefabs";
        private const string SystemOverlayPrefabPath = "Assets/Resources/Prefabs/SystemOverlay.prefab";
        private const string OverlayPanelPath = "Assets/Resources/Art/UI/overlay_panel.png";
        private const string OverlayDividerPath = "Assets/Resources/Art/UI/overlay_divider.png";
        private const string OverlayButtonNormalPath = "Assets/Resources/Art/UI/overlay_button_normal.png";
        private const string OverlayButtonHoverPath = "Assets/Resources/Art/UI/overlay_button_hover.png";
        private const string OverlayButtonPressedPath = "Assets/Resources/Art/UI/overlay_button_pressed.png";

        private static readonly Color32 TextIvory = new Color32(0xF2, 0xEB, 0xDD, 0xFF);
        private static readonly Color ScrimColor = new Color(0f, 0f, 0f, 0.35f);

        [MenuItem("Murim/Setup/Build Overlay Assets And Prefab")]
        public static void BuildAll()
        {
            ImportTmpEssentialsIfNeeded();
            ArtImportPostprocessor.ReimportAllArt();
            TMP_FontAsset overlayFont = CreateOverlaySerifFontAsset();
            ConfigureTmpSettingsAndRemoveLiberationSans(overlayFont);
            BuildSystemOverlayPrefab(overlayFont);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Murim] Overlay assets and SystemOverlay.prefab built successfully.");
        }

        public static void ImportTmpEssentialsIfNeeded()
        {
            string[] existingSettings = AssetDatabase.FindAssets("t:TMP_Settings");
            if (existingSettings.Length > 0 && Shader.Find("TextMeshPro/Mobile/Distance Field") != null)
            {
                return;
            }

            string fullPackagePath = Path.GetFullPath(TmpEssentialPackagePath);
            if (!File.Exists(fullPackagePath))
            {
                throw new FileNotFoundException("TMP Essential Resources package not found.", fullPackagePath);
            }

            AssetDatabase.ImportPackage(fullPackagePath, false);
            AssetDatabase.Refresh();
        }

        public static TMP_FontAsset CreateOverlaySerifFontAsset()
        {
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                throw new InvalidOperationException("Missing source font at " + SourceFontPath);
            }

            uint[] asciiUnicodes = BuildAsciiRange();
            int bestSize = FindMaxFittingPointSize(sourceFont, asciiUnicodes);
            if (bestSize < ArtManifest.TmpMinSamplingPointSize)
            {
                throw new InvalidOperationException(
                    $"Resolved sampling point size {bestSize} is below {ArtManifest.TmpMinSamplingPointSize}.");
            }

            if (AssetDatabase.LoadMainAssetAtPath(ArtManifest.OverlaySerifAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(ArtManifest.OverlaySerifAssetPath);
            }

            TMP_FontAsset fontAsset = BuildPackedFontAsset(sourceFont, asciiUnicodes, bestSize);
            SaveFontAssetToDisk(fontAsset, sourceFont, bestSize);
            Debug.Log($"[Murim] Created OverlaySerif SDF.asset at sampling point size {bestSize}.");
            return fontAsset;
        }

        private static uint[] BuildAsciiRange()
        {
            int count = ArtManifest.FontMaxCodepoint - ArtManifest.FontMinCodepoint + 1;
            var unicodes = new uint[count];
            for (int i = 0; i < count; i++)
            {
                unicodes[i] = (uint)(ArtManifest.FontMinCodepoint + i);
            }

            return unicodes;
        }

        private static int FindMaxFittingPointSize(Font sourceFont, uint[] asciiUnicodes)
        {
            int low = ArtManifest.TmpMinSamplingPointSize;
            int high = 280;
            int best = -1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                TMP_FontAsset probe = BuildPackedFontAsset(sourceFont, asciiUnicodes, mid);
                bool fits = probe != null;
                DestroyTemporaryFontAsset(probe);

                if (fits)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return best;
        }

        private static TMP_FontAsset BuildPackedFontAsset(Font sourceFont, uint[] asciiUnicodes, int pointSize)
        {
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                pointSize,
                ArtManifest.TmpPadding,
                GlyphRenderMode.SDFAA,
                ArtManifest.TmpAtlasSize,
                ArtManifest.TmpAtlasSize,
                AtlasPopulationMode.Dynamic,
                false);

            if (fontAsset == null)
            {
                return null;
            }

            bool added = fontAsset.TryAddCharacters(asciiUnicodes, out uint[] missing, true);
            bool singlePage = fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length == 1;
            if (!added || (missing != null && missing.Length > 0) || !singlePage)
            {
                DestroyTemporaryFontAsset(fontAsset);
                return null;
            }

            return fontAsset;
        }

        private static void SaveFontAssetToDisk(TMP_FontAsset fontAsset, Font sourceFont, int pointSize)
        {
            fontAsset.name = "OverlaySerif SDF";
            AssetDatabase.CreateAsset(fontAsset, ArtManifest.OverlaySerifAssetPath);

            Texture2D atlas = fontAsset.atlasTextures[0];
            atlas.name = "OverlaySerif SDF Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);

            Material mat = fontAsset.material;
            mat.name = "OverlaySerif SDF Material";
            AssetDatabase.AddObjectToAsset(mat, fontAsset);

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            string sourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sourceFont));
            fontAsset.creationSettings = new FontAssetCreationSettings
            {
                sourceFontFileName = Path.GetFileName(SourceFontPath),
                sourceFontFileGUID = sourceGuid,
                faceIndex = 0,
                pointSizeSamplingMode = 0,
                pointSize = pointSize,
                padding = ArtManifest.TmpPadding,
                paddingMode = 2,
                packingMode = 0,
                atlasWidth = ArtManifest.TmpAtlasSize,
                atlasHeight = ArtManifest.TmpAtlasSize,
                characterSetSelectionMode = 0,
                characterSequence = "32-126",
                renderMode = (int)GlyphRenderMode.SDFAA,
                includeFontFeatures = true,
            };
            fontAsset.ReadFontAssetDefinition();

            EditorUtility.SetDirty(fontAsset);
            EditorUtility.SetDirty(atlas);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        private static void DestroyTemporaryFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return;
            }

            if (fontAsset.atlasTextures != null)
            {
                for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
                {
                    if (fontAsset.atlasTextures[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(fontAsset.atlasTextures[i]);
                    }
                }
            }

            if (fontAsset.material != null)
            {
                UnityEngine.Object.DestroyImmediate(fontAsset.material);
            }

            UnityEngine.Object.DestroyImmediate(fontAsset);
        }

        public static void ConfigureTmpSettingsAndRemoveLiberationSans(TMP_FontAsset overlayFont)
        {
            string[] settingsGuids = AssetDatabase.FindAssets("t:TMP_Settings");
            if (settingsGuids.Length == 0)
            {
                throw new InvalidOperationException("TMP Settings.asset not found after importing TMP Essentials.");
            }

            string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuids[0]);
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);
            var so = new SerializedObject(settings);
            SerializedProperty defaultFontProp = so.FindProperty("m_defaultFontAsset");
            if (defaultFontProp != null)
            {
                defaultFontProp.objectReferenceValue = overlayFont;
            }

            SerializedProperty fallbacksProp = so.FindProperty("m_fallbackFontAssets");
            if (fallbacksProp != null && fallbacksProp.isArray)
            {
                fallbacksProp.ClearArray();
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            DeleteLiberationSansAssets();
        }

        private static void DeleteLiberationSansAssets()
        {
            if (!Directory.Exists("Assets/TextMesh Pro"))
            {
                return;
            }

            string[] files = Directory.GetFiles("Assets/TextMesh Pro", "*LiberationSans*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i].Replace('\\', '/');
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(file);
            }
        }

        public static void BuildSystemOverlayPrefab(TMP_FontAsset overlayFont)
        {
            EnsureDirectory(PrefabDirectory);

            var root = new GameObject("SystemOverlay");
            try
            {
                root.layer = DrawOrder.UiLayer;
                ConfigureRootCanvas(root);

                Image scrim = CreateScrim(root.transform);
                RectTransform panel = CreatePanel(root.transform);
                TMP_Text title = CreateTitle(panel, overlayFont);
                CreateDivider(panel);
                Button resumeButton = CreateActionButton(panel, "Resume", -184f, overlayFont);
                Button quitButton = CreateActionButton(panel, "Quit", -276f, overlayFont);
                EventSystem eventSystem = CreateEventSystem(root.transform);

                var parts = new OverlayParts
                {
                    Canvas = root.GetComponent<Canvas>(),
                    Scrim = scrim,
                    Panel = panel,
                    Title = title,
                    ResumeButton = resumeButton,
                    QuitButton = quitButton,
                    EventSystem = eventSystem,
                };
                SystemOverlay overlay = root.AddComponent<SystemOverlay>();
                WireOverlayFields(overlay, parts);

                PrefabUtility.SaveAsPrefabAsset(root, SystemOverlayPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureRootCanvas(GameObject root)
        {
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;

            root.AddComponent<GraphicRaycaster>();
        }

        private static Image CreateScrim(Transform parent)
        {
            var go = new GameObject("Scrim");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = go.AddComponent<Image>();
            image.color = ScrimColor;
            image.raycastTarget = true;
            return image;
        }

        private static RectTransform CreatePanel(Transform parent)
        {
            var go = new GameObject("Panel");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(520f, 420f);

            Image image = go.AddComponent<Image>();
            image.sprite = LoadRequiredSprite(OverlayPanelPath);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;
            return rect;
        }

        private static TMP_Text CreateTitle(Transform panel, TMP_FontAsset overlayFont)
        {
            var go = new GameObject("Title");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(panel, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            SetTopAnchoredRect(rect, -48f, new Vector2(420f, 72f));

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = overlayFont;
            text.text = "Paused";
            text.fontSize = 56f;
            text.color = TextIvory;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static void CreateDivider(Transform panel)
        {
            var go = new GameObject("Divider");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(panel, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            SetTopAnchoredRect(rect, -136f, new Vector2(320f, 8f));

            Image image = go.AddComponent<Image>();
            image.sprite = LoadRequiredSprite(OverlayDividerPath);
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static Button CreateActionButton(Transform panel, string label, float topOffsetY, TMP_FontAsset overlayFont)
        {
            var go = new GameObject(label);
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(panel, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            SetTopAnchoredRect(rect, topOffsetY, new Vector2(320f, 72f));

            Sprite normal = LoadRequiredSprite(OverlayButtonNormalPath);
            Sprite hover = LoadRequiredSprite(OverlayButtonHoverPath);
            Sprite pressed = LoadRequiredSprite(OverlayButtonPressedPath);

            Image image = go.AddComponent<Image>();
            image.sprite = normal;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = hover,
                pressedSprite = pressed,
                selectedSprite = normal,
                disabledSprite = normal,
            };
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            CreateButtonLabel(go.transform, label, overlayFont);
            return button;
        }

        private static void CreateButtonLabel(Transform buttonParent, string label, TMP_FontAsset overlayFont)
        {
            var go = new GameObject("Label");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(buttonParent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = overlayFont;
            text.text = label;
            text.fontSize = 32f;
            text.color = TextIvory;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
        }

        private static EventSystem CreateEventSystem(Transform parent)
        {
            var go = new GameObject("EventSystem");
            go.layer = DrawOrder.UiLayer;
            go.transform.SetParent(parent, false);
            EventSystem eventSystem = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            go.SetActive(false);
            return eventSystem;
        }

        private struct OverlayParts
        {
            public Canvas Canvas;
            public Image Scrim;
            public RectTransform Panel;
            public TMP_Text Title;
            public Button ResumeButton;
            public Button QuitButton;
            public EventSystem EventSystem;
        }

        private static void WireOverlayFields(SystemOverlay overlay, OverlayParts parts)
        {
            var so = new SerializedObject(overlay);
            so.FindProperty("overlayCanvas").objectReferenceValue = parts.Canvas;
            so.FindProperty("scrimImage").objectReferenceValue = parts.Scrim;
            so.FindProperty("panelRect").objectReferenceValue = parts.Panel;
            so.FindProperty("titleText").objectReferenceValue = parts.Title;
            so.FindProperty("resumeButton").objectReferenceValue = parts.ResumeButton;
            so.FindProperty("quitButton").objectReferenceValue = parts.QuitButton;
            so.FindProperty("ownedEventSystem").objectReferenceValue = parts.EventSystem;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTopAnchoredRect(RectTransform rect, float yFromTop, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, yFromTop);
            rect.sizeDelta = size;
        }

        private static Sprite LoadRequiredSprite(string assetPath)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                throw new InvalidOperationException("Missing UI sprite at " + assetPath);
            }

            return sprite;
        }

        private static void EnsureDirectory(string assetFolderPath)
        {
            if (!Directory.Exists(assetFolderPath))
            {
                Directory.CreateDirectory(assetFolderPath);
                AssetDatabase.Refresh();
            }
        }
    }
}
