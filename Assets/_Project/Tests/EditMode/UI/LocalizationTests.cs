using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using Maze.Application.Save;
using Maze.Application.Services;
using Maze.Core.Localization;
using Maze.Presentation.Localization;
using Maze.Tests.EditMode.Common;
using NUnit.Framework;
using UnityEngine;

namespace Maze.Tests.EditMode.UI
{
    public class LocalizationTests
    {
        private const string Table = "# comment\n" +
                                     "key\ten\tru\n" +
                                     "language.name\tEnglish\tРусский\n" +
                                     "language.system\tEnglish\tRussian, Belarusian\n" +
                                     "\n" +
                                     "menu.play\tPLAY\tИГРАТЬ\n" +
                                     "hud.level\tLEVEL {0}\tУРОВЕНЬ {0}\n" +
                                     "two.lines\tA\\nB\t\n";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in _created) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [Test]
        public void Source_ParsesLanguagesRowsCommentsAndEscapes()
        {
            var source = LocalizationSource.Parse(Table);

            CollectionAssert.IsEmpty(source.Errors);
            CollectionAssert.AreEqual(new[] { "en", "ru" }, source.Languages);
            Assert.AreEqual(5, source.Rows.Count);
            Assert.AreEqual("ИГРАТЬ", source.Find("menu.play", "ru"));
            Assert.AreEqual("A\nB", source.Find("two.lines", "en"));
            Assert.IsNull(source.Find("two.lines", "ru"), "An empty cell is not translated.");
            StringAssert.Contains("Р", source.CharactersOf("ru"));
            Assert.IsFalse(source.CharactersOf("en").Contains('\n'));
        }

        [Test]
        public void Source_ReportsDuplicateKeysAndMissingBaseText()
        {
            var source = LocalizationSource.Parse("key\ten\tru\na\tA\tА\na\tA2\tА2\nb\t\tБ\n");

            Assert.AreEqual(2, source.Errors.Count);
            StringAssert.Contains("repeated", source.Errors[0]);
            StringAssert.Contains("base language", source.Errors[1]);
        }

        [Test]
        public void Check_FindsMissingUnusedUntranslatedAndPlaceholderProblems()
        {
            var source = LocalizationSource.Parse("key\ten\tru\n" +
                                                  "used\tUsed {0}\tИсп {1}\n" +
                                                  "unused\tX\tХ\n" +
                                                  "weapon.bat\tBat\t\n");

            var issues = LocalizationCheck.Run(source, new[] { "used", "absent" }, new[] { "weapon." });
            var errors = issues.Where(i => i.IsError).Select(i => i.Message).ToList();
            var warnings = issues.Where(i => !i.IsError).Select(i => i.Message).ToList();

            Assert.IsTrue(errors.Any(m => m.Contains("'absent'")), "Used key missing from the table.");
            Assert.IsTrue(errors.Any(m => m.Contains("'used'") && m.Contains("placeholders")));
            Assert.IsTrue(warnings.Any(m => m.Contains("'unused'") && m.Contains("not used")));
            Assert.IsFalse(warnings.Any(m => m.Contains("'weapon.bat'") && m.Contains("not used")), "Prefix families are used.");
            Assert.IsTrue(warnings.Any(m => m.Contains("'weapon.bat'") && m.Contains("not translated")));
        }

        [Test]
        public void Catalog_ChoosesSavedThenSystemThenBase()
        {
            var catalog = Catalog(out _);

            Assert.AreEqual(1, catalog.Choose("ru", SystemLanguage.English));
            Assert.AreEqual(1, catalog.Choose(null, SystemLanguage.Belarusian));
            Assert.AreEqual(0, catalog.Choose("xx", SystemLanguage.Japanese), "Unknown saved and system: the base language.");
        }

        [Test]
        public void Catalog_PlatformLanguage_ComesAfterSaved_BeforeSystem()
        {
            var catalog = Catalog(out _);

            Assert.AreEqual(1, catalog.Choose(null, "ru", SystemLanguage.English));
            Assert.AreEqual(1, catalog.Choose(null, "RU-ru", SystemLanguage.English), "Region and case do not matter.");
            Assert.AreEqual(0, catalog.Choose("en", "ru", SystemLanguage.Russian), "The player's own choice wins.");
            Assert.AreEqual(1, catalog.Choose(null, "xx", SystemLanguage.Russian), "An unknown site language: the system one.");
        }

