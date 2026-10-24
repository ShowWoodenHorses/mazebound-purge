using System;
using System.Collections.Generic;
using Purge.Core.Common;
using Purge.Core.Content;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Generation
{
    /// <summary>
    /// How a biome's level is generated (ТЗ, stage 7): a list of steps run in order over one <see cref="LevelLayout"/>
    /// (perimeter → start → arena → spawn points → clearings → trails → obstacles → grass → decor for the Forest).
    /// A new biome is a new recipe asset; a new kind of step is a new <see cref="GenerationStep"/> class.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Purge/Biome Recipe")]
    public sealed class BiomeRecipe : ContentDefinition
    {
        [SerializeReference, ModuleList] private List<GenerationStep> _steps = new List<GenerationStep>();

        public IReadOnlyList<GenerationStep> Steps => _steps;

        internal List<GenerationStep> MutableSteps => _steps;

        public void Validate(List<string> problems) => Purge.Core.Modules.Modules.Validate(_steps, name, problems);
    }

    /// <summary>
    /// One step of a <see cref="BiomeRecipe"/>. Gets its own random stream (seed + step number), so changing one step's
    /// numbers does not reshuffle the others. Must leave kept cells alone (the <see cref="LevelLayout"/> setters do).
    /// </summary>
    [Serializable]
    public abstract class GenerationStep : ContentModule
    {
        public abstract void Apply(LevelLayout layout, DeterministicRandom random);
    }
}
