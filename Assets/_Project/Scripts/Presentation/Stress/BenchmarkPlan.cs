using System.Collections.Generic;
using Purge.Core.Horde;
using Purge.Gameplay.Horde;

namespace Purge.Presentation.Stress
{
    /// <summary>One benchmark configuration: the load and the rendering options it measures.</summary>
    public struct BenchmarkConfig
    {
        public string Name;
        public int Count;
        public float Camera;
        public StressWeapon Weapon;
        public float Storm;
        public int Fire;
        public bool Vat;
        public bool Lerp;
        public bool Shadows;
        public bool Render;
        public bool Freeze;
        public bool Effects;
        public float Resolution;
        /// <summary>Skip monsters outside the player's view (as in play); off = every monster on screen is drawn.</summary>
        public bool Cull;

        /// <summary>Sandbox (stage 5): officers and Brutes on top of the horde (two of each).</summary>
        public bool Officers;
        /// <summary>Sandbox: the elite on top (one of each).</summary>
        public bool Elite;
        /// <summary>Sandbox: champions called up on top (affixes in turn, every third an officer).</summary>
        public int Champions;
        /// <summary>Sandbox: the player fires the bench weapon with aim assist (bullets, hits, kills, drops, deaths' effects).</summary>
        public bool Shoot;
    }

    /// <summary>Measured numbers of one configuration.</summary>
    public struct BenchmarkResult
    {
        public const string Header =
            "name | count cam weapon storm fire vat lerp shadows render freeze fx res cull | avgFPS 1%low medMs | " +
            "alive drawn | simMs simMaxMs flowMs fovMaxMs drawMs weapMs fxMs | drawCalls trisK | officers elite champs shoot";

        public BenchmarkConfig Config;
        public float AvgFps, Low1Fps, MedianMs;
        public float SimMs, SimMaxMs, FlowMs, FovMaxMs, RenderMs, WeaponMs, EffectsMs;
        public float Drawn, Alive, DrawCalls, Triangles;

        public string Line()
        {
            var c = Config;
            return $"{c.Name} | {c.Count} {c.Camera:0} {c.Weapon} x{c.Storm:0.#} {c.Fire} {B(c.Vat)} {B(c.Lerp)} " +
                   $"{B(c.Shadows)} {B(c.Render)} {B(c.Freeze)} {B(c.Effects)} {c.Resolution:0.##} {B(c.Cull)} | " +
                   $"{AvgFps:0.0} {Low1Fps:0.0} {MedianMs:0.0} | {Alive:0} {Drawn:0} | " +
                   $"{SimMs:0.00} {SimMaxMs:0.00} {FlowMs:0.00} {FovMaxMs:0.00} {RenderMs:0.00} {WeaponMs:0.00} {EffectsMs:0.00} | {DrawCalls:0} {Triangles / 1000f:0} | " +
                   $"{B(c.Officers)} {B(c.Elite)} {c.Champions} {B(c.Shoot)}";
        }

        private static string B(bool value) => value ? "1" : "0";
    }

