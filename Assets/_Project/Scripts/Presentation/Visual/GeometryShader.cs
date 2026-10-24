using UnityEngine;

namespace Purge.Presentation.Visual
{
    /// <summary>Contract between the combined geometry meshes and the "Purge/Geometry" shader.</summary>
    public static class GeometryShader
    {
        /// <summary>Floor and wall materials must use this shader, otherwise single cells cannot be hidden.</summary>
        public const string Name = "Purge/Geometry";

        /// <summary>Mesh UV channel holding the grid cell (x, y) of every vertex.</summary>
        public const int CellUvChannel = 3;

        public const string VisibilityKeyword = "PURGE_VISIBILITY";

        public static readonly int VisibilityTextureId = Shader.PropertyToID("_PurgeVisibility");
        public static readonly int VisibilitySizeId = Shader.PropertyToID("_PurgeVisibilitySize");

        /// <summary>Soft snowdrifts on the floor (<see cref="SnowdriftMask"/>); off = no drift blending at all.</summary>
        public const string SnowdriftKeyword = "PURGE_SNOWDRIFT";

        public static readonly int SnowdriftTextureId = Shader.PropertyToID("_PurgeDrift");
        public static readonly int SnowdriftSizeId = Shader.PropertyToID("_PurgeDriftSize");
        public static readonly int SnowdriftColorId = Shader.PropertyToID("_PurgeDriftColor");
        public static readonly int SnowdriftParamsId = Shader.PropertyToID("_PurgeDriftParams");

        /// <summary>
        /// Bare ground on the floor (<see cref="GroundMask"/>); one multi_compile set with <see cref="SnowdriftKeyword"/>:
        /// a level shows either drifts or ground.
        /// </summary>
        public const string GroundKeyword = "PURGE_GROUND";

        public static readonly int GroundMaskId = Shader.PropertyToID("_PurgeGround");
        public static readonly int GroundTextureId = Shader.PropertyToID("_PurgeGroundTex");
        public static readonly int GroundSizeId = Shader.PropertyToID("_PurgeGroundSize");
        public static readonly int GroundParamsId = Shader.PropertyToID("_PurgeGroundParams");

        public static bool Supports(Material material) =>
            material != null && material.shader != null && material.shader.name == Name;
    }
}
