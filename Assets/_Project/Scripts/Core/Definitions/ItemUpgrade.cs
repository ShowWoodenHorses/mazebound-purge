using System;
using Purge.Core.Content;
namespace Purge.Core.Definitions
{
    public enum WeaponUpgrade { None, Pierce, DoubleShot, FullMagazineCritical, Ricochet, Magazine, Ramp,
        ExplosiveRounds, Burn, Knockback, Wide, Silent, Duration, Range, Chain, Electrify, Radius, Cluster, Lifesteal, Shatter }
    [Serializable] public sealed class UpgradeFork
    {
        public WeaponUpgrade First, Second;
        public bool Available(int choice) => (choice == 0 ? First : Second) != WeaponUpgrade.Electrify;
    }
    [Serializable] public sealed class UpgradeParameter { public string Name; public float Value; }
    public interface IItemUpgrades
    {
        int Level(ContentDefinition item);
        bool Has(WeaponDefinition item, WeaponUpgrade upgrade);
        int Count(WeaponDefinition item, WeaponUpgrade upgrade);
        float DamageFactor(ContentDefinition item);
        float RadiusFactor(ThrowableDefinition item);
        int StackBonus(ThrowableDefinition item);
    }
}
