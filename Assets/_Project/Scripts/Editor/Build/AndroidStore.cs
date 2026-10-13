using System;
using System.IO;
using UnityEngine;

namespace Maze.Editor.Build
{
    /// <summary>The store an Android build is made for (Docs/Progress.md «Площадки публикации»).</summary>
    internal enum AndroidStore
    {
        /// <summary>No store: a local test build (simulated ads, analytics in the log).</summary>
        Generic = 0,

        /// <summary>RuStore: VK Ad Network, MyTracker, the privacy policy in the settings.</summary>
        RuStore = 1,
    }

    internal static class AndroidStores
    {
        /// <summary>The publisher's name (<c>PlayerSettings.companyName</c>).</summary>
        public const string CompanyName = "BGames";

        /// <summary>The Android package: never changes once the app is published (the store ties updates to it).</summary>
        public const string PackageName = "ru.bgames.mazebound";

        /// <summary>The scripting symbol of the store's build (<c>ProjectLifetimeScope</c> registers its adapters).</summary>
        public static string Define(AndroidStore store) => store switch
        {
            AndroidStore.RuStore => "MAZE_RUSTORE",
            _ => null,
        };

        /// <summary>The APK in <c>Builds/Android</c>: Mazebound[-RuStore][-dev].apk.</summary>
        public static string FileName(AndroidStore store, bool development) =>
            "Mazebound" + (store == AndroidStore.Generic ? "" : "-" + store) + (development ? "-dev" : "") + ".apk";

        /// <summary>
        /// Android versionCode from the version "major.minor.patch" (Player Settings → Version): major·10000 +
        /// minor·100 + patch, so a store always gets a bigger code for a newer version. Null when the version is
        /// not in that form (minor and patch 0–99).
        /// </summary>
        public static int? VersionCode(string version)
        {
            var parts = (version ?? "").Trim().Split('.');
            if (parts.Length != 3) return null;
            var numbers = new int[3];
            for (var i = 0; i < 3; i++)
                if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0 || (i > 0 && numbers[i] > 99))
                    return null;
            if (numbers[0] < 1 && numbers[1] < 1 && numbers[2] < 1) return null;
            return numbers[0] * 10000 + numbers[1] * 100 + numbers[2];
        }

        /// <summary>
        /// The release signing key, kept out of the repository: <c>UserSettings/MazeAndroidSigning.json</c>
        /// (UserSettings is git-ignored). Passwords never go into ProjectSettings.
        /// </summary>
        public const string SigningFile = "UserSettings/MazeAndroidSigning.json";

        [Serializable]
        public sealed class Signing
        {
            public string keystore = "";
            public string keystorePass = "";
            public string alias = "";
            public string aliasPass = "";
        }

        /// <summary>The signing settings, or a problem description (and a template file to fill in when there is none).</summary>
        public static (Signing Signing, string Problem) LoadSigning()
        {
            if (!File.Exists(SigningFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SigningFile));
                File.WriteAllText(SigningFile, JsonUtility.ToJson(new Signing { alias = "mazebound" }, true));
                return (null, $"No release key: fill in '{Path.GetFullPath(SigningFile)}' (keystore path, passwords, alias) — see Docs/MazeMenu.md.");
            }

            var signing = JsonUtility.FromJson<Signing>(File.ReadAllText(SigningFile));
            if (signing == null || string.IsNullOrWhiteSpace(signing.keystore) || string.IsNullOrEmpty(signing.keystorePass) ||
                string.IsNullOrWhiteSpace(signing.alias) || string.IsNullOrEmpty(signing.aliasPass))
                return (null, $"'{Path.GetFullPath(SigningFile)}' is incomplete: keystore, keystorePass, alias and aliasPass are needed.");
            if (!File.Exists(signing.keystore))
                return (null, $"The keystore '{signing.keystore}' from '{SigningFile}' does not exist.");
            return (signing, null);
        }
    }
}
