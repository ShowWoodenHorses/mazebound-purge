using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Purge.Application.Assets;
using Purge.Application.Flow;
using Purge.Application.Levels;
using Purge.Application.Meta;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Composition;
using Purge.Core.Definitions;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Core.Navigation;
using Purge.Core.Visual;
using Purge.Gameplay.Doors;
using Purge.Gameplay.Level;
using Purge.Gameplay.Pickups;
using Purge.Gameplay.Player;
using Purge.Gameplay.Visibility;
using Purge.Presentation.Map;
using Purge.Presentation.Menu;
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
        private string _savedProgress;

        /// <summary>Addressables handles without music (it loads and unloads with the flow, asynchronously).</summary>
        private int AppHandles => _addressables.ActiveHandleCount - _audio.MusicHandleCount;

        [UnitySetUp]
        public IEnumerator SetUp() => Async(async () =>
        {
            _savedProgress = PlayerPrefs.HasKey(PlayerPrefsSaveStorage.Key)
                ? PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key) : null;
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
            await WaitFor(() => _flow.State == GameFlowState.MainMenu || _flow.State == GameFlowState.Error || _flow.IsTutorialLevel && _flow.State == GameFlowState.Playing);
            if (_flow.IsTutorialLevel && _flow.State == GameFlowState.Playing) await _flow.ExitToMenu();
            Assert.AreEqual(GameFlowState.MainMenu, _flow.State, _flow.ErrorMessage);
            var save = _container.Resolve<SaveService>();
            var game = save.Data.Settings.Game;
            game.NoGameplayHints = true; // Gameplay hints have a separate test that verifies the pause flow.
            save.Data.Settings.Game = game;
            var podium = _container.Resolve<MenuPodiumPresenter>();
            await WaitFor(() => !podium.IsLoading); // its assets stay for the application's lifetime
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

            if (_savedProgress == null) PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
            else PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, _savedProgress);
            PlayerPrefs.Save();
        });

        [UnityTest]
        public IEnumerator MenuTutorial_SelectsDesertAndStartsFirstLevelAfterPurchase() => Async(async () =>
        {
            var save = _container.Resolve<SaveService>();
            var catalog = _container.Resolve<ILevelCatalog>();
            var ui = _container.Resolve<UIRoot>();
            var desert = _container.Resolve<ArsenalPresenter>().GuidedWeapon;
            Assert.IsNotNull(desert);

            save.Data.Levels.Clear();
            save.Data.Levels.Add(new LevelRecord { LevelId = catalog.Tutorial.LevelId, CompletedMask = 1 });
            save.Data.Run = null;
            save.Data.Arsenal.Clear();
            save.Data.Money = desert.PurchasePrice;

            var play = ui.MainMenu.GetComponentsInChildren<Button>(true).Single(b => b.name == "PlayButton");
            play.onClick.Invoke();
            Assert.IsTrue(ui.Arsenal.IsVisible);
            var buy = ui.Arsenal.GetComponentsInChildren<Button>(true).Single(b => b.name == "Buy");
            Assert.IsTrue(buy.interactable);
            buy.onClick.Invoke();

            await WaitFor(() => _flow.State == GameFlowState.Playing || _flow.State == GameFlowState.Error);
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            Assert.AreEqual(catalog.Levels[0].LevelId, _flow.CurrentLevelId);
            Assert.IsNotNull(save.Data.FindItem(desert.Id));
            Assert.AreEqual(0, save.Data.Money);
        });

        [UnityTest]
        public IEnumerator DailyTasksTrackARealKillAndPayOnlyWhenClaimedFromTheMenu() => Async(async () =>
        {
            var save = _container.Resolve<SaveService>();
            var ui = _container.Resolve<UIRoot>();
            var day = DailyTasks.LocalDay;
            var killTask = new DailyTaskSave { Id = "kills_60", Progress = 59 };
            save.Data.Daily = new DailySave
            {
                TaskDay = day,
                Tasks = new List<DailyTaskSave>
                {
                    killTask, new DailyTaskSave { Id = "wave_2_easy" }, new DailyTaskSave { Id = "waves_3" }
                }
            };
            save.Data.Run = null;
            Button ButtonNamed(Component screen, string name) =>
                screen.GetComponentsInChildren<Button>(true).Single(button => button.name == name);

            await _flow.StartLevel("Level_Forest_01", new LevelLaunchOptions(survival: true));
            Assert.That(_flow.State, Is.EqualTo(GameFlowState.Playing), _flow.ErrorMessage);
            var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var waves = level.Resolve<Purge.Gameplay.Waves.WaveSystem>();
            var horde = level.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            Assert.That(waves.StartNextWave(), Is.True);
            horde.Tick(0.05f);
            waves.Tick(0.05f);
            var living = Enumerable.Range(0, horde.HighWater)
                .Where(slot => Purge.Gameplay.Horde.HordeSystem.IsAlive(horde.States[slot])).ToArray();
            Assert.That(living, Is.Not.Empty);
            Assert.That(horde.Damage(living[0], 100000f), Is.True);
            Assert.That(killTask.Progress, Is.EqualTo(60));
            Assert.That(killTask.Claimed, Is.False);

            await _flow.ExitToMenu();
            var before = save.Data.Money;
            ButtonNamed(ui.MainMenu, "TasksButton").onClick.Invoke();
            Assert.That(ui.Tasks.IsVisible, Is.True);
            var claim = ButtonNamed(ui.Tasks, "Claim0");
            Assert.That(claim.interactable, Is.True);
            claim.onClick.Invoke();
            Assert.That(save.Data.Money, Is.EqualTo(before + 35));
            Assert.That(claim.interactable, Is.False);
            claim.onClick.Invoke();
            Assert.That(save.Data.Money, Is.EqualTo(before + 35));

            var loginReward = ButtonNamed(ui.Tasks, "ClaimReward");
            loginReward.onClick.Invoke();
            Assert.That(save.Data.Money, Is.EqualTo(before + 50));
            Assert.That(loginReward.interactable, Is.False);
            ButtonNamed(ui.Tasks, "Back").onClick.Invoke();
            Assert.That(ui.MainMenu.IsVisible, Is.True);
        });

        [UnityTest]
        public IEnumerator SurvivalOpensLockedCastleAndStartsTheTenthWaveBoss() => Async(async () =>
        {
            var save = _container.Resolve<SaveService>();
            var progress = _container.Resolve<IProgressService>();
            var ui = _container.Resolve<UIRoot>();
            save.Data.Levels.Clear();
            save.Data.Survival.Clear();
            save.Data.Run = null;
            save.Data.Loadout = null;
            save.Data.Arsenal.Clear();
            save.Data.Arsenal.Add(new OwnedItem { Id = "desert" });
            Assert.That(progress.IsUnlocked("Level_Castle_01"), Is.False);

            ui.MainMenu.GetComponentsInChildren<Button>(true).Single(b => b.name == "SurvivalButton").onClick.Invoke();
            Assert.That(ui.RunSetup.IsVisible, Is.True);
            var castle = ui.RunSetup.GetComponentsInChildren<Button>(true).Single(b => b.name == "Location1");
            Assert.That(castle.interactable, Is.True);
            castle.onClick.Invoke();
            ui.RunSetup.GetComponentsInChildren<Button>(true).Single(b => b.name == "FightButton").onClick.Invoke();
            await WaitFor(() => _flow.State == GameFlowState.Playing || _flow.State == GameFlowState.Error);
            Assert.That(_flow.State, Is.EqualTo(GameFlowState.Playing), _flow.ErrorMessage);
            Assert.That(_flow.CurrentLevelId, Is.EqualTo("Level_Castle_01"));
            Assert.That(_flow.CurrentLaunch.Survival, Is.True);

            var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var waves = level.Resolve<Purge.Gameplay.Waves.WaveSystem>();
            var horde = level.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            var drops = level.Resolve<Purge.Gameplay.Drops.DropSystem>();
            var rewards = new List<Purge.Gameplay.Drops.DropKind>();
            drops.ItemDropped += (roll, _) => rewards.Add(roll.Kind);
            Assert.That(waves.StartNextWave(), Is.True);
            Assert.That(waves.RemainingCount, Is.EqualTo(10));
            horde.Tick(0.05f);
            waves.Tick(0.05f);
            var living = Enumerable.Range(0, horde.HighWater)
                .Where(slot => Purge.Gameplay.Horde.HordeSystem.IsAlive(horde.States[slot])).ToArray();
            Assert.That(living.Length, Is.GreaterThanOrEqualTo(3));
            foreach (var slot in living.Take(2)) Assert.That(horde.Damage(slot, 100000f), Is.True);
            rewards.Clear();
            Assert.That(horde.Damage(living[2], 100000f), Is.True);
            Assert.That(rewards, Does.Contain(Purge.Gameplay.Drops.DropKind.Ammo));
            Assert.That(rewards, Does.Contain(Purge.Gameplay.Drops.DropKind.Weapon));
            foreach (var slot in living.Skip(3)) horde.Damage(slot, 100000f);

            waves.ResumeAt(9);
            level.Resolve<Purge.Gameplay.Run.RunScore>().Restore(9, 9, 4500);
            Assert.That(waves.StartNextWave(), Is.True);
            Assert.That(waves.IsBossWave, Is.True);
            Assert.That(waves.RemainingCount, Is.EqualTo(98), "The boss and its escort count toward the wave total.");
            var boss = level.Resolve<Purge.Gameplay.Horde.BossSystem>();
            await WaitFor(() => boss.IsBossAlive);
            Assert.That(progress.LoadRun().Survival, Is.True);
            _flow.FailLevel();
            Assert.That(progress.FindSurvival("castle").BestScore, Is.EqualTo(4500));
            Assert.That(progress.FindSurvival("castle").BestWave, Is.EqualTo(9));
            Assert.That(progress.IsUnlocked("Level_Castle_01"), Is.False);
            Assert.That(progress.HasRun, Is.False);
        });

        [UnityTest]
        public IEnumerator GameplayHints_SettingsAndPausePersistTheSeenHint() => Async(async () =>
        {
            var ui = _container.Resolve<UIRoot>();
            var save = _container.Resolve<SaveService>();
            var settings = _container.Resolve<SettingsService>();
            var catalog = _container.Resolve<ILevelCatalog>();
            save.Data.SeenHints.Clear();
            save.Data.Run = null;
            await _flow.StartLevel(catalog.Levels[0].LevelId);
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            await UniTask.Delay(500);
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsEmpty(save.Data.SeenHints);

            var game = settings.Game;
            game.NoGameplayHints = false;
            settings.PreviewGame(game);
            await WaitFor(() => _flow.State == GameFlowState.Tip);
            Assert.IsTrue(ui.Tutorial.IsVisible);
            Assert.AreEqual(1, save.Data.SeenHints.Count);
            var seen = save.Data.SeenHints[0];
            var persisted = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key));
            Assert.That(persisted.SeenHints, Does.Contain(seen));

            await WaitFor(() => ui.Tutorial.CanContinue);
            var next = ui.Tutorial.GetComponentsInChildren<Button>(true).Single(b => b.name == "ContinueButton");
            next.onClick.Invoke();
            Assert.AreEqual(GameFlowState.Playing, _flow.State);
            Assert.IsFalse(ui.Tutorial.IsVisible);
        });

        [UnityTest]
        public IEnumerator CharacterTree_MenuPurchaseAndRunCheckpoint() => Async(async () =>
        {
            Button ButtonNamed(UIScreen screen, string name) =>
                screen.GetComponentsInChildren<Button>(true).Single(button => button.name == name);

            var ui = _container.Resolve<UIRoot>();
            var save = _container.Resolve<SaveService>();
            var original = PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key, string.Empty);
            var oldNodes = save.Data.CharacterNodes.ToArray();
            var oldMoney = save.Data.Money;
            var oldRun = save.Data.Run;
            try
            {
                save.Data.CharacterNodes.Clear();
                save.Data.Money = 2000;
                save.Data.Run = new RunSave();
                var character = ButtonNamed(ui.MainMenu, "CharacterButton");
                Assert.IsTrue(character.interactable);
                character.onClick.Invoke();
                Assert.IsTrue(ui.Character.IsVisible);
                ButtonNamed(ui.Character, "SecondWind").onClick.Invoke();
                ButtonNamed(ui.Character, "Buy").onClick.Invoke();
                Assert.That(save.Data.Money, Is.EqualTo(1700));
                Assert.That(save.Data.CharacterNodes, Does.Contain("SecondWind"));
                ButtonNamed(ui.Character, "Back").onClick.Invoke();
                Assert.IsTrue(ui.MainMenu.IsVisible);

                var skills = _container.Resolve<Purge.Application.Meta.RunPreparation>().Skills;
                var shared = _container.Resolve<SharedDefinitionsService>();
                var second = shared.Content.Items.OfType<WeaponDefinition>()
                    .First(weapon => weapon.UsesAmmo && weapon.Id != shared.Player.BaseWeapon.Id);
                var boosterIds = new[] { "booster_second_barrel", "booster_minefield", "booster_quiet_start" };
                var boosters = boosterIds.Select(shared.Content.Find<Purge.Core.Boosters.BoosterDefinition>).ToArray();
                Assert.That(boosters.All(booster => booster != null), Is.True);
                await _flow.StartLevel(FirstLevelId(), new LevelLaunchOptions(startIndex: 0,
                    skills: skills | CharacterSkills.Barrier, secondWeapon: second, boosters: boosters));
                Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
                var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
                Assert.That(level.Resolve<PlayerRevival>().HasSkill, Is.True);
                var weapons = level.Resolve<Purge.Gameplay.Weapons.WeaponSystem>();
                var secondSlot = weapons.Get(WeaponSlot.Second);
                Assert.That(secondSlot.Definition.Id, Is.EqualTo(second.Id));
                Assert.That(secondSlot.Ammo, Is.GreaterThanOrEqualTo(second.MagazineSize));
                var waves = level.Resolve<Purge.Gameplay.Waves.WaveSystem>();
                Assert.That(waves.StartNextWave(), Is.True);
                var health = level.Resolve<PlayerHealth>();
                Assert.That(health.Barrier, Is.EqualTo(Mathf.RoundToInt(health.Max * 0.1f)));
                var snapshot = _container.Resolve<IProgressService>().LoadRun();
                Assert.IsNotNull(snapshot);
                Assert.That(snapshot.Skills, Is.EqualTo(skills | CharacterSkills.Barrier));
                Assert.That(snapshot.Barrier, Is.EqualTo(health.Barrier));
                Assert.That(snapshot.Boosters, Is.EquivalentTo(boosterIds));
                Assert.That(level.Resolve<Purge.Gameplay.Run.SpecialRunBoosters>().Mines.Count, Is.InRange(1, 4));
                Assert.That(level.Resolve<Purge.Gameplay.Sound.SoundEventBus>().PlayerNoiseFactor, Is.EqualTo(0.4f));
            }
            finally
            {
                save.Data.Money = oldMoney;
                save.Data.Run = oldRun;
                save.Data.CharacterNodes.Clear();
                save.Data.CharacterNodes.AddRange(oldNodes);
                if (string.IsNullOrEmpty(original)) PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
                else PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, original);
                PlayerPrefs.Save();
            }
        });

        [UnityTest]
        public IEnumerator DevCheats_ButtonsChangeTestStateWithoutSavingIt() => Async(async () =>
        {
            if (!Debug.isDebugBuild) Assert.Ignore("Development UI only.");
            const string levelId = "Level_Run";
            if (_container.Resolve<ILevelCatalog>().Find(levelId) == null) Assert.Ignore("Dev run level missing.");
            var savedJson = PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key, null);
            var save = _container.Resolve<SaveService>();
            var progress = _container.Resolve<IProgressService>();
            var ui = _container.Resolve<UIRoot>();
            Button ButtonNamed(Component screen, string name) =>
                screen.GetComponentsInChildren<Button>(true).Single(button => button.name == name);

            try
            {
                await _flow.StartLevel(levelId, new LevelLaunchOptions(startIndex: 0));
                Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
                _flow.PauseGameplay();
                ButtonNamed(ui.Pause, "CheatsButton").onClick.Invoke();
                Assert.IsTrue(ui.Cheats.IsVisible);
                Assert.IsFalse(ui.Pause.IsVisible);

                var content = _container.Resolve<SharedDefinitionsService>().Content;
                var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
                var stock = level.Resolve<Purge.Gameplay.Weapons.AmmoStock>();
                var ammo = content.Items.OfType<AmmoDefinition>().ToArray();
                Assert.IsNotEmpty(ammo);
                var before = ammo.Select(stock.Get).ToArray();
                ButtonNamed(ui.Cheats, "AddAmmoButton").onClick.Invoke();
                CollectionAssert.AreEqual(before.Select(count => count + 100), ammo.Select(stock.Get));

                var weapons = content.Items.OfType<WeaponDefinition>().ToArray();
                Assert.AreEqual(weapons.Length, ui.Cheats.GetComponentsInChildren<Button>()
                    .Count(button => button.name.StartsWith("Level")));
                var pickups = level.Resolve<PickupSystem>();
                ButtonNamed(ui.Cheats, "Level0").onClick.Invoke();
                var drops = new System.Collections.Generic.List<Pickup>();
                pickups.GetAll(drops);
                var spawned = drops.Single(pickup => pickup.IsDropped && pickup.Weapon?.Definition.Id == weapons[0].Id);
                var origin = level.Resolve<PlayerSystem>().Cell;
                Assert.AreEqual(origin.Floor, spawned.Cell.Floor);
                Assert.LessOrEqual(Math.Abs(origin.X - spawned.Cell.X) + Math.Abs(origin.Y - spawned.Cell.Y), 1);

                ButtonNamed(ui.Cheats, "BackButton").onClick.Invoke();
                Assert.IsTrue(ui.Pause.IsVisible);
                var cleanProgress = JsonUtility.ToJson(save.Data);
                _flow.CompleteLevel();
                Assert.AreEqual(cleanProgress, JsonUtility.ToJson(save.Data));
                Assert.IsFalse(_flow.CanDoubleReward);

                await _flow.ExitToMenu();
                ButtonNamed(ui.MainMenu, "DevButton").onClick.Invoke();
                Assert.IsTrue(ui.DevLevels.IsVisible);
                var persisted = PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key, null);
                var balance = progress.Money;
                ButtonNamed(ui.DevLevels, "AddMoneyButton").onClick.Invoke();
                Assert.AreEqual(Math.Min((long)balance + 10_000, int.MaxValue), progress.Money);
                Assert.AreEqual(persisted, PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key, null));
            }
            finally
            {
                if (savedJson == null) PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
                else PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, savedJson);
                PlayerPrefs.Save();
            }
        });

        [UnityTest]
        public IEnumerator Arsenal_AllClasses_PreviewReleasesHandles() => Async(async () =>
        {
            var arsenal = _container.Resolve<Purge.Presentation.UI.ArsenalPresenter>();
            var ui = _container.Resolve<Purge.Presentation.UI.UIRoot>();
            var baseline = AppHandles;
            arsenal.Open();
            for (int i=0;i<9;i++)
            {
                await WaitFor(() => !arsenal.IsLoading);
                Assert.IsTrue(ui.Arsenal.IsVisible);
                var next = System.Array.Find(ui.Arsenal.GetComponentsInChildren<UnityEngine.UI.Button>(true), b => b.name == "Next");
                Assert.IsNotNull(next); next.onClick.Invoke();
            }
            arsenal.Close();
            await WaitFor(() => AppHandles == baseline);
            Assert.IsFalse(ui.Arsenal.IsVisible);
        });

        [Test]
        public void Bootstrap_ReachesMainMenu_WithLevelsInCatalog()
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            Assert.Greater(catalog.Levels.Count, 0, "Run Build / Sync for at least one level.");
            var devCatalog = catalog.DevLevels.Count > 0 ? 1 : 0; // Editor and development builds.
            Assert.AreEqual(13 + devCatalog, AppHandles,
                "Only application-wide assets are resident in the menu: level catalog (+ development levels catalog), " +
                "player definition, player visual, combat visual, weapon visuals, item visuals, card deck, content catalog, audio catalog, language catalog, " +
                "texts of one language, the menu podium's character and its weapon (music is counted apart).");
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
            var expectedHeld = level.Weapons.Select(w => w.Definition.Id).Append(baseWeapon.Id);
            if (Debug.isDebugBuild)
                expectedHeld = expectedHeld.Concat(_container.Resolve<SharedDefinitionsService>().Content.Items
                    .OfType<WeaponDefinition>().Select(w => w.Id));
            CollectionAssert.AreEquivalent(expectedHeld.Distinct().Select(id => "Weapon " + id), hands.Select(t => t.name),
                "Level weapons, the base pistol and dev catalog weapons are warmed before play.");
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

            sandbox.NextThrowable();
            Assert.AreEqual("grenade_smoke", throwing.Slot.Definition.Id);
            await WaitFor(() => throwing.Cooldown <= 0f);
            Assert.IsTrue(throwing.TryThrow(player.Position + Vector2.up * 3f));
            var smoke = container.Resolve<Purge.Gameplay.Visibility.SmokeScreen>();
            await WaitFor(() => smoke.Clouds.Count > 0);
            Assert.AreEqual(1, smoke.Clouds.Count);
            Assert.That(smoke.Clouds[0].Seconds, Is.InRange(7.5f, 8f));
            Assert.That(smoke.Clouds[0].Radius, Is.EqualTo(3f));
            ParticleSystem smokeParticles = null;
            await WaitFor(() =>
            {
                smokeParticles = SceneManager.GetSceneByName(GameScene).GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ParticleSystem>())
                    .FirstOrDefault(particles => particles.name == "FX_Grenade_Smoke_01" && particles.particleCount > 0);
                return smokeParticles != null;
            });
            await WaitFor(() => smoke.Clouds.Count == 0 || smoke.Clouds[0].Seconds <= 0.5f);
            Assert.IsTrue(smokeParticles.gameObject.activeInHierarchy);
            Assert.Greater(smokeParticles.particleCount, 0);
            await WaitFor(() => smoke.Clouds.Count == 0 && !smokeParticles.gameObject.activeInHierarchy);

            await _flow.ExitToMenu();
            Assert.AreEqual(handlesInMenu, AppHandles, "The items' looks released with the level.");
        });

        [UnityTest]
        public IEnumerator CombatSandbox_FlashbangSequencesControl_AndMineArmsAtFeet() => Async(async () =>
        {
            await _flow.StartLevel("Level_Sandbox", new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var horde = container.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            var player = container.Resolve<PlayerSystem>();
            var throwing = container.Resolve<Purge.Gameplay.Throwing.PlayerThrowing>();
            var sandbox = container.Resolve<Purge.Gameplay.Horde.CombatSandbox>();
            var effects = container.Resolve<Purge.Gameplay.Effects.EffectSystem>();
            var statuses = container.Resolve<Purge.Gameplay.Effects.StatusSystem>();
            var targets = container.Resolve<Purge.Gameplay.Combat.CombatTargets>();
            horde.GodMode = true;
            for (var i = 0; i < 3; i++) sandbox.NextThrowable();
            Assert.That(throwing.Slot.Definition.Id, Is.EqualTo("flashbang"));
            Assert.That(throwing.Slot.Definition.Effects.Count, Is.EqualTo(3), "The flashbang loads all effects.");
            Assert.That(statuses.IsActive, Is.True, "The sandbox has a status system.");
            var at = player.Position;
            var agent = horde.SpawnAt(at, Purge.Core.Monsters.MonsterPerception.Wanderer);
            Assert.That(agent, Is.GreaterThanOrEqualTo(0));
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var visible = new List<Purge.Gameplay.Combat.CombatTarget>();
            await WaitFor(() =>
            {
                targets.Collect(horde.Positions[agent], 3f, player.Floor, visible);
                return visible.Exists(target => target.Agent == agent);
            });
            var flashPoint = horde.Positions[agent];
            Assert.That(targets.IsClear(flashPoint, flashPoint, player.Floor), Is.True, "The target stands in a cell reached by the flash.");
            effects.Apply(throwing.Slot.Definition.Effects, flashPoint, player.Floor);
            Assert.That(horde.Stunned[agent], Is.True, "Flash stuns as soon as it lands.");
            Assert.That(horde.CannotAim(agent), Is.False, "Blindness has not started during the stun.");
            await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
            var weaponEffects = container.Resolve<Purge.Presentation.Visual.Horde.WeaponEffectsView>();
            var stars = (Mesh)weaponEffects.GetType().GetField("_stunStarMesh",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(weaponEffects);
            Assert.That(stars, Is.Not.Null, "The level warms up the stun stars mesh.");
            Assert.That(stars.vertexCount, Is.GreaterThanOrEqualTo(33), "A visible stunned monster gets a trio of stars.");
            var other = horde.SpawnAt(flashPoint, Purge.Core.Monsters.MonsterPerception.Wanderer, kind: 0);
            Assert.That(other, Is.GreaterThanOrEqualTo(0));
            var stun = throwing.Slot.Definition.Effects.OfType<Purge.Core.Effects.AreaStatusEffect>()
                .First(effect => effect.Status.Stuns).Status;
            statuses.Apply(Purge.Gameplay.Combat.CombatTarget.OfAgent(other), stun, 4f);
            weaponEffects.LateTick(0f);
            var combinedStars = stars.vertexCount;
            statuses.Tick(2.01f);
            Assert.That(horde.Stunned[agent], Is.False);
            Assert.That(horde.CannotAim(agent), Is.True, "Blindness starts when the stun expires.");
            weaponEffects.LateTick(0f);
            Assert.That(stars.vertexCount, Is.EqualTo(33).And.LessThan(combinedStars),
                "Only the monster whose stun is still active has stars.");
            statuses.Tick(3f);
            Assert.That(horde.CannotAim(agent), Is.False);
            weaponEffects.LateTick(0f);
            Assert.That(stars.vertexCount, Is.Zero, "The stars disappear when the last stun expires.");

            sandbox.NextThrowable();
            Assert.That(throwing.Slot.Definition.Id, Is.EqualTo("mine"));
            var plantedAt = player.Position;
            Assert.That(throwing.TryThrow(plantedAt + Vector2.up * 4f), Is.True, "The mine can be placed.");
            Assert.That(throwing.Mines.Count, Is.EqualTo(1));
            Assert.That(throwing.Mines[0].Position, Is.EqualTo(plantedAt));
            throwing.Tick(0.49f);
            Assert.That(throwing.Mines.Count, Is.EqualTo(1));
            var health = horde.Health[agent];
            throwing.Tick(0.02f);
            Assert.That(throwing.Mines.Count, Is.Zero);
            Assert.That(horde.Health[agent], Is.LessThan(health));
            await _flow.ExitToMenu();
        });

        private static Vector2 AgentPosition(Purge.Gameplay.Horde.HordeSystem horde, int agent) => horde.Positions[agent];

        [UnityTest]
        public IEnumerator Stairs_TravelSwitchesVisibilityAndMapFloor() => Async(async () =>
        {
            const string id = "Level_Stairs";
            var catalog = _container.Resolve<ILevelCatalog>();
            Assert.That(catalog.Find(id), Is.Not.Null, "Run Purge > Dev > Build Stairs Level.");
            await _flow.StartLevel(id, new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);

            var container = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var player = container.Resolve<PlayerSystem>();
            var map = container.Resolve<MapPresenter>();
            var geometry = container.Resolve<LevelData>().Geometry;
            var visuals = container.Resolve<LevelVisualSystem>();
            Assert.AreEqual(geometry.GroundFloor, player.Floor);
            Assert.AreSame(visuals.Geometry.Mask.Texture, Shader.GetGlobalTexture(GeometryShader.VisibilityTextureId));
            var combat = container.Resolve<Purge.Gameplay.Combat.PlayerCombat>();
            var bullets = container.Resolve<Purge.Gameplay.Combat.BulletSystem>();
            var root = container.Resolve<LevelViewRoot>();
            Assert.AreEqual(0, bullets.Active.Count);
            Assert.IsTrue(combat.TryAttack(Vector2.up), "The starter pistol can fire on the ground floor of Stairs.");
            var bullet = bullets.Active.Single();
            Assert.AreEqual(player.Floor, bullet.Floor);
            var tracer = root.GetComponentsInChildren<Transform>().Single(view => view.name == "Bullet");
            Assert.That(tracer.localPosition.y, Is.InRange(player.ViewHeight + 0.1f, player.ViewHeight + 2f));
            bullets.Spawn(player.Position, Vector2.up, speed: 10f, damage: 1f, floor: player.Floor - 1);
            Assert.AreEqual(2, bullets.Active.Count);
            Assert.AreEqual(1, root.GetComponentsInChildren<Transform>().Count(view => view.name == "Bullet"),
                "A tracer on the floor below remains hidden at the same horizontal coordinates.");
            var oldView = visuals.Geometry;
            var oldTexture = map.Texture;
            var stair = geometry.Stairs.First(link => link.LowerFloor == player.Floor);
            var cell = stair.LowerLanding;
            var grid = container.Resolve<LevelGrid>();
            MovePlayerTo(player, grid, cell);
            var direction = new Vector2(stair.Second.X - stair.First.X, stair.Second.Y - stair.First.Y);
            Assert.IsFalse(player.IsOnStairs, "Walking to the landing does not start climbing.");
            var startPosition = player.Position;
            player.Displace(direction);
            Assert.IsTrue(player.IsOnStairs);
            player.Tick(0.4f);
            Assert.AreEqual(cell.Floor, player.Floor);
            Assert.That(player.ViewHeight, Is.InRange(stair.Bottom.y + 0.4f, stair.Top.y - 0.4f));
            Assert.That(Vector2.Dot(player.Position - startPosition, direction), Is.GreaterThan(0.8f));
            player.Tick(1f);
            Assert.AreEqual(stair.UpperLanding, player.Cell);
            Assert.IsFalse(oldView.Root.gameObject.activeSelf);
            Assert.IsTrue(visuals.Geometry.Root.gameObject.activeSelf);
            Assert.AreSame(visuals.Geometry.Mask.Texture, Shader.GetGlobalTexture(GeometryShader.VisibilityTextureId));
            Assert.AreNotSame(oldTexture, map.Texture);

            Assert.AreEqual(CellType.Void, grid.GetCell(stair.First.OnFloor(stair.UpperFloor)));
            Assert.AreEqual(CellType.Void, grid.GetCell(stair.Second.OnFloor(stair.UpperFloor)));
            Assert.IsFalse(HasFloorOverOpening(visuals.Geometry, stair.First.OnFloor(stair.UpperFloor)));

            var upperLanding = player.Position;
            var sideways = new Vector2(-direction.y, direction.x);
            var sideExit = PlayerMovement.CellOf(upperLanding + sideways, player.Floor);
            if (grid.Has(sideExit, CellFlags.BlocksMovement)) sideways = -sideways;
            player.Displace(sideways);
            Assert.IsFalse(player.IsOnStairs, "Walking sideways away from the landing does not start descending.");

            var horde = container.Resolve<Purge.Gameplay.Horde.HordeSystem>();
            horde.Tick(0.01f);
            var kind = Enumerable.Range(0, horde.KindList.Count).First(i =>
                horde.KindList[i].Definition?.Perception == Purge.Core.Monsters.MonsterPerception.Hunter);
            var slot = horde.SpawnEmerging(new Vector2(cell.X, cell.Y), kind, 0f, cell.Floor);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), "A monster can spawn on the other floor.");
            Assert.AreEqual(cell.Floor, horde.FloorOf(slot));
            var traversedFlight = false;
            var spawnPosition = AgentPosition(horde, slot);
            for (var i = 0; i < 150 && horde.FloorOf(slot) != player.Floor; i++)
            {
                horde.Tick(1f / 30f);
                var height = horde.ViewHeightOf(slot);
                if (height > stair.Bottom.y + 0.25f && height < stair.Top.y - 0.25f &&
                    Vector2.Distance(spawnPosition, AgentPosition(horde, slot)) > 0.5f &&
                    horde.IsVisibleOnFloor(slot, player.Floor)) traversedFlight = true;
            }
            Assert.IsTrue(traversedFlight, "The monster walks along the flight at intermediate heights.");
            Assert.AreEqual(player.Floor, horde.FloorOf(slot), "A chasing monster can climb to the player.");

            _flow.OpenMap();
            Assert.AreEqual(GameFlowState.Map, _flow.State);
            var ui = _container.Resolve<UIRoot>();
            var tab = ui.Map.transform.Find($"SafeArea/Floor{geometry.GroundFloor}").GetComponent<Button>();
            Assert.IsTrue(tab.gameObject.activeInHierarchy);
            tab.onClick.Invoke();
            Assert.IsFalse(map.Icons[map.Icons.Count - 1].Visible, "The player is not on the map's selected floor.");
            Assert.AreEqual(stair.UpperFloor, player.Floor);
            _flow.CloseMap();
            MovePlayerTo(player, grid, stair.UpperLanding);
            player.Displace(-direction);
            Assert.IsTrue(player.IsOnStairs);
            player.Tick(0.4f);
            Assert.That(player.ViewHeight, Is.InRange(stair.Bottom.y + 0.4f, stair.Top.y - 0.4f));
            Assert.That(Vector2.Dot(upperLanding - player.Position, direction), Is.GreaterThan(0.8f));
            player.Tick(1f);
            Assert.AreEqual(stair.LowerLanding, player.Cell);
            Assert.AreSame(oldView.Mask.Texture, Shader.GetGlobalTexture(GeometryShader.VisibilityTextureId));
            await _flow.ExitToMenu();
        });

        private static void MovePlayerTo(PlayerSystem player, LevelGrid grid, GridPosition destination)
        {
            var mask = new bool[grid.CellCount];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = !grid.Has(grid.ToPosition(i), CellFlags.BlocksMovement);
            var path = new List<GridPosition>();
            var pathfinder = new GridPathfinder(grid.Width, grid.Height);
            Assert.IsTrue(pathfinder.TryFindPath(player.Cell, destination,
                new CellMaskPassability(grid.Width, grid.Height, mask), path));
            foreach (var cell in path.Skip(1))
                player.Displace(new Vector2(cell.X, cell.Y) - player.Position);
            Assert.AreEqual(destination, player.Cell);
        }

        private static bool HasFloorOverOpening(LevelGeometryView view, GridPosition opening)
        {
            var height = opening.ToWorld().y;
            foreach (var chunk in view.Chunks)
            {
                var mesh = chunk.Mesh;
                var vertices = mesh.vertices;
                var triangles = mesh.triangles;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]];
                    var b = vertices[triangles[i + 1]];
                    var c = vertices[triangles[i + 2]];
                    if (Mathf.Abs(a.y - height) > 0.04f || Mathf.Abs(b.y - height) > 0.04f ||
                        Mathf.Abs(c.y - height) > 0.04f) continue;
                    var center = (a + b + c) / 3f;
                    if (Mathf.Abs(center.x - opening.X) < 0.45f && Mathf.Abs(center.z - opening.Y) < 0.45f)
                        return true;
                }
            }

            return false;
        }

        [UnityTest]
        public IEnumerator Map_Icons_AndFog_UpdateOnOpen() => Async(async () =>
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

                // Fog (stage 7g): explored cells are drawn, the rest is black.
                var exploration = container.Resolve<Purge.Gameplay.Visibility.MapExploration>();
                var visibility = container.Resolve<Purge.Gameplay.Visibility.VisibilitySystem>();
                Assert.IsTrue(exploration.IsExplored(container.Resolve<PlayerSystem>().Cell), "The player's cell is explored.");
                Assert.Less(exploration.Count, grid.CellCount, "Not all is explored at the start.");
                presenter.ApplyPending();
                for (var i = 0; i < grid.CellCount; i++)
                {
                    var cell = grid.ToPosition(i);
                    var expected = exploration.IsExplored(cell) ? MapRenderer.ColorOf(grid, cell) : MapRenderer.Unknown;
                    Assert.AreEqual(expected, (Color32)presenter.Texture.GetPixel(cell.X, cell.Y), $"Map pixel {cell}.");
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
                Assert.AreEqual(level.Doors.Count(door => exploration.IsExplored(door.Position)),
                    icons.Count(icon => (icon.Kind == MapIconKind.Door || icon.Kind == MapIconKind.LockedDoor) && icon.Visible),
                    "Doors in explored cells.");

                Assert.AreEqual(level.Keys.Count(k => exploration.IsExplored(k.Position)),
                    icons.Count(icon => icon.Kind == MapIconKind.Key && icon.Visible), "Items in explored cells.");
                Assert.AreEqual(level.Medkits.Count(m => exploration.IsExplored(m.Position)),
                    icons.Count(icon => icon.Kind == MapIconKind.Medkit && icon.Visible));
                Assert.AreEqual(level.Weapons.Count(w => exploration.IsExplored(w.Position)),
                    icons.Count(icon => icon.Kind == MapIconKind.Weapon && icon.Visible));

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
        public IEnumerator Castle_LoadsFromCatalog_WithItsOwnLevelContent() => Async(async () =>
        {
            await _flow.StartLevel("Level_Castle_01");
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);

            var scope = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene));
            var level = scope.Container.Resolve<LevelRuntime>().Level;
            var geometry = scope.Container.Resolve<LevelVisualSystem>().Geometry;
            Assert.AreEqual(level.Geometry.GroundFloor, geometry.Mask.Floor);
            Assert.AreSame(geometry.Mask.Texture, Shader.GetGlobalTexture(GeometryShader.VisibilityTextureId));
            Assert.AreEqual(LevelLocation.Castle, level.Settings.Location);
            Assert.AreEqual(FloorStepSound.Stone, level.VisualTheme.FloorSteps);
            Assert.AreEqual(32, level.Geometry.Width);
            Assert.AreEqual(26, level.Geometry.Height);
            Assert.AreEqual(3, level.Geometry.FloorCount);
            Assert.IsFalse(level.Arena.IsEmpty);
            Assert.AreEqual(16, level.Arena.Rect.Width);
            Assert.AreEqual(16, level.Arena.Rect.Height);
            Assert.IsNotEmpty(level.Geometry.Stairs);
            Assert.IsNotEmpty(level.Doors.Where(door => door.RequiresKey));
            Assert.IsNotEmpty(level.Keys);
            Assert.That(level.Barrels.Count, Is.InRange(6, 10));
            Assert.IsTrue(level.Barrels.All(barrel => barrel.Kind == BarrelKind.Fire));
            Assert.IsEmpty(level.Vehicles);
        });

        [UnityTest]
        public IEnumerator Castle_ExplosionEffects_UseTheSourceFloor() => Async(async () =>
        {
            await _flow.StartLevel("Level_Castle_01", new LevelLaunchOptions(startIndex: 0));
            Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
            var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
            var player = level.Resolve<PlayerSystem>();
            Assert.Greater(player.Floor, 0);
            var barrels = level.Resolve<Purge.Gameplay.Objects.BarrelSystem>();
            var effects = level.Resolve<Purge.Gameplay.Effects.EffectSystem>();
            var bullets = level.Resolve<Purge.Gameplay.Combat.BulletSystem>();
            var pack = level.Resolve<PackEffectsPlayer>();
            var before = pack.ActiveEffects;

            var barrelEffects = barrels.DefinitionOf(BarrelKind.Fire).Effects;
            effects.Apply(barrelEffects, player.Position, player.Floor - 1);
            Assert.AreEqual(before, pack.ActiveEffects, "A blast on the floor below is hidden.");

            effects.Apply(barrelEffects, player.Position, player.Floor);
            Assert.AreEqual(before + 1, pack.ActiveEffects, "A fire barrel uses the shared explosion effect on this floor.");

            bullets.Explode(player.Position, new ExplosiveAttack(), player.Floor);
            Assert.AreEqual(before + 2, pack.ActiveEffects, "An explosive projectile uses the same floor-aware presentation.");

            var root = level.Resolve<LevelViewRoot>().transform.Find("Pack Effects");
            var height = player.Floor * GridPosition.FloorHeight;
            Assert.That(root.Cast<Transform>().Count(child => child.gameObject.activeSelf &&
                Mathf.Approximately(child.localPosition.y, height)), Is.GreaterThanOrEqualTo(2));
        });

        [UnityTest]
        public IEnumerator Castle_StartsBossWaves_OnTheirConfiguredDifficulties() => Async(async () =>
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            Assert.AreEqual(4, catalog.Difficulties.Count);
            foreach (var difficulty in catalog.Difficulties)
            {
                await _flow.StartLevel("Level_Castle_01", new LevelLaunchOptions(startIndex: 0, seed: 13, difficulty: difficulty));
                Assert.AreEqual(GameFlowState.Playing, _flow.State, _flow.ErrorMessage);
                var level = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
                var waves = level.Resolve<Purge.Gameplay.Waves.WaveSystem>();
                Assert.AreEqual(16, waves.Scenario.Count);
                var intermediate = difficulty.Level >= Purge.Core.Monsters.DifficultyLevel.Normal;
                foreach (var entry in new[] { (Index: 4, Id: "zombie_boss_common"), (Index: 8, Id: "demon_common"), (Index: 15, Id: "gargoyle_castle") })
                {
                    var enabled = entry.Index == 15 || intermediate;
                    var wave = waves.Scenario.Waves[entry.Index];
                    Assert.AreEqual(entry.Id, wave.Boss.Monster.Id);
                    Assert.AreEqual(enabled, wave.Boss.IsEnabled(difficulty.Level));
                }

                waves.ResumeAt(15);
                Assert.IsTrue(waves.StartNextWave());
                waves.Tick(0.02f);
                var bosses = level.Resolve<Purge.Gameplay.Horde.BossSystem>();
                Assert.IsTrue(bosses.IsBossAlive);
                Assert.AreEqual("gargoyle_castle", bosses.Definition.Id);
                await _flow.ExitToMenu();
            }
        });

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
        /// The tutorial from the development level list (stage 11a; its menu button returns in stage 12): the first step's window pauses the level, Continue resumes; the reminder,
        /// the arrow shows the step; the map opened counts for the map step.
        /// </summary>
        [UnityTest]
        public IEnumerator Tutorial_WindowPausesTheLevel_ContinueResumes() => Async(async () =>
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            if (catalog.Tutorial == null) Assert.Ignore("No tutorial level: Build / Sync the level with a tutorial script.");
            var ui = _container.Resolve<UIRoot>();
            Assert.IsTrue(ui.DevLevels.transform.GetComponentsInChildren<Button>(true)
                .Any(b => b.name == "Level0"), "The development list offers the tutorial first.");

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

        [UnityTest]
        public IEnumerator Tutorial_ReplayFromSettings_CompletesTwoWaves_AndPaysOnlyOnce() => Async(async () =>
        {
            var catalog = _container.Resolve<ILevelCatalog>();
            Assert.IsNotNull(catalog.Tutorial);
            var save = _container.Resolve<SaveService>();
            var ui = _container.Resolve<UIRoot>();
            var savedJson = PlayerPrefs.HasKey(PlayerPrefsSaveStorage.Key)
                ? PlayerPrefs.GetString(PlayerPrefsSaveStorage.Key)
                : null;

            try
            {
                save.Data.Levels.RemoveAll(record => record.LevelId == catalog.Tutorial.LevelId);
                var before = save.Data.Money;
                var replay = ui.Settings.GetComponentsInChildren<Button>(true).Single(b => b.name == "ReplayTutorial");

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    ui.MainMenu.GetComponentsInChildren<Button>(true).Single(b => b.name == "SettingsButton").onClick.Invoke();
                    replay.onClick.Invoke();
                    await WaitFor(() => _flow.IsTutorialLevel && _flow.State == GameFlowState.Tip);
                    var scope = FindRoot<LevelLifetimeScope>(SceneManager.GetSceneByName(GameScene)).Container;
                    var tutorial = scope.Resolve<Purge.Gameplay.Tutorial.TutorialSystem>();
                    var waves = scope.Resolve<Purge.Gameplay.Waves.WaveSystem>();
                    var horde = scope.Resolve<Purge.Gameplay.Horde.HordeSystem>();
                    var player = scope.Resolve<PlayerSystem>();
                    var health = scope.Resolve<PlayerHealth>();
                    health.Damage(int.MaxValue, Vector2.right);
                    Assert.AreEqual(1, health.Current);
                    Assert.IsFalse(waves.StartNextWave());

                    _flow.CloseTip();
                    player.EnterVehicle(new Vector2(15, 7), Vector2.up);
                    player.ExitVehicle(new Vector2(15, 7));
                    Assert.IsTrue(scope.Resolve<Purge.Gameplay.Combat.PlayerCombat>().TryAttack(Vector2.up));
                    var pickup = scope.Resolve<LevelData>().Weapons[0];
                    Assert.IsTrue(scope.Resolve<PickupSystem>().TryTakeWeapon(pickup.Position));
                    var weapons = scope.Resolve<Purge.Gameplay.Weapons.WeaponSystem>();
                    var pickupSlot = weapons.ActiveSlot;
                    Assert.IsTrue(weapons.Activate(pickupSlot == WeaponSlot.First ? WeaponSlot.Second : WeaponSlot.First));
                    Assert.IsNotNull(weapons.Drop(pickupSlot));
                    Assert.IsTrue(scope.Resolve<Purge.Gameplay.Throwing.PlayerThrowing>().TryThrow(new Vector2(15, 13)));

                    await WaitFor(() => _flow.State == GameFlowState.Tip);
                    _flow.CloseTip();
                    await WaitFor(() => waves.Phase == Purge.Gameplay.Waves.WavePhase.Running && waves.PendingCount == 0);
                    DefeatAgents(horde);
                    await WaitFor(() => _flow.State == GameFlowState.Cards || _flow.State == GameFlowState.Tip);
                    if (_flow.State == GameFlowState.Tip) _flow.CloseTip();
                    await WaitFor(() => _flow.State == GameFlowState.Cards);
                    var cards = scope.Resolve<Purge.Gameplay.Run.CardSystem>();
                    Assert.IsTrue(cards.HasOffer);
                    cards.Choose(0);
                    _flow.CloseCards();

                    await WaitFor(() => _flow.State == GameFlowState.Tip || waves.PauseElapsed > 1f);
                    if (_flow.State == GameFlowState.Tip) _flow.CloseTip();
                    Assert.IsTrue(waves.Skip());
                    await WaitFor(() => waves.RemainingCount > 0 && waves.PendingCount == 0);
                    if (_flow.State == GameFlowState.Tip) _flow.CloseTip();
                    DefeatAgents(horde);
                    await WaitFor(() => _flow.State == GameFlowState.Completed || _flow.State == GameFlowState.Tip);
                    if (_flow.State == GameFlowState.Tip) _flow.CloseTip();
                    await WaitFor(() => _flow.State == GameFlowState.Completed);

                    Assert.IsTrue(tutorial.IsFinished);
                    var price = _container.Resolve<SharedDefinitionsService>().Content.Find<WeaponDefinition>("desert").PurchasePrice;
                    Assert.AreEqual(attempt == 0 ? price : 0, _flow.LastResult.Value.Money);
                    Assert.AreEqual(before + price, save.Data.Money);
                    Assert.IsFalse(_flow.CanDoubleReward);
                    await _flow.ExitToMenu();
                }
            }
            finally
            {
                if (savedJson != null) PlayerPrefs.SetString(PlayerPrefsSaveStorage.Key, savedJson);
                else PlayerPrefs.DeleteKey(PlayerPrefsSaveStorage.Key);
                PlayerPrefs.Save();
                save.Load();
            }
        });

        private static void DefeatAgents(Purge.Gameplay.Horde.HordeSystem horde)
        {
            for (var agent = 0; agent < horde.HighWater; agent++)
                if (Purge.Gameplay.Horde.HordeSystem.IsAlive(horde.States[agent])) horde.Damage(agent, 100000f);
        }

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