        [Test]
        public void Service_PicksSystemLanguage_SavesIt_AndSwitches()
        {
            var catalog = Catalog(out var tables);
            var addressables = new FakeAddressables();
            addressables.Assets[LanguageCatalog.Address] = catalog;
            foreach (var table in tables) addressables.Assets[table.Language] = table;
            var save = new SaveService(new MemoryStorage());
            save.InitializeAsync(default).Forget();
            var settings = new SettingsService(save);
            var texts = new LocalizationService(addressables, settings) { ShowMissingKeys = false, SystemLanguage = SystemLanguage.Russian };
            var changes = 0;
            texts.LanguageChanged += () => changes++;

            Run(texts.InitializeAsync(default));

            Assert.AreEqual("ru", texts.Language);
            Assert.AreEqual("ru", settings.Language, "The first choice is saved.");
            Assert.AreEqual(1, changes, "Raised on the first load too.");
            Assert.AreEqual("ИГРАТЬ", texts.Get("menu.play"));
            Assert.AreEqual("УРОВЕНЬ 3", texts.Format("hud.level", 3));
            Assert.AreEqual("A\nB", texts.Get("two.lines"), "Untranslated: the base text.");
            Assert.AreEqual("no.such.key", texts.Get("no.such.key"));
            Assert.AreEqual(2, addressables.ActiveHandleCount, "Catalog and one table.");

            Run(texts.SetLanguageAsync(0));

            Assert.AreEqual("en", texts.Language);
            Assert.AreEqual("en", settings.Language);
            Assert.AreEqual(2, changes);
            Assert.AreEqual("PLAY", texts.Get("menu.play"));
            Assert.AreEqual(2, addressables.ActiveHandleCount, "The old table is released.");

            texts.Dispose();
            Assert.AreEqual(0, addressables.ActiveHandleCount);
        }

        [Test]
        public void Service_InDevelopment_ShowsKeysOfUntranslatedTexts()
        {
            var catalog = Catalog(out var tables);
            var addressables = new FakeAddressables();
            addressables.Assets[LanguageCatalog.Address] = catalog;
            foreach (var table in tables) addressables.Assets[table.Language] = table;
            var save = new SaveService(new MemoryStorage());
            save.InitializeAsync(default).Forget();
            var settings = new SettingsService(save) { Language = "ru" };
            var texts = new LocalizationService(addressables, settings) { ShowMissingKeys = true };

            Run(texts.InitializeAsync(default));

            Assert.AreEqual("two.lines", texts.Get("two.lines"));
            Assert.AreEqual("ИГРАТЬ", texts.Get("menu.play"));
            texts.Dispose();
        }

        /// <summary>The real table: every key of the game is there, translated into every language, placeholders agree.</summary>
        [Test]
        public void GameTable_HasEveryKey_AndEveryTranslation()
        {
            var source = LocalizationSource.Parse(File.ReadAllText("Assets/_Project/Data/Localization/Strings.tsv", Encoding.UTF8));
            var keys = new List<string>();
            var prefixes = new List<string>();
            foreach (var field in typeof(TextKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.IsLiteral)
                    (field.Name.EndsWith("Prefix") ? prefixes : keys).Add((string)field.GetRawConstantValue());

            var issues = LocalizationCheck.Run(source, keys, prefixes);

            CollectionAssert.IsEmpty(issues.Select(i => i.ToString()));
            CollectionAssert.IsSupersetOf(source.Languages, new[] { "en", "ru", "tr", "es", "de" });
        }

        private LanguageCatalog Catalog(out List<LocalizationTable> tables)
        {
            var source = LocalizationSource.Parse(Table);
            tables = new List<LocalizationTable>();
            var entries = new List<LanguageEntry>();
            for (var l = 0; l < source.Languages.Count; l++)
            {
                var table = ScriptableObject.CreateInstance<LocalizationTable>();
                _created.Add(table);
                var language = l;
                table.Fill(source.Languages[l], source.Rows.Select(r => r.Key).ToList(),
                    source.Rows.Select(r => r.Texts[language] ?? r.Texts[0]).ToList(),
                    source.Rows.Where(r => r.Texts[language] == null).Select(r => r.Key));
                tables.Add(table);
                var systems = source.Find(LocalizationSource.SystemKey, source.Languages[l]).Split(',')
                    .Select(n => (SystemLanguage)System.Enum.Parse(typeof(SystemLanguage), n.Trim()));
                // The fake loads a table reference by its "GUID": the language code here.
                entries.Add(new LanguageEntry(source.Languages[l], source.Find(LocalizationSource.NameKey, source.Languages[l]),
                    systems, source.Languages[l]));
            }

            var catalog = ScriptableObject.CreateInstance<LanguageCatalog>();
            _created.Add(catalog);
            catalog.SetLanguages(entries);
            return catalog;
        }

        private static void Run(UniTask task)
        {
            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "The fakes complete at once.");
            task.GetAwaiter().GetResult();
        }

        private sealed class MemoryStorage : ISaveStorage
        {
            private string _json;
            public string Read() => _json;
            public void Write(string json) => _json = json;
        }
    }
}
