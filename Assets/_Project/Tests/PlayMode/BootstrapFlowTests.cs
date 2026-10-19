using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Composition;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Visual;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Presentation.Map;
using Purge.Presentation.UI;
using Purge.Presentation.Visual;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;

namespace Purge.Tests.PlayMode
{
    /// <summary>
    /// Real Bootstrap scene, Addressables and LevelScope (ТЗ §110 PlayMode: Bootstrap, GameFlow, Addressables,
    /// LevelScope, Retry). Uses the first level of the project's LevelCatalog.
    /// </summary>
    public class BootstrapFlowTests
    {
        private const string BootstrapScene = "Bootstrap";
        private const string GameScene = "Game";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private IObjectResolver _container;
        private GameFlow _flow;
        private IAddressablesService _addressables;
        private AudioService _audio;

        /// <summary>Addressables handles without music (it loads and unloads with the flow, asynchronously).</summary>
        private int AppHandles => _addressables.ActiveHandleCount - _audio.MusicHandleCount;

        [UnitySetUp]
        public IEnumerator SetUp() => Async(async () =>
        {
            var loading = SceneManager.LoadSceneAsync(BootstrapScene, LoadSceneMode.Single);
            Assert.IsNotNull(loading, $"Scene '{BootstrapScene}' could not be loaded: is it in Build Settings?");
            await loading.ToUniTask();
            var scope = FindRoot<ProjectLifetimeScope>(SceneManager.GetSceneByName(BootstrapScene));
            Assert.IsNotNull(scope, "Bootstrap scene has no ProjectLifetimeScope.");
            Assert.IsNotNull(scope.Container, "ProjectLifetimeScope was not built (see the console for its exception).");

            _container = scope.Container;
            _flow = _container.Resolve<GameFlow>();
            _addressables = _container.Resolve<IAddressablesService>();
            _audio = _container.Resolve<AudioService>();
            await WaitFor(() => _flow.State == GameFlowState.MainMenu || _flow.State == GameFlowState.Error);
            Assert.AreEqual(GameFlowState.MainMenu, _flow.State, _flow.ErrorMessage);
        });

        [UnityTearDown]
        public IEnumerator TearDown() => Async(async () =>
        {
            var empty = SceneManager.CreateScene("Empty " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (var i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene == empty || !scene.isLoaded)
                    continue;
                var unloading = SceneManager.UnloadSceneAsync(scene);
                if (unloading != null)
                    await unloading.ToUniTask();
            }
        });

        [Test]
        public void Bootstrap_ReachesMainMenu_WithLevelsInCatalog()
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            Assert.Greater(catalog.Levels.Count, 0, "Run Build / Sync for at least one level.");
            var devCatalog = catalog.DevLevels.Count > 0 ? 1 : 0; // Editor and development builds.
            Assert.AreEqual(11 + devCatalog, AppHandles,
                "Only application-wide assets are resident in the menu: level catalog (+ development levels catalog), " +
                "player definition, player visual, combat visual, weapon visuals, item visuals, card deck, content catalog, audio catalog, language catalog, " +
                "texts of one language (music is counted apart).");
            Assert.IsNotNull(_audio.Catalog, "Audio catalog loaded.");
            Assert.AreSame(_audio.Catalog.MenuMusic, _audio.CurrentMusic, "Menu music in the menu.");
        }

        /// <summary>Switching the language re-texts the shown captions and keeps one table loaded.</summary>
        [UnityTest]
        public IEnumerator Language_Switch_RetextsCaptions_AndKeepsOneTable() => Async(async () =>
        {
            var texts = _container.Resolve<LocalizationService>();
            var ui = _container.Resolve<UIRoot>();
            var play = ui.MainMenu.transform.GetComponentsInChildren<TMPro.TMP_Text>(true).First(t => t.name == "Label" &&
                t.transform.parent.name == "PlayButton");
            var original = texts.LanguageIndex;
            var russian = Enumerable.Range(0, texts.Languages.Count).First(i => texts.Languages[i].Code == "ru");
            var english = Enumerable.Range(0, texts.Languages.Count).First(i => texts.Languages[i].Code == "en");
            var handles = AppHandles;
            try
            {
                await texts.SetLanguageAsync(russian);
                Assert.AreEqual("ИГРАТЬ", play.text, "PLAY in Russian.");
                Assert.AreEqual(handles, AppHandles, "The previous table is released.");
                await texts.SetLanguageAsync(english);
                Assert.AreEqual("PLAY", play.text);
            }
            finally
            {
                await texts.SetLanguageAsync(original); // The choice is saved: keep the developer's.
            }
        });

