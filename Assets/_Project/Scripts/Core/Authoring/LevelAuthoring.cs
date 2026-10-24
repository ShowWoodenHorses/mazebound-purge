using System.Linq;
using Purge.Core.Generation;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;

namespace Purge.Core.Authoring
{
    /// <summary>High-level level creation commands used by the editor tool (ТЗ §16, §29, §95).</summary>
    internal static class LevelAuthoring
    {
        /// <summary>
        /// Destructive: new logical layout from the generation settings (the biome recipe; none = an open field), all
        /// objects, visual design (including overrides), hand-edit marks and locked areas are replaced, then initial
        /// visuals are assigned from VisualSeed.
        /// </summary>
        public static GenerationResult GenerateNew(LevelData level)
        {
            var recipe = level.Generation.Recipe;
            var result = recipe != null
                ? new BiomeGenerator().Generate(level.Generation, recipe)
                : new OpenFieldGenerator().Generate(level.Generation);
            level.ApplyGenerated(result);
            ApplyDecor(level, result);
            VisualAssigner.AssignAll(level, clearOverrides: true);
            return result;
        }

        /// <summary>
        /// "Generate" with a biome recipe: everything is generated again except the cells edited by hand and the locked
        /// areas (<see cref="LevelData.Marks"/>) — they keep their cells, objects, light sources, spawn points and visual
        /// overrides; a start, spawn points and an arena on them are kept and the rest is built around them. Without a
        /// recipe — <see cref="GenerateNew"/>. A new size drops the hand-edit marks (locked areas stay).
        /// </summary>
        public static GenerationResult Generate(LevelData level)
        {
            var recipe = level.Generation.Recipe;
            if (recipe == null)
                return GenerateNew(level);

            var result = new BiomeGenerator().Generate(level.Generation, recipe, level);
            var geometry = level.Geometry;
            var sameSize = geometry.IsConsistent && geometry.Width == level.Generation.Width &&
                           geometry.Height == level.Generation.Height;
            var marks = level.Marks;
            bool Kept(GridPosition p) => sameSize && (p.Floor > 0 || marks.IsKept(geometry, p));

            foreach (var entity in level.AllEntities().Where(e => !Kept(e.Position)).ToList())
                if (level.FindEntity(entity.Id) != null) // a key may go with its door
                    LevelEditing.Remove(level, entity);
            level.MutableLights.RemoveAll(light => !Kept(light.Cell));
            level.MutableMonsterSpawns.RemoveAll(spawn => !Kept(spawn.Position));
            level.VisualData.ClearCells(p => !Kept(p));
            if (!sameSize)
                marks.ClearManual();

            level.ReplaceGeometry(result.Geometry);
            level.SetArena(result.Arena);
            foreach (var position in result.PlayerStarts)
                level.MutablePlayerStarts.Add(new PlayerStartData(level.CreateUniqueId(PlayerStartData.IdPrefix), position));
            foreach (var position in result.MonsterSpawns)
                level.MutableMonsterSpawns.Add(new MonsterSpawnData(level.CreateUniqueId(MonsterSpawnData.IdPrefix), position));
            level.AddGeneratedObjects(result);

            ApplyDecor(level, result);
            VisualAssigner.AssignAll(level, clearOverrides: false);
            return result;
        }

        /// <summary>Recreates visual assignments only. Manual overrides survive unless <paramref name="clearOverrides"/>.</summary>
        public static void RegenerateVisuals(LevelData level, bool clearOverrides) =>
            VisualAssigner.AssignAll(level, clearOverrides);

        private static void ApplyDecor(LevelData level, GenerationResult result)
        {
            if (result.Decor == null) return;
            level.Generation.DecorDensity = result.Decor.Density;
            level.Generation.MaxAutoDecorHeight = result.Decor.MaxHeight;
            level.Generation.GrassDecorDensity = result.Decor.GrassDensity;
        }
    }
}