    /// <summary>
    /// The benchmark's configurations: monster count sweep at the default camera; camera sweep; weapons (bullet
    /// storm, shotgun, flamethrower + 30 burning cells); rendering options (VAT vs bones, frame blending, shadows,
    /// effects, resolution); simulation alone and rendering alone. Base: <see cref="StressTestDefinition.StartCount"/>
    /// monsters, <see cref="StressTestDefinition.StartCameraHalfExtent"/>, machine gun.
    /// </summary>
    public static class BenchmarkPlan
    {
        public static BenchmarkConfig[] Create(StressTestDefinition settings)
        {
            var baseline = new BenchmarkConfig
            {
                Name = "base",
                Count = settings.StartCount,
                Camera = settings.StartCameraHalfExtent,
                Weapon = StressWeapon.MachineGun,
                Storm = 1f,
                Fire = 0,
                Vat = false,
                Lerp = false,
                Shadows = true,
                Render = true,
                Freeze = false,
                Effects = true,
                Resolution = 1f,
                Cull = true,
            };

            var plan = new List<BenchmarkConfig>();
            foreach (var count in settings.MonsterCounts)
            {
                var c = Named(baseline, "count" + count);
                c.Count = count;
                plan.Add(c);
            }

            foreach (var camera in settings.CameraHalfExtents)
            {
                if (camera == baseline.Camera) continue;
                var c = Named(baseline, "cam" + camera);
                c.Camera = camera;
                plan.Add(c);
            }

            var storm = Named(baseline, "storm3");
            storm.Storm = 3f;
            plan.Add(storm);

            var shotgun = Named(baseline, "shotgun3");
            shotgun.Weapon = StressWeapon.Shotgun;
            shotgun.Storm = 3f;
            plan.Add(shotgun);

            var flame = Named(baseline, "flame+fire");
            flame.Weapon = StressWeapon.Flamethrower;
            flame.Fire = settings.Weapons.FireCellsTarget;
            plan.Add(flame);

            var vat = Named(baseline, "vat");
            vat.Vat = true;
            plan.Add(vat);

            var lerp = Named(baseline, "lerp");
            lerp.Lerp = true;
            plan.Add(lerp);

            var vatLerp = Named(baseline, "vat+lerp");
            vatLerp.Vat = true;
            vatLerp.Lerp = true;
            plan.Add(vatLerp);

            var noShadows = Named(baseline, "noShadows");
            noShadows.Shadows = false;
            plan.Add(noShadows);

            var noFx = Named(baseline, "noFx");
            noFx.Effects = false;
            plan.Add(noFx);

            var res75 = Named(baseline, "res0.75");
            res75.Resolution = 0.75f;
            plan.Add(res75);

            var res50 = Named(baseline, "res0.5");
            res50.Resolution = 0.5f;
            plan.Add(res50);

            var simOnly = Named(baseline, "simOnly");
            simOnly.Render = false;
            plan.Add(simOnly);

            var renderOnly = Named(baseline, "renderOnly");
            renderOnly.Freeze = true;
            plan.Add(renderOnly);

            // Worst case for the GPU: the whole horde on screen (no view culling, farthest camera).
            var maxCount = settings.MaxCount;
            var maxCamera = baseline.Camera;
            foreach (var camera in settings.CameraHalfExtents)
                maxCamera = System.Math.Max(maxCamera, camera);
            var allCounts = maxCount > 300 ? new[] { 300, maxCount } : new[] { maxCount };
            foreach (var count in allCounts)
            {
                var all = Named(baseline, "all" + count);
                all.Count = count;
                all.Camera = maxCamera;
                all.Cull = false;
                plan.Add(all);
            }

            var allLerp = Named(baseline, "all" + maxCount + "+lerp");
            allLerp.Count = maxCount;
            allLerp.Camera = maxCamera;
            allLerp.Cull = false;
            allLerp.Lerp = true;
            plan.Add(allLerp);

            var allVat = Named(allLerp, "all" + maxCount + "+vat");
            allVat.Lerp = false;
            allVat.Vat = true;
            plan.Add(allVat);

            var allStorm = Named(allLerp, "all" + maxCount + "+flame+storm");
            allStorm.Lerp = false;
            allStorm.Weapon = StressWeapon.Flamethrower;
            allStorm.Fire = settings.Weapons.FireCellsTarget;
            plan.Add(allStorm);
            return plan.ToArray();
        }

        /// <summary>Monsters of the sandbox's benchmark (stage 5): the crowd every step starts from.</summary>
        public const int SandboxCount = 150;

        /// <summary>
        /// The combat sandbox's benchmark (stage 5, measured on the reference phone): the autopilot runs its circle, the
        /// player fires the bench rifle with aim assist (stage E3); load grows step by step — <see cref="SandboxCount"/> of
        /// the waves' kinds, + officers and Brutes (a closed door: the second flow field), + the elite, + champions,
        /// twice the crowd, and the whole of it on screen.
        /// </summary>
        public static BenchmarkConfig[] CreateSandbox(StressTestDefinition settings)
        {
            var maxCamera = settings.StartCameraHalfExtent;
            foreach (var camera in settings.CameraHalfExtents)
                maxCamera = System.Math.Max(maxCamera, camera);
            var mass = new BenchmarkConfig
            {
                Name = "mass" + SandboxCount,
                Count = SandboxCount,
                Camera = settings.StartCameraHalfExtent,
                Weapon = StressWeapon.None,
                Storm = 1f,
                Shadows = true,
                Render = true,
                Effects = true,
                Resolution = 1f,
                Cull = true,
                Shoot = true,
            };

            var officers = Named(mass, "+officers");
            officers.Officers = true;
            var elite = Named(officers, "+elite");
            elite.Elite = true;
            var champions = Named(elite, "+champs");
            champions.Champions = 12;
            var double300 = Named(champions, "full300");
            double300.Count = 300;
            var all = Named(double300, "all300");
            all.Camera = maxCamera;
            all.Cull = false;
            return new[] { mass, officers, elite, champions, double300, all };
        }

        private static BenchmarkConfig Named(BenchmarkConfig baseline, string name)
        {
            baseline.Name = name;
            return baseline;
        }
    }
}
