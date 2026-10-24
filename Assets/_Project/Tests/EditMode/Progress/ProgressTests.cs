using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Purge.Application.Levels;
using Purge.Application.Save;
using Purge.Application.Services;
using Purge.Core.Grid;
using Purge.Core.Level;
using Purge.Gameplay.Combat;
using Purge.Gameplay.Level;
using Purge.Gameplay.Run;
using Purge.Presentation.Map;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Purge.Tests.EditMode.Progress
{
    /// <summary>SaveData, progress across levels, stars, settings and the map image (ТЗ §58–60, §85–87).</summary>
    public class ProgressTests
    {
        private LevelCatalog _catalogAsset;
        private MemoryStorage _storage;
        private SaveService _save;
        private ProgressService _progress;

        [SetUp]
        public void SetUp()
        {
            _catalogAsset = ScriptableObject.CreateInstance<LevelCatalog>();
            _catalogAsset.AddOrUpdate("first", "Levels/first", "First");
            _catalogAsset.AddOrUpdate("second", "Levels/second", "Second");
            _catalogAsset.AddOrUpdate("third", "Levels/third", "Third");

            _storage = new MemoryStorage();
            _save = new SaveService(_storage);
            _save.Load();
            _progress = new ProgressService(_save, new Catalog(_catalogAsset));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_catalogAsset);

        private static LevelResult Completed(int kills = 0, int money = 0) => new LevelResult(true, kills, money: money);

        // ------------------------------------------------------------ Money (stage 6d)

        [Test]
        public void RunMoney_IsKeptOnDeathAndWin_AddMoneySaves()
        {
            _progress.RecordRun("first", new LevelResult(false, 3, money: 10));
            Assert.AreEqual(10, _progress.Money, "ТЗ: при смерти собранное остаётся.");
            Assert.IsFalse(_progress.IsCompleted("first"), "A death completes nothing.");
            Assert.AreEqual(3, _progress.TotalKills);
            Assert.AreEqual(0, _save.Data.Stats.RunsWon);

            _progress.RecordRun("first", Completed(kills: 1, money: 25));
            Assert.AreEqual(35, _progress.Money);
            Assert.AreEqual(1, _save.Data.Stats.RunsWon);

            var writes = _storage.Writes;
            _progress.AddMoney(25);
            _progress.AddMoney(0);
            Assert.AreEqual(60, _progress.Money);
            Assert.AreEqual(writes + 1, _storage.Writes, "Saved at once; nothing to add — no write.");
        }

        // ------------------------------------------------------------ Progress

        [Test]
        public void LevelAddedAfterTheLastCompleted_IsUnlocked()
        {
            _catalogAsset.Remove("third");
            _progress.RecordRun("first", Completed());
            _progress.RecordRun("second", Completed());
            Assert.IsNull(_save.Data.FindLevel("third"), "Not in the catalog when 'second' was completed.");

            _catalogAsset.AddOrUpdate("third", "Levels/third", "Third"); // An update brings a new level.
            Assert.IsTrue(_progress.IsUnlocked("third"), "The previous level is completed: the new one is open.");
            Assert.IsFalse(_progress.IsUnlocked("unknown"));
        }

        [Test]
        public void OnlyFirstLevelIsUnlocked_CompletionUnlocksTheNext()
        {
            Assert.IsTrue(_progress.IsUnlocked("first"));
            Assert.IsFalse(_progress.IsUnlocked("second"));

            _progress.RecordRun("first", Completed());

            Assert.IsTrue(_progress.IsUnlocked("second"));
            Assert.IsFalse(_progress.IsUnlocked("third"));
        }

        [Test]
        public void KillsAreSummed()
        {
            _progress.RecordRun("first", Completed(kills: 2));
            _progress.RecordRun("first", Completed(kills: 1));

            Assert.AreEqual(3, _progress.TotalKills);
            Assert.IsFalse(_progress.IsCompleted("second"));
        }

        [Test]
        public void Progress_SurvivesRestart()
        {
            _progress.RecordRun("first", Completed(kills: 1, money: 9));
            Assert.AreEqual(1, _storage.Writes, "Saved right after completion.");

            var save = new SaveService(_storage);
            save.Load();
            var progress = new ProgressService(save, new Catalog(_catalogAsset));

            Assert.IsTrue(progress.IsCompleted("first"));
            Assert.AreEqual(9, progress.Money);
            Assert.AreEqual(1, progress.TotalKills);
            Assert.IsTrue(progress.IsUnlocked("second"));
        }

        [Test]
        public void ResetProgress_ForgetsEverything()
        {
            _progress.RecordRun("first", Completed(kills: 4));
            _progress.ResetProgress();

            Assert.IsFalse(_progress.IsCompleted("first"));
            Assert.AreEqual(0, _progress.TotalKills);
            Assert.IsFalse(_progress.IsUnlocked("second"));
            Assert.IsTrue(_progress.IsUnlocked("first"));
        }

        // ------------------------------------------------------------ Save

        [Test]
        public void CorruptedSave_StartsFresh()
        {
            _storage.Json = "{ not json";
            LogAssert.Expect(LogType.Warning, new Regex("corrupted"));

            _save.Load();

            Assert.IsNotNull(_save.Data.Levels);
            Assert.AreEqual(0, _save.Data.Stats.TotalKills);
        }

        [Test]
        public void MissingFields_AreFilledIn()
        {
            _storage.Json = "{\"SaveVersion\":1,\"Money\":5}";
            _save.Load();

            Assert.AreEqual(5, _save.Data.Money);
            Assert.IsNotNull(_save.Data.Levels);
            Assert.IsNotNull(_save.Data.Arsenal);
            Assert.IsNotNull(_save.Data.Stats);
            Assert.IsNotNull(_save.Data.Run);
            Assert.IsFalse(_save.Data.HasRun);
            Assert.IsNotNull(_save.Data.Settings);
        }

        [Test]
        public void Settings_AreStoredInSave_AndSavedOnChange()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);

            settings.MusicVolume = 0.25f;
            settings.MusicVolume = 0.25f;
            settings.SfxVolume = 0.5f;
            settings.SfxVolume = 7f;

            Assert.AreEqual(3, _storage.Writes, "Saved once per real change.");
            var save = new SaveService(_storage);
            save.Load();
            Assert.AreEqual(0.25f, save.Data.Settings.MusicVolume);
            Assert.AreEqual(1f, save.Data.Settings.SfxVolume, "Clamped.");
        }

        [Test]
        public void ShowFps_OffByDefault_SavedAtOnce_AndRaisesItsEvent()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            Assert.IsFalse(settings.ShowFps, "Off by default (old saves have no such field).");
            var changes = 0;
            settings.ShowFpsChanged += () => changes++;
            var writes = _storage.Writes;

            settings.ShowFps = true;
            settings.ShowFps = true;

            Assert.AreEqual(1, changes, "Once per real change.");
            Assert.AreEqual(writes + 1, _storage.Writes, "Saved at once.");
            var save = new SaveService(_storage);
            save.Load();
            Assert.IsTrue(save.Data.Settings.ShowFps);
        }

        [Test]
        public void Volumes_PreviewAppliesAtOnce_SaveWritesOnce_ResetOtherRestoresDefaults()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var changes = 0;
            settings.VolumeChanged += () => changes++;
            var writes = _storage.Writes;

            settings.PreviewVolumes(0.3f, 0.6f);
            settings.PreviewVolumes(0.3f, 0.6f);
            settings.PreviewVolumes(0.3f, 5f);

            Assert.AreEqual(2, changes, "Once per real change.");
            Assert.AreEqual(0.3f, settings.MusicVolume, 1e-5f);
            Assert.AreEqual(1f, settings.SfxVolume, "Clamped.");
            Assert.AreEqual(writes, _storage.Writes, "Preview does not write.");

            settings.SavePreviewed();
            settings.SavePreviewed();
            Assert.AreEqual(writes + 1, _storage.Writes);

            settings.ShowFps = true;
            settings.PreviewVolumes(0.1f, 0.2f);
            settings.ResetOther();
            Assert.AreEqual(1f, settings.MusicVolume);
            Assert.AreEqual(1f, settings.SfxVolume);
            Assert.IsFalse(settings.ShowFps);
            var save = new SaveService(_storage);
            save.Load();
            Assert.AreEqual(1f, save.Data.Settings.MusicVolume, "Reset is saved.");
            Assert.IsFalse(save.Data.Settings.ShowFps);
        }

        [Test]
        public void ControlsSettings_PreviewAppliesAtOnce_SaveWritesOnce_Clamped()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var changes = 0;
            settings.ControlsChanged += () => changes++;
            var writes = _storage.Writes;

            var controls = settings.Controls;
            controls.StickDeadZone = 0.3f;
            settings.PreviewControls(controls);
            controls.Size = 5f;
            settings.PreviewControls(controls);
            settings.PreviewControls(controls);

            Assert.AreEqual(2, changes, "Once per real change.");
            Assert.AreEqual(ControlsSettings.MaxSize, settings.Controls.Size, "Clamped.");
            Assert.AreEqual(writes, _storage.Writes, "Preview does not write.");

            settings.SavePreviewed();
            settings.SavePreviewed();
            Assert.AreEqual(writes + 1, _storage.Writes);

            var save = new SaveService(_storage);
            save.Load();
            Assert.AreEqual(0.3f, save.Data.Settings.Controls.StickDeadZone, 1e-5f);
            Assert.AreEqual(ControlsSettings.MaxSize, save.Data.Settings.Controls.Size, 1e-5f);

            settings.ResetControls();
            Assert.AreEqual(ControlsSettings.Default, settings.Controls);
            Assert.AreEqual(writes + 2, _storage.Writes);
        }

        [Test]
        public void GameSettings_OldSaveZeros_ReadAsDefaults_PreviewSaved_ResetRestores()
        {
            // Stage 6e2: a save without the "Game" page (zeros) gets the defaults; vibration on.
            _save.Data.Settings.Game = default;
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            Assert.AreEqual(GameSettings.Default, settings.Game);
            Assert.IsTrue(settings.Game.CameraShake);
            Assert.IsTrue(settings.Controls.Vibration);

            var changes = 0;
            settings.GameChanged += () => changes++;
            var writes = _storage.Writes;
            var game = settings.Game;
            game.CameraDistance = 20;
            game.ResolutionScale = 0.62f;
            game.NoCameraShake = true;
            game.Compare = PickupCompare.Color;
            settings.PreviewGame(game);
            settings.PreviewGame(game);
            Assert.AreEqual(1, changes, "Once per real change.");
            Assert.AreEqual(GameSettings.MaxCameraDistance, settings.Game.CameraDistance, "Clamped.");
            Assert.AreEqual(0.6f, settings.Game.ResolutionScale, 1e-4f, "Whole steps of 0.05.");
            Assert.AreEqual(writes, _storage.Writes, "Preview does not write.");

            settings.SavePreviewed();
            var save = new SaveService(_storage);
            save.Load();
            Assert.AreEqual(PickupCompare.Color, save.Data.Settings.Game.Compare);
            Assert.IsFalse(save.Data.Settings.Game.CameraShake);

            settings.ResetGame();
            Assert.AreEqual(GameSettings.Default, settings.Game);
        }

        [Test]
        public void Vibration_OffSaved_ControlsResetTurnsItOn()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var controls = settings.Controls;
            controls.NoVibration = true;
            settings.SetControls(controls);

            var save = new SaveService(_storage);
            save.Load();
            Assert.IsFalse(save.Data.Settings.Controls.Vibration);

            settings.ResetTouchControls();
            Assert.IsTrue(settings.Controls.Vibration);
        }

        [Test]
        public void ControlsScheme_TwoSticksByDefault_Saved_BrokenValueClamped()
        {
            Assert.AreEqual(TouchScheme.TwoSticks, ControlsSettings.Default.Scheme);
            Assert.AreEqual(TouchScheme.TwoSticks, default(ControlsSettings).Scheme, "Old saves (zero): two sticks.");

            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var controls = settings.Controls;
            controls.Scheme = TouchScheme.Auto;
            settings.SetControls(controls);
            var save = new SaveService(_storage);
            save.Load();
            Assert.AreEqual(TouchScheme.Auto, save.Data.Settings.Controls.Scheme);

            controls.Scheme = (TouchScheme)7;
            Assert.AreEqual(TouchScheme.TwoSticks, controls.Clamped().Scheme);
        }

        [Test]
        public void TouchLayout_IsSaved_Clamped_AndOldSavesGetBuiltInPlaces()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var controls = settings.Controls;
            var layout = controls.Layout;
            layout[TouchElement.Attack] = new TouchPlacement { Moved = true, Position = new Vector2(0.7f, 1.5f), Scale = 9f };
            controls.Layout = layout;
            settings.SetControls(controls);

            var save = new SaveService(_storage);
            save.Load();
            var attack = save.Data.Settings.Controls.Layout.Attack;
            Assert.IsTrue(attack.Moved);
            Assert.AreEqual(new Vector2(0.7f, 1f), attack.Position, "Clamped to the safe area.");
            Assert.AreEqual(TouchPlacement.MaxScale, attack.Scale, 1e-5f);
            Assert.IsFalse(save.Data.Settings.Controls.Layout.Use.Moved);

            // A save from before the layout existed: no Layout field.
            _storage.Json = "{\"Version\":2,\"Settings\":{\"Controls\":{\"StickDeadZone\":0.1,\"Size\":1.2,\"Opacity\":0.5}}}";
            _save.Load();
            var old = new SettingsService(_save);
            old.InitializeAsync(CancellationToken.None);
            Assert.AreEqual(TouchLayout.Default, old.Controls.Layout, "Built-in places, scale 1.");
            Assert.AreEqual(1.2f, old.Controls.Size, 1e-5f);
        }

        [Test]
        public void MazeFormatSave_KeepsSettings_DropsProgress()
        {
            const string moved = "{\"Moved\":true,\"Position\":{\"x\":0.3,\"y\":0.4},\"Scale\":1.2}";
            _storage.Json = "{\"Version\":4,\"UnlockedLevels\":[\"first\",\"second\"],\"TotalKills\":40," +
                            "\"Settings\":{\"MusicVolume\":0.25,\"Controls\":{\"Size\":1,\"Opacity\":0.6,\"Layout\":{" +
                            $"\"Stick\":{moved}" + "}}}}";
            _save.Load();

            Assert.AreEqual(SaveData.CurrentVersion, _save.Data.SaveVersion);
            Assert.AreEqual(0.25f, _save.Data.Settings.MusicVolume, 1e-5f, "Settings are kept.");
            Assert.IsTrue(_save.Data.Settings.Controls.Layout.Stick.Moved);
            Assert.IsEmpty(_save.Data.Levels, "Maze progress has no place in the new format.");
            Assert.AreEqual(0, _save.Data.Stats.TotalKills);
            Assert.IsFalse(_progress.IsUnlocked("second"));
        }

        [Test]
        public void Normalize_DropsBrokenAndRepeatedEntries_ClampsNumbers()
        {
            var data = new SaveData { Money = -5 };
            data.Levels.Add(new LevelRecord { LevelId = "a", CompletedMask = 1 });
            data.Levels.Add(new LevelRecord { LevelId = "a", CompletedMask = 2 });
            data.Levels.Add(null);
            data.Levels.Add(new LevelRecord { LevelId = "" });
            data.Arsenal.Add(new OwnedItem { Id = "ak_74", Level = 0, Mark = (PoolMark)9 });
            data.Arsenal.Add(new OwnedItem { Id = "ak_74", Level = 4 });
            data.CharacterNodes.Add("n");
            data.CharacterNodes.Add("n");
            data.CharacterNodes.Add(null);
            data.Run.Active = true; // a run of no level
            data.Run.Cards.Add("card");

            data.Normalize();

            Assert.AreEqual(0, data.Money);
            Assert.AreEqual(1, data.Levels.Count);
            Assert.AreEqual(1, data.Levels[0].CompletedMask, "The first record wins.");
            Assert.AreEqual(1, data.Arsenal.Count);
            Assert.AreEqual(1, data.Arsenal[0].Level);
            Assert.AreEqual(PoolMark.Normal, data.Arsenal[0].Mark);
            CollectionAssert.AreEqual(new[] { "n" }, data.CharacterNodes);
            Assert.IsFalse(data.HasRun);
            Assert.IsEmpty(data.Run.Cards);
        }

        [Test]
        public void RunCheckpoint_SavedAndLoaded_AbandonKeepsMoney()
        {
            var run = new RunSnapshot
            {
                Seed = 77, Wave = 2, WaveInProgress = true, Money = 31, Score = 900, Kills = 40, Experience = 6, Level = 4,
                Health = 55, SkillReviveUsed = true, Time = 123.5f, ActiveSlot = 1, ThrowableId = "grenade", ThrowableCount = 4,
            };
            run.Cards.Add("card_a");
            run.Weapons[1] = "ak_74";
            run.Ammo[1] = 9;
            _progress.SaveRun("first", 0, run);

            _save.Load();
            Assert.IsTrue(_progress.HasRun);
            Assert.AreEqual("first", _progress.RunLevelId);
            Assert.AreEqual(3, _save.Data.Run.Wave, "the save counts waves from 1");
            var loaded = _progress.LoadRun();
            Assert.AreEqual(77, loaded.Seed);
            Assert.AreEqual(2, loaded.Wave);
            Assert.IsTrue(loaded.WaveInProgress);
            Assert.AreEqual(2, loaded.NextWave);
            Assert.AreEqual(2, loaded.WavesCleared);
            Assert.AreEqual(31, loaded.Money);
            Assert.AreEqual(900, loaded.Score);
            Assert.AreEqual(40, loaded.Kills);
            Assert.AreEqual(4, loaded.Level);
            Assert.AreEqual(6, loaded.Experience);
            Assert.AreEqual(55, loaded.Health);
            Assert.IsTrue(loaded.SkillReviveUsed);
            Assert.IsFalse(loaded.AdReviveUsed);
            Assert.AreEqual(123.5f, loaded.Time);
            Assert.AreEqual(1, loaded.ActiveSlot);
            CollectionAssert.AreEqual(new[] { "card_a" }, loaded.Cards);
            Assert.IsTrue(string.IsNullOrEmpty(loaded.Weapons[0]));
            Assert.AreEqual("ak_74", loaded.Weapons[1]);
            Assert.AreEqual(9, loaded.Ammo[1]);
            Assert.AreEqual("grenade", loaded.ThrowableId);
            Assert.AreEqual(4, loaded.ThrowableCount);

            var money = _progress.Money;
            _progress.AbandonRun(loaded.Money);
            Assert.AreEqual(money + 31, _progress.Money);
            Assert.IsFalse(_progress.HasRun);
            Assert.IsNull(_progress.LoadRun());
        }

                [Test]
        public void RunSave_SurvivesTheRoundTrip_AndClears()
        {
            _save.Data.Run.Active = true;
            _save.Data.Run.Mode = RunMode.Level;
            _save.Data.Run.LevelId = "first";
            _save.Data.Run.Difficulty = 2;
            _save.Data.Run.Wave = 7;
            _save.Data.Run.WaveInProgress = true;
            _save.Data.Run.Score = 123456789012L;
            _save.Data.Run.Cards.Add("card_a");
            _save.Data.Run.Weapons.Add(new RunWeapon { Id = "ak_74", Ammo = 12 });
            _save.Data.Run.Weapons.Add(new RunWeapon());
            _save.Data.Run.ThrowableId = "grenade";
            _save.Data.Run.ThrowableCount = 3;
            _save.Data.Arsenal.Add(new OwnedItem { Id = "ak_74", Level = 3, Forks = 2, Mark = PoolMark.Excluded });
            _save.Save();

            _save.Load();

            var run = _save.Data.Run;
            Assert.IsTrue(_save.Data.HasRun);
            Assert.AreEqual("first", run.LevelId);
            Assert.AreEqual(2, run.Difficulty);
            Assert.AreEqual(7, run.Wave);
            Assert.IsTrue(run.WaveInProgress);
            Assert.AreEqual(123456789012L, run.Score);
            CollectionAssert.AreEqual(new[] { "card_a" }, run.Cards);
            Assert.AreEqual(2, run.Weapons.Count);
            Assert.AreEqual(12, run.Weapons[0].Ammo);
            Assert.AreEqual(3, run.ThrowableCount);
            Assert.AreEqual(PoolMark.Excluded, _save.Data.FindItem("ak_74").Mark);
            Assert.AreEqual(2, _save.Data.FindItem("ak_74").Forks);

            run.Clear();
            Assert.IsFalse(_save.Data.HasRun);
            Assert.IsEmpty(run.Weapons);
            Assert.IsNull(run.ThrowableId);
        }

        [Test]
        public void Completion_MarksTheDifficulty_AnyDifficultyOpensTheNext()
        {
            _progress.RecordRun("first", Completed(), difficulty: 2);

            Assert.IsTrue(_save.Data.FindLevel("first").IsCompletedOn(2));
            Assert.IsFalse(_save.Data.FindLevel("first").IsCompletedOn(0));
            Assert.IsTrue(_progress.IsCompleted("first"));
            Assert.IsTrue(_progress.IsUnlocked("second"));
            Assert.IsFalse(_progress.IsCompleted("second"));
        }

        [Test]
        public void ResetProgress_KeepsSettings()
        {
            _save.Data.Settings.MusicVolume = 0.4f;
            _save.Data.Money = 50;
            _save.Data.Arsenal.Add(new OwnedItem { Id = "bat" });
            _progress.RecordRun("first", Completed());

            _progress.ResetProgress();

            Assert.AreEqual(0, _save.Data.Money);
            Assert.IsEmpty(_save.Data.Arsenal);
            Assert.IsEmpty(_save.Data.Levels);
            Assert.AreEqual(0.4f, _save.Data.Settings.MusicVolume, 1e-5f);
        }

        [Test]
        public void ResetTouchControls_KeepsAim_ResetAim_KeepsTouchControls()
        {
            var settings = new SettingsService(_save);
            settings.InitializeAsync(CancellationToken.None);
            var controls = settings.Controls;
            controls.Size = 1.3f;
            controls.AimMode = AimMode.Four;
            controls.AimAssist = !ControlsSettings.Default.AimAssist;
            var layout = controls.Layout;
            layout.Use = new TouchPlacement { Moved = true, Position = new Vector2(0.5f, 0.5f), Scale = 1.2f };
            controls.Layout = layout;
            settings.SetControls(controls);

            settings.ResetAim();
            Assert.AreEqual(ControlsSettings.Default.AimMode, settings.Controls.AimMode);
            Assert.AreEqual(ControlsSettings.Default.AimAssist, settings.Controls.AimAssist);
            Assert.AreEqual(1.3f, settings.Controls.Size, 1e-5f);
            Assert.IsTrue(settings.Controls.Layout.Use.Moved);

            controls = settings.Controls;
            controls.AimMode = AimMode.Four;
            settings.SetControls(controls);
            settings.ResetTouchControls();
            Assert.AreEqual(AimMode.Four, settings.Controls.AimMode, "Aim stays.");
            Assert.AreEqual(ControlsSettings.Default.Size, settings.Controls.Size, 1e-5f);
            Assert.AreEqual(TouchLayout.Default, settings.Controls.Layout);
        }

        [Test]
        public void MapIconLayout_StableTiltAndShift_DoorAndPlayerRotation()
        {
            Assert.AreEqual(MapIconLayout.Tilt("door_1", 12f), MapIconLayout.Tilt("door_1", 12f), "Stable per id.");
            Assert.AreEqual(MapIconLayout.Shift("door_1", 0.15f), MapIconLayout.Shift("door_1", 0.15f));
            var differs = false;
            for (var i = 0; i < 20; i++)
            {
                var tilt = MapIconLayout.Tilt("door_" + i, 12f);
                var shift = MapIconLayout.Shift("door_" + i, 0.15f);
                Assert.LessOrEqual(Mathf.Abs(tilt), 12f);
                Assert.LessOrEqual(Mathf.Abs(shift.x), 0.15f);
                Assert.LessOrEqual(Mathf.Abs(shift.y), 0.15f);
                differs |= !Mathf.Approximately(tilt, MapIconLayout.Tilt("door_0", 12f));
            }

            Assert.IsTrue(differs, "Different objects lie differently.");

            var level = ScriptableObject.CreateInstance<LevelData>();
            try
            {
                // Row y=1: floor - door - floor (east-west passage, walls north and south); column x=3: a north-south one.
                var geometry = new LevelGeometry(5, 3);
                geometry.SetCell(new GridPosition(0, 1), CellType.Floor);
                geometry.SetCell(new GridPosition(1, 1), CellType.Door);
                geometry.SetCell(new GridPosition(2, 1), CellType.Floor);
                geometry.SetCell(new GridPosition(3, 0), CellType.Floor);
                geometry.SetCell(new GridPosition(3, 1), CellType.Door);
                geometry.SetCell(new GridPosition(3, 2), CellType.Floor);
                level.ReplaceGeometry(geometry);
                var grid = new LevelGrid(level.Geometry);
                Assert.AreEqual(90f, MapIconLayout.DoorRotation(grid, new GridPosition(1, 1)), "East-west passage: turned plank.");
                Assert.AreEqual(0f, MapIconLayout.DoorRotation(grid, new GridPosition(3, 1)), "North-south passage.");
            }
            finally
            {
                Object.DestroyImmediate(level);
            }

            Assert.AreEqual(0f, MapIconLayout.PlayerRotation(Vector2.up), 1e-4f);
            Assert.AreEqual(-90f, MapIconLayout.PlayerRotation(Vector2.right), 1e-4f, "Facing east: turned clockwise.");
            Assert.AreEqual(new Vector2(2.5f, 3.5f), MapIconLayout.CenterOf(new GridPosition(2, 3)));
            Assert.AreEqual(new Vector2(2.5f, 3.5f), MapIconLayout.CenterOf(new Vector2(2f, 3f)));
        }

        [Test]
        public void MapIconToggles_OnByDefault_AlsoInOldSaves_Saved()
        {
            var settings = new SettingsService(_save);
            Assert.IsTrue(settings.MapShowPlayer);
            Assert.IsTrue(settings.MapShowZombies);

            var writes = _storage.Writes;
            settings.MapShowPlayer = false;
            settings.MapShowPlayer = false;
            Assert.AreEqual(writes + 1, _storage.Writes, "Saved once per change.");

            var save = new SaveService(_storage);
            save.Load();
            var loaded = new SettingsService(save);
            Assert.IsFalse(loaded.MapShowPlayer);
            Assert.IsTrue(loaded.MapShowZombies);

            _storage.Json = "{\"Settings\":{\"ShowFps\":true}}";
            _save.Load();
            var old = new SettingsService(_save);
            Assert.IsTrue(old.MapShowPlayer, "Old saves show the icons.");
            Assert.IsTrue(old.MapShowZombies);
        }

        [Test]
        public void SaveWithoutControls_GetsDefaultControls()
        {
            _storage.Json = "{\"Settings\":{\"MusicVolume\":0.5,\"SfxVolume\":1.0}}";
            _save.Load();

            Assert.AreEqual(0.5f, _save.Data.Settings.MusicVolume);
            Assert.AreEqual(ControlsSettings.Default, _save.Data.Settings.Controls);
        }

        // ------------------------------------------------------------ Map

        [Test]
        public void MapRenderer_DrawsTheWholeLevel_DoorsAsPassages()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            try
            {
                var geometry = new LevelGeometry(4, 2);
                geometry.SetCell(new GridPosition(1, 0), CellType.Floor);
                geometry.SetCell(new GridPosition(2, 0), CellType.Door);
                geometry.SetCell(new GridPosition(3, 0), CellType.Floor);
                level.ReplaceGeometry(geometry);

                var grid = new LevelGrid(level.Geometry);
                var pixels = new Color32[grid.CellCount];
                MapRenderer.Render(grid, pixels);

                Assert.AreEqual(MapRenderer.Wall, pixels[grid.ToIndex(new GridPosition(0, 0))]);
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(1, 0))]);
                Assert.AreEqual(MapRenderer.Wall, pixels[grid.ToIndex(new GridPosition(1, 1))]);
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(2, 0))], "A door is a passage; its icon shows it.");
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(3, 0))]);
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        // ------------------------------------------------------------ Fakes

        private sealed class MemoryStorage : ISaveStorage
        {
            public string Json;
            public int Writes;

            public string Read() => Json;

            public void Write(string json)
            {
                Json = json;
                Writes++;
            }
        }

        private sealed class Catalog : ILevelCatalog
        {
            private readonly LevelCatalog _asset;

            public Catalog(LevelCatalog asset) => _asset = asset;

            public IReadOnlyList<LevelCatalogEntry> Levels => _asset.Levels;
            public IReadOnlyList<LevelCatalogEntry> DevLevels => System.Array.Empty<LevelCatalogEntry>();
            public LevelCatalogEntry Tutorial => _asset.Tutorial;
            public LevelCatalogEntry Find(string levelId) => _asset.Find(levelId);
        }
    }
}
