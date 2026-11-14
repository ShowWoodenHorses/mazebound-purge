using System;
using Purge.Core.Modules;
using UnityEngine;

namespace Purge.Core.Boosters
{
    public enum SpecialBoosterKind { SecondBarrel, Headhunter, Minefield, QuietStart, DropLoad, SecondDome }

    [Serializable, ModuleInfo("Character booster")]
    public sealed class SpecialBoost : BoosterModule
    {
        [SerializeField] private SpecialBoosterKind _kind;

        public SpecialBoost(SpecialBoosterKind kind) => _kind = kind;
        public SpecialBoosterKind Kind => _kind;
    }
}
