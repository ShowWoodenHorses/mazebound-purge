using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Maze.Application.Levels;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Core.Grid;
using Maze.Core.Level;
using Maze.Gameplay.Combat;
using Maze.Gameplay.Level;
using Maze.Gameplay.Map;
using Maze.Presentation.Map;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Maze.Tests.EditMode.Progress
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

        private static LevelResult Completed(int kills = 0, int zombies = 0, int fragments = 0, int totalFragments = 0) =>
            new LevelResult(true, kills, zombies, fragments, totalFragments);

        // ------------------------------------------------------------ Time

        [Test]
        public void BestTime_KeepsTheFastestCompletedRun_AndSurvivesReload()
        {
            Assert.AreEqual(0f, _progress.GetBestTime("first"), "No time before a completed run.");
            _progress.RecordCompletion("first", new LevelResult(true, 0, 0, 0, 0, 80f));
            _progress.RecordCompletion("first", new LevelResult(true, 0, 0, 0, 0, 95f));
            Assert.AreEqual(80f, _progress.GetBestTime("first"), "A slower run keeps the best.");
            _progress.RecordCompletion("first", new LevelResult(true, 0, 0, 0, 0, 61.5f));
            _progress.RecordCompletion("first", Completed());
            Assert.AreEqual(61.5f, _progress.GetBestTime("first"), "Faster replaces it; an unknown time (0) does not.");

            var reloaded = new SaveService(_storage);
            reloaded.Load();
            Assert.AreEqual(61.5f, new ProgressService(reloaded, new Catalog(_catalogAsset)).GetBestTime("first"));
        }

        // ------------------------------------------------------------ Stars

        [Test]
        public void Stars_ExitAllZombiesAllFragments()
        {
            Assert.AreEqual(1, Completed(kills: 1, zombies: 2, fragments: 0, totalFragments: 1).Stars);
            Assert.AreEqual(2, Completed(kills: 2, zombies: 2, fragments: 0, totalFragments: 1).Stars);
            Assert.AreEqual(2, Completed(kills: 0, zombies: 2, fragments: 1, totalFragments: 1).Stars);
            Assert.AreEqual(3, Completed(kills: 2, zombies: 2, fragments: 1, totalFragments: 1).Stars);
            Assert.AreEqual(3, Completed().Stars, "Nothing to kill or collect: those stars are free.");
            Assert.AreEqual(0, new LevelResult(false, 2, 2, 1, 1).Stars, "No stars without completion.");
        }

        // ------------------------------------------------------------ Progress

        [Test]
        public void LevelAddedAfterTheLastCompleted_IsUnlocked()
        {
            _catalogAsset.Remove("third");
            _progress.RecordCompletion("first", Completed());
            _progress.RecordCompletion("second", Completed());
            Assert.IsFalse(_save.Data.UnlockedLevels.Contains("third"), "Not in the catalog when 'second' was completed.");

            _catalogAsset.AddOrUpdate("third", "Levels/third", "Third"); // An update brings a new level.
            Assert.IsTrue(_progress.IsUnlocked("third"), "The previous level is completed: the new one is open.");
            Assert.IsFalse(_progress.IsUnlocked("unknown"));
        }

        [Test]
        public void OnlyFirstLevelIsUnlocked_CompletionUnlocksTheNext()
        {
            Assert.IsTrue(_progress.IsUnlocked("first"));
            Assert.IsFalse(_progress.IsUnlocked("second"));

            _progress.RecordCompletion("first", Completed());

            Assert.IsTrue(_progress.IsUnlocked("second"));
            Assert.IsFalse(_progress.IsUnlocked("third"));
        }

        [Test]
        public void BestStarsAreKept_KillsAreSummed()
        {
            _progress.RecordCompletion("first", Completed(kills: 2, zombies: 2));
            _progress.RecordCompletion("first", Completed(kills: 1, zombies: 2));

            Assert.AreEqual(3, _progress.GetStars("first"));
            Assert.AreEqual(3, _progress.TotalKills);
            Assert.AreEqual(0, _progress.GetStars("second"));
        }

        [Test]
        public void FailedRun_IsNotRecorded()
        {
            Assert.Throws<System.ArgumentException>(() => _progress.RecordCompletion("first", new LevelResult(false, 1, 1, 0, 0)));
            Assert.AreEqual(0, _storage.Writes);
        }

        [Test]
        public void Progress_SurvivesRestart()
        {
            _progress.RecordCompletion("first", Completed(kills: 1, zombies: 3));
            Assert.AreEqual(1, _storage.Writes, "Saved right after completion.");

            var save = new SaveService(_storage);
            save.Load();
            var progress = new ProgressService(save, new Catalog(_catalogAsset));

            Assert.AreEqual(2, progress.GetStars("first"));
            Assert.AreEqual(1, progress.TotalKills);
            Assert.IsTrue(progress.IsUnlocked("second"));
        }

        [Test]
        public void ResetProgress_ForgetsEverything()
        {
            _progress.RecordCompletion("first", Completed(kills: 4, zombies: 4));
            _progress.ResetProgress();

            Assert.AreEqual(0, _progress.GetStars("first"));
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

            Assert.IsNotNull(_save.Data.UnlockedLevels);
            Assert.AreEqual(0, _save.Data.TotalKills);
        }

        [Test]
        public void MissingFields_AreFilledIn()
        {
            _storage.Json = "{\"TotalKills\":5}";
            _save.Load();

            Assert.AreEqual(5, _save.Data.TotalKills);
            Assert.IsNotNull(_save.Data.LevelStars);
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
        public void Version2Save_AttackClusterAndUsePlacesReset_StickKept()
        {
            const string moved = "{\"Moved\":true,\"Position\":{\"x\":0.3,\"y\":0.4},\"Scale\":1.2}";
            _storage.Json = "{\"Version\":2,\"Settings\":{\"Controls\":{\"Size\":1,\"Opacity\":0.6,\"Layout\":{" +
                            $"\"Stick\":{moved},\"Attack\":{moved},\"Use\":{moved},\"Melee\":{moved},\"Ranged\":{moved}" + "}}}}";
            _save.Load();

            var layout = _save.Data.Settings.Controls.Layout;
            Assert.AreEqual(TouchPlacement.Default, layout.Attack, "The cluster starts at its built-in place.");
            Assert.AreEqual(TouchPlacement.Default, layout.Melee);
            Assert.AreEqual(TouchPlacement.Default, layout.Ranged);
            Assert.IsTrue(layout.Stick.Moved);
            Assert.AreEqual(TouchPlacement.Default, layout.Use, "v4: Use has a new built-in place.");
            Assert.AreEqual(SaveData.CurrentVersion, _save.Data.Version);
        }

        [Test]
        public void Version3Save_OnlyUsePlaceReset()
        {
            const string moved = "{\"Moved\":true,\"Position\":{\"x\":0.3,\"y\":0.4},\"Scale\":1.2}";
            _storage.Json = "{\"Version\":3,\"Settings\":{\"Controls\":{\"Size\":1,\"Opacity\":0.6,\"Layout\":{" +
                            $"\"Stick\":{moved},\"Attack\":{moved},\"Use\":{moved}" + "}}}}";
            _save.Load();

            var layout = _save.Data.Settings.Controls.Layout;
            Assert.IsTrue(layout.Attack.Moved, "The cluster's place (v3) is kept.");
            Assert.IsTrue(layout.Stick.Moved);
            Assert.AreEqual(TouchPlacement.Default, layout.Use);
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
            Assert.IsTrue(settings.MapShowFragments);

            var writes = _storage.Writes;
            settings.MapShowPlayer = false;
            settings.MapShowPlayer = false;
            Assert.AreEqual(writes + 1, _storage.Writes, "Saved once per change.");

            var save = new SaveService(_storage);
            save.Load();
            var loaded = new SettingsService(save);
            Assert.IsFalse(loaded.MapShowPlayer);
            Assert.IsTrue(loaded.MapShowFragments);

            _storage.Json = "{\"Settings\":{\"ShowFps\":true}}";
            _save.Load();
            var old = new SettingsService(_save);
            Assert.IsTrue(old.MapShowPlayer, "Old saves show the icons.");
            Assert.IsTrue(old.MapShowFragments);
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
        public void MapRenderer_DrawsOnlyCollectedRegions_DoorsAsPassages()
        {
            var level = ScriptableObject.CreateInstance<LevelData>();
            try
            {
                var geometry = new LevelGeometry(4, 2);
                geometry.SetCell(new GridPosition(1, 0), CellType.Floor);
                geometry.SetCell(new GridPosition(2, 0), CellType.Door);
                geometry.SetCell(new GridPosition(3, 0), CellType.Floor);
                level.ReplaceGeometry(geometry);
                level.MutableMapFragments.Add(new MapFragmentData("fragment_1", new GridPosition(1, 0), new GridRect(0, 0, 2, 2)));
                level.MutableMapFragments.Add(new MapFragmentData("fragment_2", new GridPosition(3, 0), new GridRect(2, 0, 2, 2)));

                var grid = new LevelGrid(level.Geometry);
                var map = new MapSystem(level);
                var pixels = new Color32[grid.CellCount];

                MapRenderer.Render(grid, map, pixels);
                foreach (var pixel in pixels)
                    Assert.AreEqual(MapRenderer.Unknown, pixel, "Nothing collected: the map is empty.");

                map.Collect(level.MapFragments[0]);
                MapRenderer.Render(grid, map, pixels);
                Assert.AreEqual(MapRenderer.Wall, pixels[grid.ToIndex(new GridPosition(0, 0))]);
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(1, 0))]);
                Assert.AreEqual(MapRenderer.Wall, pixels[grid.ToIndex(new GridPosition(1, 1))]);
                Assert.AreEqual(MapRenderer.Unknown, pixels[grid.ToIndex(new GridPosition(2, 0))], "Other fragment's region.");

                map.Collect(level.MapFragments[1]);
                MapRenderer.Render(grid, map, pixels);
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(2, 0))], "A door is a passage; its icon shows it.");
                Assert.AreEqual(MapRenderer.Floor, pixels[grid.ToIndex(new GridPosition(3, 0))]);
                Assert.IsTrue(map.AllCollected);
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
