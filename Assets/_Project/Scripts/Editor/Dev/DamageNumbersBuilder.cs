using System.Collections.Generic;
using System.IO;
using Maze.Core.Visual;
using Maze.Presentation.UI.Style;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Damage Numbers: the digit atlas and material for health bars and damage numbers
    /// (<see cref="CombatFeedback"/>, <c>CombatFeedbackView</c>). Cuts "0-9", "+" and "-" with their distance-field
    /// padding out of the UI's bold font (<see cref="UiStyle.BoldFont"/>, Rajdhani Bold) into a small single-channel
    /// texture, so the level does not pull in the whole UI atlas; writes the glyph layout and the <c>Maze/Overhead</c>
    /// material into Data/Combat/CombatVisual. Colours and sizes in the asset are kept. Run after changing the font.
    /// </summary>
    public static class DamageNumbersBuilder
    {
        private const string Folder = "Assets/_Project/Art/Effects";
        private const string TexturePath = Folder + "/fx_digits.png";
        private const string MaterialPath = Folder + "/fx_overhead.mat";
        private const string CombatVisualPath = "Assets/_Project/Data/Combat/CombatVisual.asset";
        private const string Characters = "0123456789+-";
        private const int Gap = 2;

        [MenuItem("Maze/Dev/Build Damage Numbers")]
        public static void Build()
        {
            var shader = Shader.Find("Maze/Overhead");
            var style = AssetDatabase.LoadAssetAtPath<UiStyle>(UiStyleBuilder.StylePath);
            var font = style != null ? style.BoldFont : null;
            var visual = AssetDatabase.LoadAssetAtPath<CombatVisualDefinition>(CombatVisualPath);
            if (shader == null || font == null || font.atlasTexture == null || visual == null)
            {
                Debug.LogError("[Maze] Build Damage Numbers needs the 'Maze/Overhead' shader, UiStyle with a Bold Font " +
                               $"(Build UI Style) and {CombatVisualPath} (Build Combat Effects).");
                return;
            }

            var padding = font.atlasPadding;
            var source = ReadAtlas(font.atlasTexture);
            var cut = new List<(char Character, Glyph Glyph)>();
            var width = Gap;
            var height = 0;
            foreach (var character in Characters)
            {
                if (!font.characterLookupTable.TryGetValue(character, out var found) || found.glyph == null)
                {
                    Debug.LogWarning($"[Maze] Font '{font.name}' has no '{character}'.");
                    continue;
                }

                var rect = found.glyph.glyphRect;
                var glyph = new Glyph
                {
                    Source = new RectInt(rect.x - padding, rect.y - padding, rect.width + padding * 2, rect.height + padding * 2),
                    Metrics = found.glyph.metrics,
                    X = width,
                };
                cut.Add((character, glyph));
                width += glyph.Source.width + Gap;
                height = Mathf.Max(height, glyph.Source.height + Gap * 2);
            }

            var digitHeight = 0f;
            foreach (var (character, glyph) in cut)
                if (character == '0') digitHeight = glyph.Metrics.height;
            if (cut.Count == 0 || digitHeight <= 0f)
            {
                Object.DestroyImmediate(source);
                Debug.LogError($"[Maze] Font '{font.name}' has no digits.");
                return;
            }

            // Copy each glyph's field into its own cell of the new texture (R = distance field).
            var pixels = new Color32[width * height];
            foreach (var (_, glyph) in cut)
            for (var y = 0; y < glyph.Source.height; y++)
            for (var x = 0; x < glyph.Source.width; x++)
            {
                var sx = glyph.Source.x + x;
                var sy = glyph.Source.y + y;
                if (sx < 0 || sy < 0 || sx >= source.width || sy >= source.height) continue;
                var a = source.GetPixel(sx, sy).a;
                var value = (byte)Mathf.RoundToInt(a * 255f);
                pixels[(Gap + y) * width + glyph.X + x] = new Color32(value, value, value, 255);
            }
            Object.DestroyImmediate(source);

            var texture = SaveTexture(pixels, width, height);
            var scale = 1f / digitHeight;
            var glyphs = new List<NumberGlyph>();
            foreach (var (character, glyph) in cut)
            {
                var m = glyph.Metrics;
                glyphs.Add(new NumberGlyph
                {
                    Character = character,
                    Uv = new Rect((float)glyph.X / width, (float)Gap / height,
                        (float)glyph.Source.width / width, (float)glyph.Source.height / height),
                    Quad = new Vector4(
                        (m.horizontalBearingX - padding) * scale,
                        (m.horizontalBearingY - glyph.Source.height + padding) * scale,
                        (m.horizontalBearingX - padding + glyph.Source.width) * scale,
                        (m.horizontalBearingY + padding) * scale),
                    Advance = m.horizontalAdvance * scale,
                });
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "fx_overhead" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            EditorUtility.SetDirty(material);

            visual.Feedback.Material = material;
            visual.Feedback.Glyphs = glyphs.ToArray();
            EditorUtility.SetDirty(visual);

            var problem = LevelDesigner.LevelSync.SyncShared(AddressableAssetSettingsDefaultObject.GetSettings(true));
            AssetDatabase.SaveAssets();
            if (problem != null) Debug.LogWarning("[Maze] " + problem);
            Debug.Log($"[Maze] Damage numbers: {glyphs.Count} glyphs from '{font.name}' → {TexturePath}, {width}×{height}.");
        }

        /// <summary>The font atlas read back through a render texture (static atlases are not readable).</summary>
        private static Texture2D ReadAtlas(Texture2D atlas)
        {
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Texture2D SaveTexture(Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.SingleChannel;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(width, height));
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        private struct Glyph
        {
            public RectInt Source;
            public UnityEngine.TextCore.GlyphMetrics Metrics;
            public int X;
        }
    }
}
