using System;
using UnityEngine;

namespace Purge.Core.Horde
{
    /// <summary>What a baked clip is used for (the renderer picks clips by kind).</summary>
    public enum HordeClipKind
    {
        Idle = 0,
        Walk = 1,
        Run = 2,
        Attack = 3,
        Death = 4,
    }

    /// <summary>One clip in the baked animation textures: rows FirstFrame .. FirstFrame + FrameCount - 1.</summary>
    [Serializable]
    public struct HordeClip
    {
        public string Name;
        public HordeClipKind Kind;
        public int FirstFrame;
        public int FrameCount;
        public float Length;
        public bool Loop;
    }

    /// <summary>
    /// Look of mass monsters (rank-and-file, ТЗ: baked animation + GPU instancing; no GameObject, no Animator).
    /// Built by Purge → Dev → Build Horde Visuals from a Synty character and Mixamo clips:
    /// <list type="bullet">
    /// <item><see cref="Mesh"/> — the character welded by position + UV (flat-shaded Synty: ~3× fewer vertices;
    /// normals come from screen derivatives in the shader). UV1.x — the vertex column in <see cref="VertexAnimation"/>;
    /// UV2 — two strongest bones (indices xy, weights zw).</item>
    /// <item><see cref="VertexAnimation"/> (VAT) — model-space positions, a column per vertex, a row per frame.</item>
    /// <item><see cref="BoneAnimation"/> — bone matrices (3 texels = 3 rows of a 3×4 matrix per bone), a row per frame;
    /// shared by every character on the same skeleton.</item>
    /// </list>
    /// Which of the two the shader uses is the global keyword PURGE_HORDE_VAT (stress test compares them).
    /// </summary>
    [CreateAssetMenu(menuName = "Purge/Horde Visual", fileName = "HordeVisual")]
    public sealed class HordeVisualDefinition : ScriptableObject
    {
        [SerializeField] private Mesh _mesh;
        [Tooltip("One material per look variant (shader Purge/Horde, same mesh, different albedo).")]
        [SerializeField] private Material[] _variants = Array.Empty<Material>();
        [SerializeField] private Texture2D _vertexAnimation;
        [SerializeField] private Texture2D _boneAnimation;
        [SerializeField, Min(1)] private int _boneCount = 1;
        [SerializeField, Min(1f)] private float _frameRate = 30f;
        [SerializeField] private HordeClip[] _clips = Array.Empty<HordeClip>();
        [Tooltip("Model scale in the level (the models are ~1.8 m tall, cells 1 m).")]
        [SerializeField, Min(0.01f)] private float _scale = 0.75f;
        [Tooltip("Vertex count of the source skinned mesh (before welding), for the report.")]
        [SerializeField] private int _sourceVertexCount;
        [Tooltip("Colour of the splashes when a monster is hit or killed (cartoon style: no blood), sRGB.")]
        [SerializeField] private Color _splashColor = new Color(0.55f, 0.85f, 0.25f, 1f);

        public Color SplashColor => _splashColor;

        public Mesh Mesh => _mesh;
        public Material[] Variants => _variants;
        public Texture2D VertexAnimation => _vertexAnimation;
        public Texture2D BoneAnimation => _boneAnimation;
        public int BoneCount => _boneCount;
        public float FrameRate => _frameRate;
        public HordeClip[] Clips => _clips;
        public float Scale => _scale;
        public int SourceVertexCount => _sourceVertexCount;

        /// <summary>Index of the first clip of <paramref name="kind"/>, -1 when there is none.</summary>
        public int FindClip(HordeClipKind kind)
        {
            for (var i = 0; i < _clips.Length; i++)
                if (_clips[i].Kind == kind)
                    return i;
            return -1;
        }

        /// <summary>Number of clips of <paramref name="kind"/>.</summary>
        public int CountClips(HordeClipKind kind)
        {
            var count = 0;
            for (var i = 0; i < _clips.Length; i++)
                if (_clips[i].Kind == kind)
                    count++;
            return count;
        }

        internal void Configure(Mesh mesh, Material[] variants, Texture2D vertexAnimation, Texture2D boneAnimation,
            int boneCount, float frameRate, HordeClip[] clips, float scale, int sourceVertexCount)
        {
            _mesh = mesh;
            _variants = variants;
            _vertexAnimation = vertexAnimation;
            _boneAnimation = boneAnimation;
            _boneCount = boneCount;
            _frameRate = frameRate;
            _clips = clips;
            _scale = scale;
            _sourceVertexCount = sourceVertexCount;
        }
    }
}
