namespace Maze.Core.Validation
{
    public static class ValidationCodes
    {
        // Structural
        public const string GeometryCorrupted = "GeometryCorrupted";
        public const string NoPlayerStart = "NoPlayerStart";
        public const string NoExit = "NoExit";
        public const string EmptyId = "EmptyId";
        public const string DuplicateId = "DuplicateId";
        public const string OutOfBounds = "OutOfBounds";
        public const string InsideWall = "InsideWall";
        public const string DoorNotOnDoorCell = "DoorNotOnDoorCell";
        public const string DoorCellWithoutDoor = "DoorCellWithoutDoor";
        public const string Intersection = "Intersection";
        public const string ZombieOnPlayerStart = "ZombieOnPlayerStart";
        public const string MissingKeyReference = "MissingKeyReference";
        public const string UnusedKey = "UnusedKey";
        public const string KeySharedByDoors = "KeySharedByDoors";
        public const string MissingPatrolReference = "MissingPatrolReference";
        public const string InvalidPatrolPoint = "InvalidPatrolPoint";
        public const string PatrolTooShort = "PatrolTooShort";
        public const string UnusedPatrol = "UnusedPatrol";
        public const string MissingDefinition = "MissingDefinition";
        public const string InvalidFragmentRegion = "InvalidFragmentRegion";
        public const string FragmentOverlap = "FragmentOverlap";
        public const string FrozenDoorInitiallyOpen = "FrozenDoorInitiallyOpen";
        public const string SurfacesOutOfSync = "SurfacesOutOfSync";
        public const string SurfaceNotOnFloor = "SurfaceNotOnFloor";

        // Visual
        public const string NoVisualTheme = "NoVisualTheme";
        public const string LightNotOnFloor = "LightNotOnFloor";
        public const string InvalidLight = "InvalidLight";
        public const string MissingVisualSet = "MissingVisualSet";
        public const string VisualSetKindMismatch = "VisualSetKindMismatch";
        public const string DuplicateVariantId = "DuplicateVariantId";
        public const string BrokenPrefabReference = "BrokenPrefabReference";

        // Geometry prefabs (floor, wall): checked by the editor validator, which can inspect prefab assets.
        public const string GeometryMeshNotReadable = "GeometryMeshNotReadable";
        public const string GeometryShaderUnsupported = "GeometryShaderUnsupported";
        public const string GeometryUnsupportedRenderer = "GeometryUnsupportedRenderer";
        public const string InvalidDefaultVariant = "InvalidDefaultVariant";
        public const string AssignmentsOutOfSync = "AssignmentsOutOfSync";
        public const string MissingVisual = "MissingVisual";
        public const string UnknownVariant = "UnknownVariant";
        public const string VariantDefinitionMismatch = "VariantDefinitionMismatch";
        public const string WallCategoryWithoutVariants = "WallCategoryWithoutVariants";
        public const string StaleWallVisual = "StaleWallVisual";
        public const string OverrideOutOfBounds = "OverrideOutOfBounds";
        public const string OverrideLayerMismatch = "OverrideLayerMismatch";
        public const string InvalidDecorPlacement = "InvalidDecorPlacement";
        public const string StaleDecorPlacement = "StaleDecorPlacement";
        public const string InvalidObjectPlacement = "InvalidObjectPlacement";
        public const string OverrideForMissingEntity = "OverrideForMissingEntity";
        public const string UniformDistribution = "UniformDistribution";
        public const string KeyDoorColorMismatch = "KeyDoorColorMismatch";
        public const string RepeatedKeyColor = "RepeatedKeyColor";
        public const string UnlockedDoorWithColor = "UnlockedDoorWithColor";
        public const string FrozenDoorWithoutIceVisual = "FrozenDoorWithoutIceVisual";

        public const string TutorialEmpty = "TutorialEmpty";
        public const string TutorialStepInvalid = "TutorialStepInvalid";

        // Gameplay
        public const string NoReachableExit = "NoReachableExit";
        public const string ExitUnreachable = "ExitUnreachable";
        public const string KeyUnreachable = "KeyUnreachable";
        public const string ObjectUnreachable = "ObjectUnreachable";
        public const string DoorWithoutPassage = "DoorWithoutPassage";
        public const string PatrolPointUnreachable = "PatrolPointUnreachable";
        public const string ShortestPath = "ShortestPath";
    }
}
