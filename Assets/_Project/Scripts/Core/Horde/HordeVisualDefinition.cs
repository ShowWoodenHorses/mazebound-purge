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
        /// <summary>A hit staggers the monster (it stands while it plays).</summary>
        HitReact = 5,
        /// <summary>Stunned (a state): reels on the spot, looped.</summary>
        Stun = 6,
        /// <summary>Climbs out of the ground (a new monster's first seconds).</summary>
        Emerge = 7,
    }

    /// <summary>Which way a death clip falls: the side the killing push came from (Any = whatever the hit).</summary>
    public enum HordeClipSide
    {
        Any = 0,
        /// <summary>Hit in the front: falls backwards.</summary>
        Front = 1,
        Back = 2,
        Left = 3,
        Right = 4,
    }

    /// <summary>A model glued into the baked mesh, skinned to one bone (ТЗ: оружие рядовых вклеивается в меш).</summary>
    [Serializable]
    public sealed class HordeEquipment
    {
        [Tooltip("The bone that carries it (weight 1).")]
        public HumanBodyBones Bone = HumanBodyBones.RightHand;
        [Tooltip("The model (its offset in the hand — the model's own EquipmentModel component, shared by every monster).")]
        public GameObject Model;
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
        public HordeClipSide Side;
        /// <summary>Frames per second of clip time in the texture (0 = the look's <see cref="HordeVisualDefinition.FrameRate"/>).</summary>
        public float FrameRate;
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
        [Tooltip("Whose look it is (empty = the stress test's built-in monster).")]
        [SerializeField, Content.ContentRef(typeof(Monsters.MonsterDefinition))] private string _monsterId;
        [Tooltip("Models glued into the mesh (Build Monsters reads it; rebuild after a change).")]
        [SerializeField] private HordeEquipment[] _equipment = Array.Empty<HordeEquipment>();
        [SerializeField] private Mesh _mesh;
        [Tooltip("One material per look variant (shader Purge/Horde, same mesh, different albedo) — the body, submesh 0.")]
        [SerializeField] private Material[] _variants = Array.Empty<Material>();
        [Tooltip("Materials of submeshes 1.. (the equipment), shared by every variant.")]
        [SerializeField] private Material[] _extraMaterials = Array.Empty<Material>();
        [Tooltip("Ground speed of the walk/run clips at normal playback, cells per second (0 = unknown: play at 1×).")]
        [SerializeField, Min(0f)] private float _locomotionSpeed;
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
        public string MonsterId => _monsterId;
        public HordeEquipment[] Equipment => _equipment ??= Array.Empty<HordeEquipment>();
        public Material[] ExtraMaterials => _extraMaterials ??= Array.Empty<Material>();
        public float LocomotionSpeed => _locomotionSpeed;

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

        /// <summary>Clips of <paramref name="kind"/> (indices into <see cref="Clips"/>), in order.</summary>
        public int[] ClipsOf(HordeClipKind kind)
        {
            var result = new int[CountClips(kind)];
            var n = 0;
            for (var i = 0; i < _clips.Length; i++)
                if (_clips[i].Kind == kind)
                    result[n++] = i;
            return result;
        }

        internal void ConfigureMonster(string monsterId, Material[] extraMaterials, float locomotionSpeed, Color splashColor)
        {
            _monsterId = monsterId;
            _extraMaterials = extraMaterials ?? Array.Empty<Material>();
            _locomotionSpeed = locomotionSpeed;
            _splashColor = splashColor;
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