        [UnityTest]
        public IEnumerator StartLevel_BuildsLevelScope_ThenExit_ReleasesEverything() => Async(async () =>
        {
            var levelId = FirstLevelId();
            var handlesInMenu = AppHandles;

            await _flow.StartLevel(levelId);

            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var gameScene = SceneManager.GetSceneByName(GameScene);
            Assert.IsTrue(gameScene.isLoaded);
            var levelScope = FindRoot<LevelLifetimeScope>(gameScene);
            Assert.IsNotNull(levelScope.Container, "LevelScope must be built.");
            Assert.AreSame(_container, levelScope.Parent.Container, "LevelScope is a child of the project scope.");

            var level = levelScope.Container.Resolve<LevelData>();
            var grid = levelScope.Container.Resolve<LevelGrid>();
            Assert.AreEqual(level.Geometry.Width, grid.Width);
            Assert.AreEqual(LevelRunState.Running, levelScope.Container.Resolve<LevelRuntime>().State);
            Assert.Greater(AppHandles, handlesInMenu);

            await _flow.ExitToMenu();

            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
            Assert.IsFalse(SceneManager.GetSceneByName(GameScene).isLoaded, "Game scene must be unloaded.");
            Assert.AreEqual(handlesInMenu, AppHandles, "Level Addressables must be released.");
        });

