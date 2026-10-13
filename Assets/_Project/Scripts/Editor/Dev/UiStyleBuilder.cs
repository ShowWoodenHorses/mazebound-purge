using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Maze.Core.Localization;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build UI Style: the shared <c>Maze/UIShape</c> material, the text fonts and the <see cref="UiStyle"/>
    /// asset (palette, line widths). Fonts are static TextMeshPro SDF atlases (mobile SDF shader) with only the
    /// characters the game shows: printable ASCII plus every character of the translation table
    /// (<see cref="LocalizationBuilder.SourcePath"/>). Rajdhani SemiBold / Bold take what they have (Latin of all
    /// languages); the rest (Cyrillic) goes to their fallbacks Exo 2 SemiBold / Bold, scaled to Rajdhani's letters.
    /// An existing style keeps its tuned values; existing font assets are refilled in place (references survive). Also
    /// makes Rajdhani the TextMeshPro default font and removes the TMP sample font and emoji from <c>Resources</c>
    /// (they would ship in every build). Build Localization runs it after building the tables.
    /// </summary>
    internal static class UiStyleBuilder
    {
        public const string StylePath = "Assets/_Project/Data/UI/UiStyle.asset";
        public const string MaterialPath = "Assets/_Project/Art/UI/UIShape.mat";
        private const string ShaderName = "Maze/UIShape";
        private const string FontFolder = "Assets/_Project/Art/Fonts/Rajdhani";
        private const string FallbackFolder = "Assets/_Project/Art/Fonts/Exo2";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>Exo 2 capitals are ~7% taller than Rajdhani's, its lower case ~3% shorter: a middle size.</summary>
        private const float FallbackScale = 0.96f;

        /// <summary>Atlas sizes tried in order until every character fits.</summary>
        private static readonly Vector2Int[] AtlasSizes =
            { new Vector2Int(512, 512), new Vector2Int(1024, 512), new Vector2Int(1024, 1024), new Vector2Int(2048, 1024) };

        private static readonly string[] TmpExtras =
        {
            "Assets/TextMesh Pro/Resources/Fonts & Materials",
            "Assets/TextMesh Pro/Resources/Sprite Assets",
            "Assets/TextMesh Pro/Fonts",
            "Assets/TextMesh Pro/Sprites",
        };

        [MenuItem("Maze/Dev/Build UI Style")]
        public static void BuildMenu()
        {
            var style = Build();
            EditorGUIUtility.PingObject(style);
        }

        public static UiStyle Build()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new FileNotFoundException($"Shader '{ShaderName}' not found.");

            EnsureFolder(Path.GetDirectoryName(MaterialPath));
            EnsureFolder(Path.GetDirectoryName(StylePath));

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "UIShape" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            var style = AssetDatabase.LoadAssetAtPath<UiStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<UiStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
            }

            style.ShapeMaterial = material;
            var characters = Characters();
            style.Font = BuildFamily(FontFolder, "Rajdhani-SemiBold", FallbackFolder, "Exo2-SemiBold", characters);
            style.BoldFont = BuildFamily(FontFolder, "Rajdhani-Bold", FallbackFolder, "Exo2-Bold", characters);
            EditorUtility.SetDirty(style);
            ConfigureTextMeshPro(style.Font);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] UI style built: {StylePath}.");
            return style;
        }

        /// <summary>Printable ASCII and every character of every language in the translation table.</summary>
        private static string Characters()
        {
            var set = new SortedSet<char>();
            for (var c = 32; c < 127; c++) set.Add((char)c);
            if (File.Exists(LocalizationBuilder.SourcePath))
            {
                var source = LocalizationSource.Parse(File.ReadAllText(LocalizationBuilder.SourcePath, Encoding.UTF8));
                foreach (var language in source.Languages)
                    foreach (var c in source.CharactersOf(language))
                        set.Add(c);
            }

            return new string(set.ToArray());
        }

        /// <summary>
        /// The main font with the characters it has and its fallback with the rest (no fallback when nothing is left
        /// for it).
        /// </summary>
        private static TMP_FontAsset BuildFamily(string folder, string name, string fallbackFolder, string fallbackName,
            string characters)
        {
            var source = LoadFont(folder, name);
            var own = new StringBuilder();
            var rest = new StringBuilder();
            foreach (var c in characters)
                (c == ' ' || HasGlyph(source, c) ? own : rest).Append(c);

            TMP_FontAsset fallback = null;
            if (rest.Length > 0)
            {
                var fallbackSource = LoadFont(fallbackFolder, fallbackName);
                var missing = rest.ToString().Where(c => !HasGlyph(fallbackSource, c)).ToArray();
                if (missing.Length > 0)
                    Debug.LogWarning($"[Maze] {name} and {fallbackName} have no glyphs for '{new string(missing)}'.");
                fallback = BuildFont(fallbackFolder, fallbackName, rest.ToString(), FallbackScale);
            }

            var main = BuildFont(folder, name, own.ToString(), 1f);
            main.fallbackFontAssetTable = fallback != null ? new List<TMP_FontAsset> { fallback } : new List<TMP_FontAsset>();
            EditorUtility.SetDirty(main);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Maze] Font {name}: {own.Length} characters" +
                      (fallback != null ? $"; {fallbackName}: {rest.Length} ({rest})." : "."));
            return main;
        }

        private static Font LoadFont(string folder, string name)
        {
            var sourcePath = $"{folder}/{name}.ttf";
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null) throw new FileNotFoundException($"Font '{sourcePath}' not found.");
            return source;
        }

        private static bool HasGlyph(Font font, char c)
        {
            if (FontEngine.LoadFontFace(font) != FontEngineError.Success) return false;
            return FontEngine.TryGetGlyphIndex(c, out var index) && index != 0;
        }

        /// <summary>
        /// Static SDF atlas (sampling 56, padding 6) with <paramref name="characters"/>, in the smallest of
        /// <see cref="AtlasSizes"/> they fit. <paramref name="scale"/> sizes the glyphs (a fallback matches the main font).
        /// </summary>
        private static TMP_FontAsset BuildFont(string folder, string name, string characters, float scale)
        {
            var source = LoadFont(folder, name);

            TMP_FontAsset fresh = null;
            foreach (var size in AtlasSizes)
            {
                if (fresh != null) Object.DestroyImmediate(fresh);
                fresh = TMP_FontAsset.CreateFontAsset(source, 56, 6, GlyphRenderMode.SDFAA, size.x, size.y,
                    AtlasPopulationMode.Dynamic, false);
                if (fresh.TryAddCharacters(characters, out var missing) || string.IsNullOrEmpty(missing)) break;
                if (size == AtlasSizes[AtlasSizes.Length - 1])
                    Debug.LogWarning($"[Maze] {name}: missing characters '{missing}'.");
            }

            var faceInfo = fresh.faceInfo;
            faceInfo.scale = scale;
            fresh.faceInfo = faceInfo;

            var path = $"{folder}/{name} SDF.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                asset = fresh;
                asset.name = name + " SDF";
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                // Refill in place: the GUID (and every reference to it) stays.
                EditorUtility.CopySerialized(fresh, asset);
                asset.name = name + " SDF";
                foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    if (old is Texture2D || old is Material) Object.DestroyImmediate(old, true);
            }

            var atlas = fresh.atlasTexture;
            atlas.name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);

            var material = new Material(Shader.Find("TextMeshPro/Mobile/Distance Field")) { name = name + " Material" };
            material.CopyPropertiesFromMaterial(fresh.material);
            material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            material.SetTexture(ShaderUtilities.ID_MainTex, atlas);
            AssetDatabase.AddObjectToAsset(material, asset);

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_AtlasPopulationMode").enumValueIndex = (int)AtlasPopulationMode.Static;
            var atlases = serialized.FindProperty("m_AtlasTextures");
            atlases.arraySize = 1;
            atlases.GetArrayElementAtIndex(0).objectReferenceValue = atlas;
            serialized.FindProperty("m_Material").objectReferenceValue = material;
            // Static atlas: the TTF is not needed at runtime and should not ship.
            var sourceFont = serialized.FindProperty("m_SourceFontFile");
            if (sourceFont != null) sourceFont.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (fresh != asset)
            {
                // Its atlas now belongs to the asset: the temporary one must not destroy it on its way out.
                var temporary = new SerializedObject(fresh);
                temporary.FindProperty("m_AtlasTextures").arraySize = 0;
                temporary.ApplyModifiedPropertiesWithoutUndo();
                Object.DestroyImmediate(fresh);
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        private static void ConfigureTextMeshPro(TMP_FontAsset font)
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[Maze] TMP Settings not found: import TMP Essential Resources.");
                return;
            }

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
            serialized.FindProperty("m_fallbackFontAssets").arraySize = 0;
            serialized.FindProperty("m_defaultSpriteAsset").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);

            foreach (var folder in TmpExtras)
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }
    }
}
