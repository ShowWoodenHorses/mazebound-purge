using System.IO;
using System.Linq;
using Maze.Core.Level;
using Maze.Editor.Dev;
using Maze.Editor.LevelDesigner;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Maze.Editor.Build
{
    /// <summary>
    /// Player builds (ТЗ §1–2: WebGL 2 and Android): checks the catalog levels, builds Addressables content for the
    /// active platform, then the player into <c>Builds/&lt;Platform&gt;</c> (git-ignored). The platform must be active:
    /// otherwise the menu only switches to it (a long reimport) and has to be run again.
    /// Development builds show the debug buttons and let any level start; release builds do not.
    /// </summary>
    internal static class MazeBuilder
    {
        private const string Output = "Builds";

        [MenuItem("Maze/Build/WebGL (Development)")]
        public static void WebGLDevelopment() => Build(BuildTarget.WebGL, development: true);

        [MenuItem("Maze/Build/WebGL (Release)")]
        public static void WebGLRelease() => Build(BuildTarget.WebGL, development: false);

        [MenuItem("Maze/Build/WebGL Yandex Games (Development)")]
        public static void WebGLYandexDevelopment() => Build(BuildTarget.WebGL, development: true, WebPlatform.Yandex);

        [MenuItem("Maze/Build/WebGL Yandex Games (Release)")]
        public static void WebGLYandexRelease() => Build(BuildTarget.WebGL, development: false, WebPlatform.Yandex);

        [MenuItem("Maze/Build/WebGL CrazyGames (Development)")]
        public static void WebGLCrazyGamesDevelopment() => Build(BuildTarget.WebGL, development: true, WebPlatform.CrazyGames);

        [MenuItem("Maze/Build/WebGL CrazyGames (Release)")]
        public static void WebGLCrazyGamesRelease() => Build(BuildTarget.WebGL, development: false, WebPlatform.CrazyGames);

        [MenuItem("Maze/Build/WebGL VK Games (Development)")]
        public static void WebGLVkDevelopment() => Build(BuildTarget.WebGL, development: true, WebPlatform.Vk);

        [MenuItem("Maze/Build/WebGL VK Games (Release)")]
        public static void WebGLVkRelease() => Build(BuildTarget.WebGL, development: false, WebPlatform.Vk);

        [MenuItem("Maze/Build/WebGL GameDistribution (Development)")]
        public static void WebGLGameDistributionDevelopment() => Build(BuildTarget.WebGL, development: true, WebPlatform.GameDistribution);

        [MenuItem("Maze/Build/WebGL GameDistribution (Release)")]
        public static void WebGLGameDistributionRelease() => Build(BuildTarget.WebGL, development: false, WebPlatform.GameDistribution);

        [MenuItem("Maze/Build/Android APK (Development)")]
        public static void AndroidDevelopment() => Build(BuildTarget.Android, development: true);

        [MenuItem("Maze/Build/Android APK (Release)")]
        public static void AndroidRelease() => Build(BuildTarget.Android, development: false);

        [MenuItem("Maze/Build/Android RuStore APK (Development)")]
        public static void AndroidRuStoreDevelopment() => Build(BuildTarget.Android, development: true, store: AndroidStore.RuStore);

        [MenuItem("Maze/Build/Android RuStore APK (Release)")]
        public static void AndroidRuStoreRelease() => Build(BuildTarget.Android, development: false, store: AndroidStore.RuStore);

        /// <summary>
        /// Builds the player. Returns the report summary, or a problem description when nothing was built.
        /// <paramref name="web"/> — the site of a WebGL build: its scripting symbol goes only to this player build
        /// (the editor keeps compiling without it), its SDK script into the page, the build into its own folder.
        /// <paramref name="store"/> — the same for an Android build (its symbol, its APK name). An Android release
        /// build is signed with the key from <see cref="AndroidStores.SigningFile"/>.
        /// </summary>
        public static string Build(BuildTarget target, bool development, WebPlatform web = WebPlatform.Generic,
            AndroidStore store = AndroidStore.Generic)
        {
            if (target != BuildTarget.WebGL) web = WebPlatform.Generic;
            if (target != BuildTarget.Android) store = AndroidStore.Generic;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
                return Fail($"Switched the active platform to {target}. Run the build again once the reimport is done.");
            }

            var problem = CheckLevels();
            if (problem != null) return Fail(problem);

            ConfigurePlatform(target, development);
            if (target == BuildTarget.WebGL)
                PlayerSettings.SetTemplateCustomValue(PlatformHeadVariable, WebPlatforms.Head(web));
            AndroidStores.Signing signing = null;
            if (target == BuildTarget.Android)
            {
                problem = ConfigureAndroid();
                if (problem == null && !development) (signing, problem) = AndroidStores.LoadSigning();
                if (problem == null) problem = ResolveAndroidDependencies();
                if (problem != null) return Fail(problem);
            }

            // Addressables content must be built for the active platform before the player.
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null) return Fail("No Addressables settings: run Build / Sync in the Level Designer first.");

            // One copy of every shared dependency: a definition copied into two bundles is two different objects at
            // runtime (weapons in hands were not found on device), and duplicates only make the build bigger.
            // A bundle per visual prefab (before looking for duplicates: they depend on the packing).
            LevelSync.PackSeparately(settings.FindGroup(LevelSync.VisualsGroup));
            var isolated = LevelSync.IsolateDuplicates(settings);
            if (isolated > 0)
                Debug.Log($"[Maze] {isolated} asset(s) used by several Addressables groups are kept in '{LevelSync.SharedDependenciesGroup}'.");
            var duplicates = LevelSync.FindDuplicates(settings);
            if (duplicates.Count > 0)
                return Fail("Assets are still copied into several Addressables bundles: " + string.Join(", ", duplicates.Take(10)));
            AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
            // Development levels (and their catalog) ship only in development builds.
            var devIncluded = LevelSync.SetDevLevelsIncluded(settings, development);
            AddressablesPlayerBuildResult content;
            try
            {
                AddressableAssetSettings.BuildPlayerContent(out content);
            }
            finally
            {
                LevelSync.SetDevLevelsIncluded(settings, devIncluded);
            }

            if (!string.IsNullOrEmpty(content.Error)) return Fail("Addressables build failed: " + content.Error);

            var location = target == BuildTarget.Android
                ? Path.Combine(Output, "Android", AndroidStores.FileName(store, development))
                : Path.Combine(Output, WebPlatforms.Folder(web, development));
            var define = target == BuildTarget.Android ? AndroidStores.Define(store) : WebPlatforms.Define(web);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { RuntimeScenesBuilder.BootstrapPath, RuntimeScenesBuilder.GamePath },
                locationPathName = location,
                target = target,
                targetGroup = group,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = define != null ? new[] { define } : null,
                // Without it the player is built for subtarget 0 (WebGL: Generic — DXT textures in the player's data)
                // and the editor setting is reset to it: every next build reimported all textures back to ASTC.
                subtarget = target == BuildTarget.WebGL ? (int)WebGLTextureSubtarget.ASTC : 0,
            };

            BuildReport report;
            try
            {
                UseSigningKey(signing);
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                // The key's path and passwords never stay in ProjectSettings: the build saved the settings with them.
                UseSigningKey(null);
                AssetDatabase.SaveAssets();
            }

            var summary = report.summary;
            var platformName = web != WebPlatform.Generic ? " " + web : store != AndroidStore.Generic ? " " + store : "";
            if (target == BuildTarget.Android)
                platformName += $" {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}){(signing != null ? ", signed" : ", debug key")}";
            var text = $"{target}{platformName} {(development ? "development" : "release")} build: {summary.result}, " +
                       $"{summary.totalErrors} error(s), {summary.totalWarnings} warning(s), " +
                       $"{SizeOf(location) / (1024f * 1024f):0.0} MB, {summary.totalTime.TotalSeconds:0}s → {Path.GetFullPath(location)}";
            if (summary.result == BuildResult.Succeeded && WebPlatforms.NeedsArchive(web))
                text += " + " + Archive(location);
            if (summary.result == BuildResult.Succeeded && web == WebPlatform.Vk)
                text += " + " + PrepareVkUpload(location);
            if (summary.result == BuildResult.Succeeded) Debug.Log("[Maze] " + text);
            else Debug.LogError("[Maze] " + text);
            return text;
        }

        /// <summary>The page around the WebGL build: <c>Assets/WebGLTemplates/Mazebound</c>.</summary>
        private const string WebTemplate = "PROJECT:Mazebound";

        /// <summary>The template's variable for the site's SDK script (<see cref="WebPlatforms.Head"/>).</summary>
        private const string PlatformHeadVariable = "MAZE_PLATFORM_HEAD";

        /// <summary>Settings the builds rely on; kept in code so a fresh checkout builds the same way.</summary>
        private static void ConfigurePlatform(BuildTarget target, bool development)
        {
            if (target == BuildTarget.WebGL)
            {
                // Full window, own loading bar, no scrollbars or page gestures (platform rules).
                PlayerSettings.WebGL.template = WebTemplate;
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                // Without it a compressed build only runs on a server that sends Content-Encoding (any static server
                // works, plain http on a phone too). A platform serving the files with the right headers can drop it.
                PlayerSettings.WebGL.decompressionFallback = true;
                // New file names for new content: browsers and CDNs never mix an old cached file into a new version.
                PlayerSettings.WebGL.nameFilesAsHashes = true;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.showDiagnostics = false;
                PlayerSettings.WebGL.exceptionSupport = development
                    ? WebGLExceptionSupport.FullWithStacktrace
                    : WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly; // GameFlow catches them into the error screen
                PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL,
                    development ? Il2CppCodeGeneration.OptimizeSpeed : Il2CppCodeGeneration.OptimizeSize);
                // Our assemblies and VContainer (reflection) are kept whole by Assets/_Project/link.xml.
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
                PlayerSettings.stripEngineCode = true;
                // The page decides what is secure (platforms serve https, the browser blocks mixed content); Unity's
                // own ban on http would break a build opened over the local network (http://192.168.x.x on a phone).
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                // Textures: ASTC. Phones (most of the web audience) have no DXT — Unity unpacked every texture there to
                // raw RGBA (4–8x the memory); a desktop without ASTC unpacks instead, it has the memory.
                if (EditorUserBuildSettings.webGLBuildSubtarget != WebGLTextureSubtarget.ASTC)
                    EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ASTC; // reimports the textures
            }
            else
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.NotAllowed; // Android: no cleartext traffic
            }

            // No "Made with Unity" screen (optional since Unity 6, Personal included): the game's own loading shows.
            PlayerSettings.SplashScreen.show = false;

            // The game is landscape only (both landscape sides, rotates between them).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        /// <summary>The app icon (Art/AppIcon; the adaptive icon shows the picture as its background layer).</summary>
        private const string AppIconPath = "Assets/_Project/Art/AppIcon/AppIcon.png";

        private const string AppIconForegroundPath = "Assets/_Project/Art/AppIcon/AppIconForeground.png";

        /// <summary>Gradle templates External Dependency Manager writes the SDKs' Android libraries into.</summary>
        private const string GradleTemplatesFolder = "Assets/Plugins/Android";

        private static readonly (string File, string Setting)[] GradleTemplates =
        {
            ("mainTemplate.gradle", "useCustomMainGradleTemplate"),
            ("gradleTemplate.properties", "useCustomGradlePropertiesTemplate"),
            ("settingsTemplate.gradle", "useCustomGradleSettingsTemplate"),
        };

        /// <summary>
        /// Android settings the store builds rely on (ТЗ §1–2; RuStore): the package, the version code from the
        /// version, IL2CPP for ARM64 and ARMv7 (cheap phones are still 32-bit), the newest target API, an APK, the
        /// icon, the Gradle templates. Returns a problem, or null.
        /// </summary>
        private static string ConfigureAndroid()
        {
            var versionCode = AndroidStores.VersionCode(PlayerSettings.bundleVersion);
            if (versionCode == null)
                return $"Player Settings → Version '{PlayerSettings.bundleVersion}' is not 'major.minor.patch' (minor, patch 0–99): " +
                       "the Android version code is made from it.";

            PlayerSettings.companyName = AndroidStores.CompanyName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidStores.PackageName);
            PlayerSettings.Android.bundleVersionCode = versionCode.Value;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto; // the newest installed
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.splitApplicationBinary = false;
            EditorUserBuildSettings.buildAppBundle = false; // RuStore takes an APK signed with our own key
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            SetAppIcons();
            return EnableGradleTemplates();
        }

        private static void SetAppIcons()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconForegroundPath);
            if (icon == null || foreground == null)
            {
                Debug.LogWarning($"[Maze] Build: no app icon at '{AppIconPath}' — the default Unity icon is used.");
                return;
            }

            var target = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
            {
                var icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (var slot in icons)
                    slot.SetTextures(slot.maxLayerCount > 1 ? new[] { icon, foreground } : new[] { icon });
                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
        }

        /// <summary>Copies Unity's Gradle templates into the project once and switches them on.</summary>
        private static string EnableGradleTemplates()
        {
            var source = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/Tools/GradleTemplates");
            Directory.CreateDirectory(GradleTemplatesFolder);
            var copied = false;
            foreach (var (file, _) in GradleTemplates)
            {
                var destination = Path.Combine(GradleTemplatesFolder, file);
                if (File.Exists(destination)) continue;
                var template = Path.Combine(source, file);
                if (!File.Exists(template)) return $"Unity's Gradle template '{template}' is missing (Android Build Support).";
                File.Copy(template, destination);
                copied = true;
            }

            if (copied) AssetDatabase.Refresh();
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            foreach (var (_, setting) in GradleTemplates)
                settings.FindProperty(setting).boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            return null;
        }

        /// <summary>
        /// External Dependency Manager writes the Android libraries the SDKs need (VK Ad Network, MyTracker) into
        /// the Gradle templates; Gradle downloads them while building.
        /// </summary>
        private static string ResolveAndroidDependencies()
        {
            var resolver = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver"))
                .FirstOrDefault(t => t != null);
            var resolve = resolver?.GetMethod("ResolveSync", new[] { typeof(bool) });
            if (resolve == null) return "External Dependency Manager is not loaded: the SDKs' Android libraries cannot be resolved.";
            var resolved = resolve.Invoke(null, new object[] { true }) is true;
            return resolved ? null : "External Dependency Manager failed to resolve the Android libraries (see its log in the console).";
        }

        /// <summary>The release key for the build (null: the debug key; also clears the key's path and passwords).</summary>
        private static void UseSigningKey(AndroidStores.Signing signing)
        {
            PlayerSettings.Android.useCustomKeystore = signing != null;
            PlayerSettings.Android.keystoreName = signing?.keystore ?? "";
            PlayerSettings.Android.keystorePass = signing?.keystorePass ?? "";
            PlayerSettings.Android.keyaliasName = signing?.alias ?? "";
            PlayerSettings.Android.keyaliasPass = signing?.aliasPass ?? "";
        }

        /// <summary>Every catalog level must be valid: the game refuses to start an invalid one.</summary>
        private static string CheckLevels()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(LevelSync.CatalogPath);
            if (catalog == null || catalog.Levels.Count == 0)
                return "The level catalog is empty: run Build / Sync for at least one level.";

            var problems = catalog.Levels
                .Select(entry => (entry, level: AssetDatabase.FindAssets($"t:{nameof(LevelData)} {entry.LevelId}")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<LevelData>)
                    .FirstOrDefault(l => l != null && l.name == entry.LevelId)))
                .Select(x => x.level == null
                    ? $"'{x.entry.LevelId}' not found"
                    : EditorLevelValidator.Validate(x.level) is var report && !report.IsValid
                        ? $"'{x.entry.LevelId}' has {report.ErrorCount} validation error(s)"
                        : null)
                .Where(p => p != null)
                .ToList();
            return problems.Count == 0 ? null : "Catalog levels are not ready: " + string.Join("; ", problems);
        }

        /// <summary>The build folder as a zip next to it (the site's console takes an archive with index.html at its root).</summary>
        private static string Archive(string folder)
        {
            var zip = folder.TrimEnd('/', '\\') + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal,
                includeBaseDirectory: false);
            return $"{new FileInfo(zip).Length / (1024f * 1024f):0.0} MB archive → {Path.GetFullPath(zip)}";
        }

        /// <summary>The upload config of the VK hosting (<c>vk-miniapps-deploy</c>, run in <c>Builds</c>).</summary>
        public const string VkHostingConfig = "vk-hosting-config.json";

        /// <summary>Files the VK hosting refuses (no audio or video files there).</summary>
        private static readonly string[] VkForbiddenExtensions = { ".mp3", ".ogg", ".wav", ".m4a", ".aac", ".mp4", ".webm", ".mov" };

        /// <summary>
        /// VK build: VK Bridge next to the page, <c>Builds/vk-hosting-config.json</c> pointing at this build (the upload
        /// utility zips the folder itself and updates the game's addresses), a check for files the hosting refuses.
        /// </summary>
        private static string PrepareVkUpload(string folder)
        {
            if (!File.Exists(WebPlatforms.VkBridgeSource))
                return $"NO {WebPlatforms.VkBridgeFile}: '{WebPlatforms.VkBridgeSource}' is missing — the game will not see VK";
            File.Copy(WebPlatforms.VkBridgeSource, Path.Combine(folder, WebPlatforms.VkBridgeFile), overwrite: true);

            var appId = WebPlatforms.VkAppId.Trim();
            var appIdValue = appId.Length > 0 ? appId : "\"\"";
            var config = "{\n" +
                         $"  \"static_path\": \"{Path.GetFileName(folder.TrimEnd('/', '\\'))}\",\n" +
                         $"  \"app_id\": {appIdValue},\n" +
                         "  \"endpoints\": { \"web\": \"index.html\", \"mvk\": \"index.html\", \"mobile\": \"index.html\" }\n" +
                         "}\n";
            var configPath = Path.Combine(Output, VkHostingConfig);
            File.WriteAllText(configPath, config);

            var forbidden = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => VkForbiddenExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Path.GetFileName(f))
                .ToList();
            var text = $"{VkHostingConfig} → {Path.GetFullPath(configPath)}";
            if (appId.Length == 0) text += " (no app id: set WebPlatforms.VkAppId or MINI_APPS_APP_ID)";
            if (forbidden.Count > 0)
            {
                text += "; the VK hosting refuses audio/video files: " + string.Join(", ", forbidden.Take(10));
                Debug.LogWarning("[Maze] " + text);
            }

            return text;
        }

        /// <summary>Size of what ships: the APK file or the WebGL folder (the report also counts IL2CPP symbol backups).</summary>
        private static long SizeOf(string location)
        {
            if (File.Exists(location)) return new FileInfo(location).Length;
            if (!Directory.Exists(location)) return 0;
            return new DirectoryInfo(location).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        private static string Fail(string problem)
        {
            Debug.LogWarning("[Maze] Build: " + problem);
            return problem;
        }
    }
}
