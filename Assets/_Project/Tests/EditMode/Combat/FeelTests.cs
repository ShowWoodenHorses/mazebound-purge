using NUnit.Framework;
using Purge.Core.Audio;
using Purge.Core.Definitions;
using Purge.Core.Feel;
using UnityEngine;

namespace Purge.Tests.EditMode.Combat
{
    /// <summary>Shooting feel data (stage 4в): channel multipliers of the stand, the weapon's feel, sound parts.</summary>
    public class FeelTests
    {
        [Test]
        public void Channels_OffIsZero_ScaleIsClamped_ResetRestores()
        {
            var channels = new FeelChannels();
            Assert.AreEqual(1f, channels[FeelChannel.Knockback]);

            channels.SetScale(FeelChannel.Knockback, 2.5f);
            Assert.AreEqual(2.5f, channels[FeelChannel.Knockback]);
            channels.SetScale(FeelChannel.Knockback, 10f);
            Assert.AreEqual(FeelChannels.MaxScale, channels[FeelChannel.Knockback]);

            channels.SetOn(FeelChannel.Knockback, false);
            Assert.AreEqual(0f, channels[FeelChannel.Knockback], "off");
            Assert.AreEqual(FeelChannels.MaxScale, channels.Scale(FeelChannel.Knockback), "the scale is kept for when it is on again");

            channels.Reset();
            Assert.IsTrue(channels.IsOn(FeelChannel.Knockback));
            Assert.AreEqual(1f, channels[FeelChannel.Knockback]);
        }

        [Test]
        public void Weapon_WithoutFeelModule_FeelsAsDefault_WithIt_AsTheModule()
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                weapon.Configure("gun", new BulletAttack(), new AmmoModule());
                Assert.AreSame(FeelModule.Default, weapon.Feel);

                var feel = new FeelModule { Knockback = 5f };
                weapon.Configure("gun", new BulletAttack(), new AmmoModule(), feel);
                Assert.AreSame(feel, weapon.Feel);
                Assert.AreEqual(5f, weapon.Feel.Knockback);
            }
            finally
            {
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void Catalog_FindsPartsByName_AndBlastsBySize()
        {
            var catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            var clip = AudioClip.Create("test", 100, 1, 44100, false);
            try
            {
                var tail = new SoundPart("tail", new SoundCue());
                tail.Cue.Clips = new[] { clip };
                catalog.MutableParts.Add(tail);
                catalog.MutableParts.Add(new SoundPart("empty", new SoundCue()));

                Assert.AreSame(tail.Cue, catalog.Part("TAIL"), "names ignore case");
                Assert.IsNull(catalog.Part("empty"), "a part without clips is silent");
                Assert.IsNull(catalog.Part("missing"));
                Assert.IsNull(catalog.Part(null));

                catalog.ExplosionSmallCue.Clips = new[] { clip };
                catalog.ExplosionCue.Clips = new[] { clip };
                Assert.AreSame(catalog.ExplosionSmallCue, catalog.ExplosionOf(1f));
                Assert.AreSame(catalog.ExplosionCue, catalog.ExplosionOf(2f));
                Assert.AreSame(catalog.ExplosionCue, catalog.ExplosionOf(5f), "no large one: the medium");
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(catalog);
            }
        }
    }
}