        [UnityTest]
        public IEnumerator StartLevel_BuildsVisuals_AndExit_DestroysThem() => Async(async () =>
        {
            await _flow.StartLevel(FirstLevelId());
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);

            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var level = container.Resolve<LevelData>();
            var visuals = container.Resolve<LevelVisualSystem>();
            var geometry = visuals.Geometry;
            Assert.Greater(geometry.Chunks.Count, 0);
            Assert.AreEqual(0, geometry.MissingVisuals, "Every cell layer of a synced level has a prefab.");
            Assert.IsTrue(Shader.IsKeywordEnabled(GeometryShader.VisibilityKeyword));

            // Every placed object has a view (zombies' come from ZombieViewPresenter), except player starts; so does
            // every light source next to a wall (its fixture, e.g. a torch).
            var fixtures = level.Lights.Count(l => !LightFixtures.Resolve(level, l).IsEmpty &&
                                                   LightFixtures.TryGetWallSide(level.Geometry, l, out _));
            Assert.AreEqual(fixtures, container.Resolve<LightFixturesView>().Count, "Every mounted light has a fixture.");
            var expectedViews = level.AllEntities().Count(e => !(e is PlayerStartData)) + fixtures;
            Assert.AreEqual(expectedViews, container.Resolve<EntityViewRegistry>().Count);

            var meshes = geometry.Chunks.Select(c => c.Mesh).ToList();
            await _flow.ExitToMenu();

            Assert.IsTrue(meshes.All(m => m == null), "Chunk meshes are runtime assets and must be destroyed.");
            Assert.IsFalse(Shader.IsKeywordEnabled(GeometryShader.VisibilityKeyword));
        });

        [UnityTest]
        public IEnumerator StartLevel_SpawnsPlayerAtStart_WithViewAndCamera() => Async(async () =>
        {
            await _flow.StartLevel(FirstLevelId(), new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);

            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var level = container.Resolve<LevelData>();
            var player = container.Resolve<Purge.Gameplay.Player.PlayerSystem>();
            Assert.IsTrue(player.IsSpawned);
            Assert.AreEqual(level.PlayerStarts[0].Position, player.Cell);
            Assert.AreEqual(player.Cell, container.Resolve<Purge.Gameplay.Grid.OccupancyMap>().PlayerCell);

            var view = container.Resolve<PlayerViewPresenter>().View;
            Assert.IsNotNull(view, "Player view instantiated.");
            Assert.AreEqual(new Vector3(player.Position.x, 0f, player.Position.y), view.transform.position);

            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var camera = container.Resolve<TopDownCamera>().transform;
            var toPlayer = view.transform.position - camera.position;
            Assert.Greater(Vector3.Dot(camera.forward, toPlayer.normalized), 0.99f, "Camera looks at the player.");

            await _flow.ExitToMenu();
            Assert.IsTrue(view == null, "Player view destroyed with the level.");
        });

        [UnityTest]
        public IEnumerator StartLevel_AppliesVisibility_ToGeometryAndObjects() => Async(async () =>
        {
            await _flow.StartLevel(FirstLevelId(), new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);

            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var visibility = container.Resolve<VisibilitySystem>();
            var player = container.Resolve<Purge.Gameplay.Player.PlayerSystem>();
            var grid = container.Resolve<LevelGrid>();
            var geometry = container.Resolve<LevelVisualSystem>().Geometry;

            Assert.IsTrue(visibility.HasResult, "Calculated when the player spawned.");
            Assert.IsTrue(visibility.IsVisible(player.Cell));
            var side = visibility.Radius * 2 + 1;
            Assert.LessOrEqual(visibility.VisibleCells.Count, side * side, "Within the view square (ТЗ: ~21×21).");

            for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                var cell = new GridPosition(x, y);
                Assert.AreEqual(visibility.IsRevealed(cell), geometry.Mask.IsVisible(cell), $"Geometry mask at {cell}.");
            }

            foreach (var chunk in geometry.Chunks)
                Assert.AreEqual(geometry.Mask.AnyVisible(chunk.Cells), chunk.IsVisible, $"Chunk {chunk.Cells}.");

            // Fog of war (the level's theme has a fog material): the first reveal is instant and matches geometry.
            var fog = container.Resolve<FogOfWarView>();
            if (fog.IsActive)
            {
                for (var y = 0; y < grid.Height; y++)
                for (var x = 0; x < grid.Width; x++)
                {
                    var cell = new GridPosition(x, y);
                    Assert.AreEqual(visibility.IsRevealed(cell) ? 1f : 0f, fog.Mask.ValueOf(cell), $"Fog at {cell}.");
                }
            }
            foreach (var view in container.Resolve<EntityViewRegistry>().All)
                Assert.AreEqual(visibility.IsVisible(view.Cell), view.IsVisible, $"View '{view.EntityId}' at {view.Cell}.");

            await _flow.ExitToMenu();
        });

        [UnityTest]
        public IEnumerator DoorsAndPickups_ViewsFollowGameplay() => Async(async () =>
        {
            const string itemsLevel = "Level_Items";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(itemsLevel) == null)
                Assert.Ignore($"Dev level '{itemsLevel}' (doors, key, weapons) is not in the catalog.");

            await _flow.StartLevel(itemsLevel, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var level = container.Resolve<LevelData>();
            var doors = container.Resolve<DoorSystem>();
            var pickups = container.Resolve<PickupSystem>();
            var views = container.Resolve<EntityViewRegistry>();

            var plain = level.Doors.First(d => !d.RequiresKey);
            var locked = level.Doors.First(d => d.RequiresKey);
            Assert.IsTrue(doors.IsLocked(locked.Id));
            Assert.IsTrue(Child(views, locked.Id, "Lock").activeSelf, "Padlock shown on a locked door.");

            // The warm-up drew everything (both door states, every held weapon) and put it all back.
            Assert.Greater(container.Resolve<LevelWarmup>().WarmedCount, views.Count, "Views plus held weapons and effects.");
            Assert.IsTrue(Child(views, plain.Id, "Closed").activeSelf);
            Assert.IsFalse(Child(views, plain.Id, "Open").activeSelf);
            var baseWeapon = container.Resolve<PlayerDefinition>().BaseWeapon;
            Assert.AreEqual("Weapon " + baseWeapon.Id, container.Resolve<PlayerWeaponPresenter>().Shown.name,
                "The base pistol of the empty slot is in hands.");
            var hands = container.Resolve<PlayerViewPresenter>().View.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("Weapon ")).ToList();
            Assert.AreEqual(level.Weapons.Select(w => w.Definition.Id).Append(baseWeapon.Id).Distinct().Count(), hands.Count,
                "Every weapon of the level and the base pistol are created in hands while loading.");
            Assert.IsTrue(hands.All(t => !t.gameObject.activeSelf || t.name == "Weapon " + baseWeapon.Id),
                "Not held yet: hidden (only the base pistol is in hands).");

            doors.SetOpen(plain.Id, true);
            Assert.IsFalse(Child(views, plain.Id, "Closed").activeSelf);
            Assert.IsTrue(Child(views, plain.Id, "Open").activeSelf);
            doors.Unlock(locked.Id);
            Assert.IsFalse(Child(views, locked.Id, "Lock").activeSelf, "Padlock gone after unlocking.");

            var melee = level.Weapons.Where(w => w.Definition.IsMelee).ToList();
            Assert.GreaterOrEqual(melee.Count, 2, "Needs two melee weapons to test dropping.");
            Assert.IsTrue(pickups.TryTakeWeapon(melee[0].Position));
            Assert.IsFalse(views.TryGet(melee[0].Id, out _), "Taken weapon's view is removed.");

            Assert.IsTrue(pickups.TryTakeWeapon(melee[1].Position)); // the second slot
            Assert.IsTrue(pickups.DropWeapon(WeaponSlot.First));
            Assert.IsTrue(views.TryGet(melee[0].Id, out var dropped), "Dropped weapon gets a view again.");
            Assert.AreEqual(container.Resolve<Purge.Gameplay.Player.PlayerSystem>().Cell, dropped.Cell);
            Assert.IsNotNull(dropped.GameObject);

            var held = container.Resolve<PlayerWeaponPresenter>().Shown;
            Assert.IsNotNull(held, "The weapon in hands is shown.");
            Assert.AreEqual("Weapon " + melee[1].Definition.Id, held.name);
            Assert.IsTrue(held.activeInHierarchy);
            var rig = container.Resolve<PlayerViewPresenter>().View.GetComponent<CharacterWeaponRig>();
            Assert.AreEqual(rig.MeleeSocket, held.transform.parent, "Melee weapons sit in the hand socket.");

            await _flow.ExitToMenu();
        });

        [UnityTest]
        public IEnumerator Shooting_BulletViewsArePooled_AndReleased() => Async(async () =>
        {
            const string itemsLevel = "Level_Items";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(itemsLevel) == null)
                Assert.Ignore($"Dev level '{itemsLevel}' (with a ranged weapon) is not in the catalog.");

            var handlesInMenu = AppHandles;
            await _flow.StartLevel(itemsLevel, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var level = container.Resolve<LevelData>();
            var bullets = container.Resolve<Purge.Gameplay.Combat.BulletSystem>();
            var combat = container.Resolve<Purge.Gameplay.Combat.PlayerCombat>();
            var views = container.Resolve<CombatViewPresenter>();

            var gun = level.Weapons.First(w => !w.Definition.IsMelee);
            Assert.IsTrue(container.Resolve<PickupSystem>().TryTakeWeapon(gun.Position));
            var held = container.Resolve<PlayerWeaponPresenter>().Shown;
            Assert.IsNotNull(held, "The gun in hands is shown.");
            Assert.AreEqual("Weapon " + gun.Definition.Id, held.name);

            // After the blend-in: the left palm holds the gun's grip, the bladed rifle stance is turned to the facing.
            var playerView = container.Resolve<PlayerViewPresenter>().View;
            var rig = playerView.GetComponent<CharacterWeaponRig>();
            await WaitFor(() => rig.HandWeight >= 1f);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var grip = held.GetComponent<WeaponModel>().GripLeft;
            Assert.IsNotNull(grip, "Guns have a left hand grip mark.");
            Assert.Less(Vector3.Distance(rig.PalmLeft.position, grip.position), 0.03f, "Left palm on the grip.");
            var animator = playerView.GetComponent<Animator>();
            var across = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position -
                         animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            var chestYaw = Vector3.SignedAngle(playerView.transform.forward,
                Vector3.Cross(Vector3.ProjectOnPlane(across, Vector3.up), Vector3.up), Vector3.up);
            if (rig.MaxTwist > 0f)
                Assert.Less(Mathf.Abs(chestYaw), 25f, $"Chest turned toward the facing (yaw {chestYaw:F0}).");
            // The barrel once the rifle stance has blended in (the animator's transition from the pickup).
            await UniTask.Delay(TimeSpan.FromSeconds(0.6f));
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var barrelYaw = Vector3.SignedAngle(playerView.transform.forward,
                Vector3.ProjectOnPlane(held.GetComponent<WeaponModel>().Muzzle.forward, Vector3.up), Vector3.up);
            if (rig.AimBarrelWeight >= 1f && rig.MaxTwist <= 0f)
                Assert.Less(Mathf.Abs(barrelYaw), 12f, $"Barrel turned toward the line of fire (yaw {barrelYaw:F0}; the clip: about -23).");

            var levelVoices = _audio.PlayingCount(SoundChannel.Level);
            Assert.IsTrue(combat.TryAttack(Vector2.down));
            Assert.AreEqual(1, bullets.Active.Count);
            Assert.Greater(_audio.PlayingCount(SoundChannel.Level), levelVoices, "The shot sounds.");
            Assert.AreEqual(1, views.ActiveBulletViews, "A bullet view taken from the pool.");
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            // The flash is a pack effect since stage Э1 (PackEffectsPlayer), the shell is the presenter's own.
            var packEffects = container.Resolve<PackEffectsPlayer>();
            Assert.GreaterOrEqual(views.ActiveEffects + packEffects.ActiveEffects, 2,
                "Muzzle flash and shell shown after the gun was posed.");

            await WaitFor(() => bullets.Active.Count == 0);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            Assert.AreEqual(0, views.ActiveBulletViews, "Returned to the pool when the bullet ended.");
            await WaitFor(() => views.ActiveEffects == 0);

            await _flow.ExitToMenu();
            Assert.AreEqual(handlesInMenu, AppHandles, "Combat and held weapon prefabs released with the level.");
            Assert.AreEqual(0, _audio.PlayingCount(SoundChannel.Level), "Level sounds stop with the level.");
        });

        [UnityTest]
        public IEnumerator CombatSandbox_EveryWeapon_HurtsTheHorde() => Async(async () =>
        {
            const string sandboxLevel = "Level_Sandbox";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(sandboxLevel) == null)
                Assert.Ignore($"Dev level '{sandboxLevel}' is not in the catalog.");

            var handlesInMenu = AppHandles;
            await _flow.StartLevel(sandboxLevel, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var level = container.Resolve<LevelData>();
            var horde = container.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            var pickups = container.Resolve<PickupSystem>();
            var weapons = container.Resolve<Purge.Gameplay.Weapons.WeaponSystem>();
            var combat = container.Resolve<Purge.Gameplay.Combat.PlayerCombat>();
            var targets = container.Resolve<Purge.Gameplay.Combat.CombatTargets>();
            var player = container.Resolve<PlayerSystem>();
            Assert.IsTrue(horde.IsActive, "The sandbox has a horde.");
            Assert.IsTrue(weapons.Active != null && weapons.Active.IsBase, "The base pistol is in hands.");
            Assert.Greater(level.Weapons.Count, 0);
            horde.GodMode = true;

            var hits = 0;
            Action<Purge.Gameplay.Combat.CombatTarget, Vector2, float, Vector2, bool> count = (target, _, _, _, _) =>
            {
                if (target.Agent >= 0) hits++;
            };
            targets.Damaged += count;
            // Feel (stage 4в): a weapon's hit knocks the monster back (WeaponFeelSystem handles it first).
            var pushed = 0;
            Action<Purge.Gameplay.Combat.CombatTarget, Vector2, Vector2, Purge.Core.Definitions.WeaponDefinition, bool> feel =
                (target, _, _, _, _) =>
                {
                    if (target.Agent >= 0 && horde.PushVelocities[target.Agent].sqrMagnitude > 0f) pushed++;
                };
            targets.WeaponDamaged += feel;
            try
            {
                foreach (var pickup in level.Weapons)
                {
                    var name = pickup.Definition.name;
                    Assert.IsTrue(pickups.TryTakeWeapon(pickup.Position), $"Takes {name}.");
                    Assert.AreSame(pickup.Definition, weapons.Active.Definition);
                    hits = 0;
                    // At the nearest monster until one is hurt: the horde comes to the player, so melee reaches too.
                    var until = Time.realtimeSinceStartup + 12f;
                    while (hits == 0 && Time.realtimeSinceStartup < until)
                    {
                        var nearest = horde.FindNearest(player.Position, 40f);
                        if (nearest >= 0)
                            combat.TryAttack((AgentPosition(horde, nearest) - player.Position).normalized);
                        await UniTask.Yield();
                    }

                    Assert.Greater(hits, 0, $"{name} hurts the horde.");
                }

                Assert.Greater(pushed, 0, "Hits knock monsters back.");
            }
            finally
            {
                targets.Damaged -= count;
                targets.WeaponDamaged -= feel;
            }

            await _flow.ExitToMenu();
            Assert.AreEqual(handlesInMenu, AppHandles, "The sandbox's assets released with the level.");
        });

        [UnityTest]
        public IEnumerator CombatSandbox_Throwables_Zones_AndDrops() => Async(async () =>
        {
            const string sandboxLevel = "Level_Sandbox";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(sandboxLevel) == null)
                Assert.Ignore($"Dev level '{sandboxLevel}' is not in the catalog.");

            var handlesInMenu = AppHandles;
            await _flow.StartLevel(sandboxLevel, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var horde = container.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            var player = container.Resolve<PlayerSystem>();
            var throwing = container.Resolve<Purge.Gameplay.Throwing.PlayerThrowing>();
            var sandbox = container.Resolve<Purge.Gameplay.Horde.CombatSandbox>();
            var zones = container.Resolve<Purge.Gameplay.Effects.ZoneSystem>();
            var drops = container.Resolve<Purge.Gameplay.Drops.DropSystem>();
            var targets = container.Resolve<Purge.Gameplay.Combat.CombatTargets>();
            var dropViews = container.Resolve<Purge.Presentation.Visual.DropViewPresenter>();
            horde.GodMode = true;
            Assert.IsFalse(throwing.Slot.IsEmpty, "The sandbox gives a stack of throwables.");
            Assert.IsTrue(drops.IsActive, "The sandbox's horde drops items.");

            var hits = 0;
            Action<Purge.Gameplay.Combat.CombatTarget, Vector2, float, Vector2, bool> count = (target, _, _, _, _) =>
            {
                if (target.Agent >= 0) hits++;
            };
            targets.Damaged += count;
            try
            {
                // A grenade at the nearest monster hurts the horde.
                var until = Time.realtimeSinceStartup + 12f;
                while (hits == 0 && Time.realtimeSinceStartup < until)
                {
                    var nearest = horde.FindNearest(player.Position, 7f);
                    if (nearest >= 0 && throwing.Cooldown <= 0f)
                    {
                        if (throwing.Slot.IsEmpty) sandbox.Refill();
                        throwing.TryThrow(AgentPosition(horde, nearest));
                    }

                    await UniTask.Yield();
                }

                Assert.Greater(hits, 0, "The grenade hurts the horde.");

                // A molotov puts fire on cells.
                sandbox.NextThrowable();
                Assert.AreEqual("molotov", throwing.Slot.Definition.Id);
                await WaitFor(() => throwing.Cooldown <= 0f);
                Assert.IsTrue(throwing.TryThrow(player.Position + Vector2.up * 3f));
                await WaitFor(() => zones.Count > 0);

                // Killed monsters drop items (40 % of the table is something).
                until = Time.realtimeSinceStartup + 15f;
                while (drops.Dropped == 0 && Time.realtimeSinceStartup < until)
                {
                    var nearest = horde.FindNearest(player.Position, 40f);
                    if (nearest >= 0) targets.Damage(Purge.Gameplay.Combat.CombatTarget.OfAgent(nearest), 1000f, Vector2.up);
                    await UniTask.Yield();
                }

                Assert.Greater(drops.Dropped, 0, "Something dropped.");
                await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                Assert.Greater(dropViews.LyingCount, 0, "Dropped items are shown from the pool.");
            }
            finally
            {
                targets.Damaged -= count;
            }

            await _flow.ExitToMenu();
            Assert.AreEqual(handlesInMenu, AppHandles, "The items' looks released with the level.");
        });

        private static Vector2 AgentPosition(Purge.Gameplay.Horde.HordeSystem horde, int agent) => horde.Positions[agent];

        [UnityTest]
        public IEnumerator Zombies_HaveViews_FollowGameplay_AndDisappearWhenKilled() => Async(async () =>
        {
            const string itemsLevel = "Level_Items";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(itemsLevel) == null)
                Assert.Ignore($"Dev level '{itemsLevel}' (with zombies) is not in the catalog.");

            await _flow.StartLevel(itemsLevel, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var zombies = container.Resolve<Purge.Gameplay.Zombies.ZombieSystem>();
            var views = container.Resolve<EntityViewRegistry>();
            Assert.Greater(zombies.TotalCount, 0, "Level_Items has zombies.");

            foreach (var zombie in zombies.Zombies)
            {
                Assert.IsTrue(views.TryGet(zombie.Id, out var view), $"View of '{zombie.Id}'.");
                Assert.AreEqual(zombie.Cell, view.Cell);
            }

            // Let the AI run and check views follow.
            await UniTask.Delay(TimeSpan.FromSeconds(1.5));
            foreach (var zombie in zombies.Zombies)
            {
                views.TryGet(zombie.Id, out var view);
                var position = view.GameObject.transform.localPosition;
                Assert.AreEqual(zombie.Position.x, position.x, 1e-3f);
                Assert.AreEqual(zombie.Position.y, position.z, 1e-3f);
                Assert.AreEqual(zombie.Cell, view.Cell, "Visibility uses the zombie's current cell.");
            }

            var footprints = container.Resolve<FootprintsView>().Field;
            Assert.IsNotNull(footprints, "The theme has footprints (Purge → Dev → Build Footprints).");
            Assert.AreEqual(2, container.Resolve<VisionZonesView>().ZoneCount, "Walker and Hunter have vision zones, Listener none.");

            // Health bars and numbers (Purge → Dev → Build Damage Numbers).
            var feedback = container.Resolve<CombatFeedbackView>();
            var health = container.Resolve<Purge.Gameplay.Player.PlayerHealth>();
            zombies.Zombies[1].ApplyDamage(5f, Vector2.right);
            health.Damage(7);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            health.HealToFull();
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            Assert.AreEqual(1, feedback.ActiveBars, "The damaged zombie shows its bar.");
            CollectionAssert.AreEquivalent(new[] { "5", "7", "+7" }, feedback.NumberTexts());
            Assert.Greater(feedback.DrawnQuads, 0, "The player's numbers are always drawn.");
            await UniTask.Delay(TimeSpan.FromSeconds(container.Resolve<CombatVisualDefinition>().Feedback.NumberLifetime + 0.2f));
            Assert.AreEqual(0, feedback.ActiveNumbers, "Numbers live briefly.");

            var victim = zombies.Zombies[0];
            victim.ApplyDamage(10000f, Vector2.right);
            Assert.AreEqual(1, zombies.KilledCount);
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate); // hit reactions are shown in the late tick
            CollectionAssert.AreEqual(new[] { "999" }, feedback.NumberTexts(), "The killing hit shows its number.");
            if (views.TryGet(victim.Id, out var corpse) && corpse.GameObject.GetComponentInChildren<Animator>() is { } animator)
                Assert.IsTrue(animator.GetBool(ZombieAnimatorParameters.Dead), "The corpse plays the death.");
            await UniTask.Delay(TimeSpan.FromSeconds(ZombieViewPresenter.CorpseTime + 0.5f));
            Assert.IsFalse(views.TryGet(victim.Id, out _), "Killed zombie's view is removed after the death animation.");
            Assert.AreEqual(0, feedback.ActiveBars, "The bar goes some seconds after the last hit.");

            await _flow.ExitToMenu();
        });

        [UnityTest]
        public IEnumerator Map_Icons_AndCompletionByKillingAll_SavesStars() => Async(async () =>
        {
            const string itemsLevel = "Level_Items";
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Find(itemsLevel) == null)
                Assert.Ignore($"Dev level '{itemsLevel}' is not in the catalog.");

            // Completion writes the real save (PlayerPrefs): keep the developer's progress intact.
            var savedJson = PlayerPrefs.HasKey(PlayerPrefsSaveStorage.Key) ? PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key) : null;
            var save = _container.Resolve<SaveService>();
            try
            {
                await _flow.StartLevel(itemsLevel, new LevelLaunchOptions(startIndex: 0));
                Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
                var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
                var level = container.Resolve<LevelData>();
                var grid = container.Resolve<LevelGrid>();
                var presenter = container.Resolve<MapPresenter>();
                var ui = _container.Resolve<UIRoot>();

                for (var i = 0; i < grid.CellCount; i++)
                {
                    var cell = grid.ToPosition(i);
                    Assert.AreEqual(MapRenderer.ColorOf(grid.GetCell(cell)), (Color32)presenter.Texture.GetPixel(cell.X, cell.Y),
                        $"The whole level is drawn: {cell}.");
                }

                var runtime = container.Resolve<LevelRuntime>();
                _flow.OpenMap();
                Assert.AreEqual(GameFlowState.Map, _flow.State);
                Assert.AreEqual(LevelRunState.Paused, runtime.State, "ТЗ §60: the map pauses gameplay.");
                Assert.IsTrue(ui.Map.IsVisible);
                Assert.IsFalse(ui.Hud.IsVisible);

                // Icons: refreshed on opening; the start, doors, items, zombies and the player — each with its switch.
                var icons = presenter.Icons;
                var player = container.Resolve<PlayerSystem>();
                var playerIcon = icons[icons.Count - 1];
                Assert.AreEqual(MapIconKind.Player, playerIcon.Kind);
                Assert.IsTrue(playerIcon.Visible);
                Assert.AreEqual(MapIconLayout.CenterOf(player.Position), playerIcon.Center);
                Assert.IsTrue(icons[0].Visible, "The start.");
                Assert.IsTrue(icons.Where(icon => icon.Kind == MapIconKind.Door || icon.Kind == MapIconKind.LockedDoor)
                    .All(icon => icon.Visible), "Doors.");

                var zombies = container.Resolve<Purge.Gameplay.Zombies.ZombieSystem>();
                Assert.Greater(zombies.TotalCount, 0, "Level_Items has zombies.");
                var zombieIcons = icons.Where(icon => icon.Kind == MapIconKind.Zombie).ToList();
                Assert.AreEqual(zombies.TotalCount, zombieIcons.Count);
                Assert.IsTrue(zombieIcons.All(icon => icon.Visible), "Living zombies are shown wherever they are.");
                Assert.AreEqual(MapIconLayout.CenterOf(zombies.Zombies[0].Position), zombieIcons[0].Center);
                Assert.AreEqual(level.Keys.Count, icons.Count(icon => icon.Kind == MapIconKind.Key && icon.Visible));
                Assert.AreEqual(level.Medkits.Count, icons.Count(icon => icon.Kind == MapIconKind.Medkit && icon.Visible));
                Assert.AreEqual(level.Weapons.Count, icons.Count(icon => icon.Kind == MapIconKind.Weapon && icon.Visible));

                var zombieToggle = ui.Map.transform.Find("SafeArea/ShowZombiesToggle").GetComponent<Toggle>();
                var iconCount = presenter.Icons.Count;
                zombieToggle.isOn = false;
                Assert.IsFalse(presenter.Icons.Any(icon => icon.Kind == MapIconKind.Zombie && icon.Visible), "Zombies switched off.");
                Assert.AreEqual(iconCount, presenter.Icons.Count, "The icon count never changes.");
                zombieToggle.isOn = true;

                var playerToggle = ui.Map.transform.Find("SafeArea/ShowPlayerToggle").GetComponent<Toggle>();
                Assert.IsTrue(playerToggle.isOn);
                playerToggle.isOn = false;
                Assert.IsFalse(presenter.Icons[presenter.Icons.Count - 1].Visible, "Player toggle hides the icon.");
                Assert.IsFalse(_container.Resolve<SettingsService>().MapShowPlayer, "Saved in the settings.");
                playerToggle.isOn = true;

                _flow.CloseMap();
                Assert.AreEqual(LevelRunState.Running, runtime.State);

                // Temporary win rule (until waves): the last zombie killed completes the level.
                foreach (var zombie in zombies.Zombies.ToList())
                    if (zombie.IsAlive) zombie.ApplyDamage(100000f, Vector2.left);
                Assert.AreEqual(GameFlowState.Completed, _flow.State);
                var result = _flow.LastResult.Value;
                Assert.IsTrue(result.AllZombiesKilled);
                Assert.AreEqual(LevelResult.MaxStars, result.Stars);
                Assert.AreEqual(LevelResult.MaxStars, _container.Resolve<IProgressService>().GetStars(itemsLevel));
                Assert.IsTrue(ui.Result.IsVisible);

                await _flow.ExitToMenu();
                Assert.IsNull(presenter.Texture, "Map texture destroyed with the level.");
            }
            finally
            {
                if (savedJson != null) PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, savedJson);
                else PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
                PlayerPrefs.Save();
                save.Load();
            }
        });

        private static GameObject Child(EntityViewRegistry views, string entityId, string child)
        {
            Assert.IsTrue(views.TryGet(entityId, out var view), $"No view for '{entityId}'.");
            var found = view.GameObject.transform.Find(child);
            Assert.IsNotNull(found, $"'{entityId}' has no '{child}' (placeholder doors: Purge → Dev → Rebuild Placeholder Doors).");
            return found.gameObject;
        }

        [UnityTest]
        public IEnumerator Retry_AfterFail_ReloadsLevel_WithSingleLevelScope() => Async(async () =>
        {
            var levelId = FirstLevelId();
            var handlesInMenu = AppHandles;
            await _flow.StartLevel(levelId);
            var firstRuntime = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container.Resolve<LevelRuntime>();

            _flow.FailLevel();
            Assert.AreEqual(GameFlowState.Failed, _flow.State);
            await _flow.RetryLevel();

            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            Assert.AreEqual(LevelRunState.Disposed, firstRuntime.State, "Old LevelScope must be disposed.");
            var gameScenes = 0;
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name == GameScene) gameScenes++;
            Assert.AreEqual(1, gameScenes, "Only one LevelScope at a time (ТЗ §7).");

            await _flow.ExitToMenu();
            Assert.AreEqual(handlesInMenu, AppHandles);
        });

        [UnityTest]
        public IEnumerator ExitToMenu_DuringLoading_LeavesNoLevelBehind() => Async(async () =>
        {
            var handlesInMenu = AppHandles;
            var loading = _flow.StartLevel(FirstLevelId());
            await _flow.ExitToMenu();
            await loading;

            Assert.AreEqual(GameFlowState.MainMenu, _flow.State);
            Assert.IsFalse(_flow.HasLevel);
            Assert.IsFalse(SceneManager.GetSceneByName(GameScene).isLoaded);
            Assert.AreEqual(handlesInMenu, AppHandles);
        });

        /// <summary>
        /// The tutorial from its menu button: the first step's window pauses the level, Continue resumes; the reminder,
        /// the arrow shows the step; the map opened counts for the map step.
        /// </summary>
        [UnityTest]
        public IEnumerator Tutorial_WindowPausesTheLevel_ContinueResumes() => Async(async () =>
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Tutorial == null) Assert.Ignore("No tutorial level: Build / Sync the level with a tutorial script.");
            var ui = _container.Resolve<UIRoot>();
            Assert.IsTrue(ui.MainMenu.transform.GetComponentsInChildren<Button>(true)
                .Any(b => b.name == "TutorialButton" && b.gameObject.activeSelf), "The menu shows the Tutorial button.");

            await _flow.StartLevel(catalog.Tutorial.LevelId);
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            Assert.IsTrue(_flow.IsTutorialLevel);
            Assert.IsTrue(ui.Tutorial.IsVisible);

            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var tutorial = container.Resolve<Purge.Gameplay.Tutorial.TutorialSystem>();
            var runtime = container.Resolve<LevelRuntime>();
            Assert.AreEqual(0, tutorial.StepIndex, "The first step is on.");

            await WaitFor(() => _flow.State == GameFlowState.Tip);
            Assert.AreEqual(LevelRunState.Paused, runtime.State, "The window pauses the level.");
            Assert.IsTrue(ui.Hud.IsVisible, "The HUD stays under the window.");
            var dialog = ui.Tutorial.transform.Find("Dialog").gameObject;
            Assert.IsTrue(dialog.activeSelf);

            await WaitFor(() => ui.Tutorial.CanContinue);
            _flow.CloseTip();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.AreEqual(LevelRunState.Running, runtime.State);
            Assert.IsFalse(dialog.activeSelf);
            Assert.IsTrue(ui.Tutorial.transform.Find("HintRow").gameObject.activeSelf, "The reminder stays.");
            await UniTask.Yield();
            await UniTask.Yield();
            Assert.IsTrue(ui.Tutorial.transform.Find("Pointer").gameObject.activeSelf, "The arrow points at the target.");

            await _flow.ExitToMenu();
            Assert.IsFalse(ui.Tutorial.IsVisible);
        });

        private string FirstLevelId()
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Levels.Count == 0)
                Assert.Ignore("Level catalog is empty: run Build / Sync for a level.");
            return catalog.Levels[0].LevelId;
        }

        private static T FindRoot<T>(Scene scene) where T : UnityEngine.Component
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<T>(out var component))
                    return component;
            return null;
        }

        /// <summary>
        /// Runs an async test body as a coroutine. Must stay a compiler-generated iterator: when play mode is entered
        /// without a domain reload (MCP for Unity runs PlayMode tests that way) right after an EditMode run, the Test
        /// Framework keeps its EditMode helper and reads the iterator state field ("&lt;&gt;1__state") of every
        /// [UnitySetUp]/[UnityTest] enumerator. UniTask's own enumerator has no such field → NullReferenceException.
        /// </summary>
        private static IEnumerator Async(Func<UniTask> body)
        {
            yield return UniTask.ToCoroutine(body);
        }

        private static async UniTask WaitFor(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            await UniTask.WaitUntil(condition, cancellationToken: timeout.Token);
        }
    }
}

