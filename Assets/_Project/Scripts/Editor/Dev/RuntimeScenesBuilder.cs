using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maze.Application.Save;
using Maze.Composition;
using Maze.Core.Localization;
using Maze.Presentation.Localization;
using Maze.Presentation.Map;
using Maze.Presentation.UI;
using Maze.Presentation.UI.Shapes;
using Maze.Presentation.UI.Style;
using Maze.Presentation.UI.Touch;
using Maze.Presentation.Visual;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Creates the runtime scenes (ТЗ §6–7) with placeholder uGUI screens and puts them into Build Settings:
    /// Bootstrap (first; ProjectLifetimeScope, application UI) and Game (additive; LevelLifetimeScope, level view root,
    /// top-down camera, light).
    /// Re-running overwrites both scenes.
    /// </summary>
    internal static class RuntimeScenesBuilder
    {
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string BootstrapPath = ScenesFolder + "/Bootstrap.unity";
        public const string GamePath = ScenesFolder + "/Game.unity";

        /// <summary>Ground of the menu screens (under the backdrop picture) and of the game camera before a level.</summary>
        private static readonly Color Background = new Color(0.098f, 0.09f, 0.078f, 1f);

        /// <summary>Over the paused level: pause, result, "Finish level?".</summary>
        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.62f);

        private static readonly Color MapBackground = new Color(0.07f, 0.066f, 0.06f, 1f);

        private static UiStyle _style;

        /// <summary>Base-language texts for <see cref="Loc"/>; captions made from keys, for <see cref="UIRoot"/>.</summary>
        private static LocalizationSource _strings;
        private static readonly List<LocalizedText> Localized = new List<LocalizedText>();

        /// <summary>Marks a caption argument of the helpers below as a key: <see cref="Text"/> localizes it.</summary>
        private const char KeyMark = '\u0001';

        [MenuItem("Maze/Dev/Build Runtime Scenes")]
        public static void Build()
        {
            var existing = File.Exists(BootstrapPath) || File.Exists(GamePath);
            if (existing && !EditorUtility.DisplayDialog("Build Runtime Scenes",
                    "Bootstrap and Game scenes already exist and will be overwritten.", "Overwrite", "Cancel"))
                return;

            var problem = Rebuild();
            if (problem != null)
                EditorUtility.DisplayDialog("Build Runtime Scenes", problem, "OK");
        }

        /// <summary>Builds both scenes without asking (tools, automation). Returns why it could not, or null.</summary>
        public static string Rebuild()
        {
            // Scenes are created additively and closed after saving, so the open scenes stay as they are.
            // Unity cannot add a new scene next to an untitled one.
            var openPaths = Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).path).ToList();
            if (openPaths.Any(string.IsNullOrEmpty))
                return "Save or close the untitled scene first.";
            if (openPaths.Contains(BootstrapPath) || openPaths.Contains(GamePath))
                return "Close the Bootstrap and Game scenes first.";

            if (!AssetDatabase.IsValidFolder(ScenesFolder))
                AssetDatabase.CreateFolder("Assets/_Project", "Scenes");

            _style = AssetDatabase.LoadAssetAtPath<UiStyle>(UiStyleBuilder.StylePath);
            if (_style == null || _style.Font == null) _style = UiStyleBuilder.Build();
            _strings = File.Exists(LocalizationBuilder.SourcePath)
                ? LocalizationSource.Parse(File.ReadAllText(LocalizationBuilder.SourcePath, System.Text.Encoding.UTF8))
                : null;
            Localized.Clear();

            BuildGameScene();
            BuildBootstrapScene();
            RegisterInBuildSettings();

            Debug.Log($"[Maze] Runtime scenes built: {BootstrapPath} (first in Build Settings), {GamePath}.");
            return null;
        }

        private static void BuildGameScene()
        {
            var scene = NewScene();

            // Top-down camera: frames the level when it is built, follows the player later.
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.depth = 0;
            cameraObject.transform.SetPositionAndRotation(new Vector3(10f, 22f, 4f), Quaternion.Euler(60f, 0f, 0f));
            var topDown = cameraObject.AddComponent<TopDownCamera>();
            SetReference(topDown, "_camera", camera);

            // Weak "moon" without shadows; LevelLighting sets color and intensity from the theme.
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 0.35f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.2f, 0.25f, 0.34f);

            var viewRoot = new GameObject("Level View").AddComponent<LevelViewRoot>();
            SetReference(viewRoot, "_moon", light);

            var scopeObject = new GameObject("LevelLifetimeScope");
            var scope = scopeObject.AddComponent<LevelLifetimeScope>();
            var serializedScope = new SerializedObject(scope);
            serializedScope.FindProperty("autoRun").boolValue = false;
            serializedScope.ApplyModifiedPropertiesWithoutUndo();
            SetReference(scope, "_viewRoot", viewRoot);
            SetReference(scope, "_camera", topDown);

            SaveAndClose(scene, GamePath);
        }

        private static void BuildBootstrapScene()
        {
            var scene = NewScene();

            // Renders the menus while no level is loaded; the Game scene camera draws on top of it.
            var cameraObject = new GameObject("UI Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.cullingMask = 0;
            camera.depth = -10;
            cameraObject.AddComponent<AudioListener>();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var ui = BuildUI();
            SetReference(ui, "_eventSystem", eventSystem.GetComponent<EventSystem>());
            var touch = BuildTouchControls();
            SetReference(ui, "_touchControls", touch);
            SetReferences(ui, "_localizedTexts", Localized.Cast<Object>().ToList());
            SetReference(ui.Hud, "_touchControls", touch);
            LinkTutorialHighlights(ui, touch);

            var scopeObject = new GameObject("ProjectLifetimeScope");
            var scope = scopeObject.AddComponent<ProjectLifetimeScope>();
            SetReference(scope, "_ui", ui);

            SaveAndClose(scene, BootstrapPath);
        }

        private static Scene NewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene); // New GameObjects go to the active scene.
            return scene;
        }

        private static void SaveAndClose(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
        }

        private static UIRoot BuildUI()
        {
            var canvasObject = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            var root = canvasObject.AddComponent<UIRoot>();
            var parent = canvasObject.transform;

            SetReference(root, "_mainMenu", BuildMainMenu(parent));
            SetReference(root, "_levelSelect", BuildLevelSelect(parent));
            SetReference(root, "_loading", BuildLoading(parent));
            SetReference(root, "_hud", BuildHud(parent));
            SetReference(root, "_tutorial", BuildTutorial(parent)); // over the HUD, under the pause and result screens
            SetReference(root, "_pause", BuildPause(parent));
            SetReference(root, "_confirmExit", BuildConfirmExit(parent));
            SetReference(root, "_map", BuildMap(parent));
            SetReference(root, "_result", BuildResult(parent));
            SetReference(root, "_settings", BuildSettings(parent));
            SetReference(root, "_error", BuildError(parent));
            SetReference(root, "_fpsCounter", BuildFpsCounter(parent)); // over every screen
            SetReference(root, "_rotateDevice", BuildRotateDevice(parent)); // last: covers even the FPS counter
            AddUiSounds(root);
            return root;
        }

        /// <summary>
        /// Every button and toggle gets a <see cref="UiSound"/>: a click; Back for back / close / "No"; silent where the
        /// screen it opens sounds by itself (pause, resume, map).
        /// </summary>
        private static void AddUiSounds(UIRoot root)
        {
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                if (!(selectable is Button) && !(selectable is Toggle)) continue;
                var screen = selectable.GetComponentInParent<UIScreen>(true);
                var name = selectable.gameObject.name;
                var kind = UiSoundKind.Click;
                if (screen is HudScreen && (name == "PauseButton" || name == "MapButton")) kind = UiSoundKind.None;
                else if (screen is PauseScreen && name == "ResumeButton") kind = UiSoundKind.None;
                else if (screen is MapScreen && name == "CloseButton") kind = UiSoundKind.None;
                else if (screen is TutorialScreen && name == "ContinueButton") kind = UiSoundKind.None; // resume sound
                else if (name == "BackButton" || name == "NoButton") kind = UiSoundKind.Back;

                var sound = selectable.gameObject.AddComponent<UiSound>();
                sound.Kind = kind;
            }
        }

        /// <summary>
        /// Main menu (mock-up, minimal): the title with an ornament line on the left, PLAY (accent; the next level at
        /// once), LEVELS, TUTORIAL and SETTINGS under it; sign-in at the top right; total stars and kills at the bottom left; debug reset and the version at the bottom right.
        /// </summary>
        private static MainMenuScreen BuildMainMenu(Transform parent)
        {
            var screen = Screen<MainMenuScreen>(parent, "MainMenuScreen", Background);
            Backdrop(screen.transform);
            var icons = UiIcons();
            var area = SafeArea(screen.transform);

            // The game's name is one English word in every language: not localized.
            AutoSize(Text(area, "Title", "MAZEBOUND", 132f, true, TopLeft, TopLeft, new Vector2(160f, -110f),
                new Vector2(1200f, 150f), TextAlignmentOptions.TopLeft, spacing: 22f));
            OrnamentLine(area, TopLeft, new Vector2(172f, -285f), 560f);

            var buttons = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            buttons.SetParent(area, false);
            buttons.anchorMin = buttons.anchorMax = buttons.pivot = TopLeft;
            // High and compact: a site banner (VK) shortens the screen from below.
            buttons.anchoredPosition = new Vector2(168f, -340f);
            buttons.sizeDelta = new Vector2(500f, 0f);
            var layout = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            buttons.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var play = TextButton(buttons, "PlayButton", Loc(TextKeys.MenuPlay), true, icons.ChevronRight, 84f);
            var levels = TextButton(buttons, "LevelsButton", Loc(TextKeys.MenuLevels), false, icons.Menu, 84f);
            var tutorial = TextButton(buttons, "TutorialButton", Loc(TextKeys.MenuTutorial), false, icons.Help, 84f);
            var settings = TextButton(buttons, "SettingsButton", Loc(TextKeys.Settings), false, icons.Settings, 84f);

            // Bottom right, over the version: bottom left the totals ran into the last button on a short screen
            // (VK's frame on a desktop).
            var summary = Row(area, "Summary", new Vector2(1f, 0f), new Vector2(-64f, 96f), 44f, new Vector2(1f, 0f));
            var stars = Counter(summary, "Stars", icons.StarFilled, out var starsText);
            stars.transform.Find("Icon").GetComponent<Image>().color = _style.Accent;
            Counter(summary, "Kills", icons.Skull, out var killsText);

            var reset = LinkButton(area, "DebugResetProgressButton", Loc(TextKeys.MenuResetProgress), new Vector2(1f, 0f),
                new Vector2(-64f, 176f), 380f);
            var version = Text(area, "Version", "v0.1", 24f, true, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-64f, 52f), new Vector2(140f, 36f), TextAlignmentOptions.BottomRight, _style.MutedText, 6f);

            // Signing in to the site's account (Yandex Games), top right; shown by the router only when possible.
            var account = Rect(area, "Account", new Vector2(1f, 1f), new Vector2(-64f, -64f), new Vector2(420f, 140f));
            account.pivot = new Vector2(1f, 1f);
            var signIn = TextButton(account, "SignInButton", Loc(TextKeys.MenuSignIn), false, icons.Player, 72f, 26f);
            var signInRect = signIn.GetComponent<RectTransform>();
            signInRect.anchorMin = new Vector2(0f, 1f);
            signInRect.anchorMax = new Vector2(1f, 1f);
            signInRect.pivot = new Vector2(0.5f, 1f);
            signInRect.anchoredPosition = Vector2.zero;
            signInRect.sizeDelta = new Vector2(0f, 72f);
            var hint = Text(account, "Hint", Loc(TextKeys.MenuSignInHint), 22f, false, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -84f), new Vector2(420f, 56f), TextAlignmentOptions.TopRight, _style.MutedText);
            AutoSize(hint);
            account.gameObject.SetActive(false);

            SetReference(screen, "_playButton", play);
            SetReference(screen, "_levelsButton", levels);
            SetReference(screen, "_tutorialButton", tutorial);
            SetReference(screen, "_settingsButton", settings);
            SetReference(screen, "_summary", summary.gameObject);
            SetReference(screen, "_stars", starsText);
            SetReference(screen, "_kills", killsText);
            SetReference(screen, "_version", version);
            SetReference(screen, "_debugResetProgressButton", reset);
            SetReference(screen, "_account", account.gameObject);
            SetReference(screen, "_signInButton", signIn);
            return screen;
        }

        /// <summary>
        /// Level select (user's spec): back, title and total stars at the top; ten tiles in two rows of five (number,
        /// padlock when locked, three star slots under completed ones); arrows, page diamonds and the range at the
        /// bottom. The screen itself takes horizontal swipes.
        /// </summary>
        private static LevelSelectScreen BuildLevelSelect(Transform parent)
        {
            var screen = Screen<LevelSelectScreen>(parent, "LevelSelectScreen", Background);
            Backdrop(screen.transform);
            var icons = UiIcons();
            var area = SafeArea(screen.transform);

            var back = RoundButton(area, "BackButton", icons.ChevronLeft, TopLeft, new Vector2(100f, -90f), 84f, 36f);
            Text(area, "Title", Loc(TextKeys.LevelSelectTitle), 46f, true, TopLeft, new Vector2(0f, 0.5f), new Vector2(172f, -90f),
                new Vector2(700f, 60f), TextAlignmentOptions.MidlineLeft, spacing: 16f);
            var total = Row(area, "TotalStars", new Vector2(1f, 1f), new Vector2(-64f, -90f), 10f, new Vector2(1f, 0.5f));
            var totalCounter = Counter(total, "Stars", icons.StarFilled, out var totalText);
            totalCounter.transform.Find("Icon").GetComponent<Image>().color = _style.Accent;

            var gridRect = Rect(area, "Grid", Center, new Vector2(0f, 30f), new Vector2(1058f, 414f));
            var gridGroup = gridRect.gameObject.AddComponent<CanvasGroup>();
            var grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(170f, 190f);
            grid.spacing = new Vector2(52f, 34f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;
            var tiles = new List<Object>();
            for (var i = 0; i < LevelPages.PerPage; i++)
                tiles.Add(LevelTileView(gridRect, i, icons));

            var empty = Text(area, "EmptyLabel", Loc(TextKeys.LevelSelectEmpty), 30f, false,
                Center, Center, new Vector2(0f, 30f), new Vector2(1200f, 60f), TextAlignmentOptions.Center, _style.MutedText);
            empty.gameObject.SetActive(false);

            var previous = RoundButton(area, "PreviousButton", icons.ChevronLeft, new Vector2(0.5f, 0f), new Vector2(-250f, 96f), 76f, 32f);
            var next = RoundButton(area, "NextButton", icons.ChevronRight, new Vector2(0.5f, 0f), new Vector2(250f, 96f), 76f, 32f);
            var dots = Row(area, "Dots", new Vector2(0.5f, 0f), new Vector2(-70f, 96f), 18f, new Vector2(0.5f, 0.5f));
            dots.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var dotTemplate = Shape(dots, "DotTemplate", Center, Vector2.zero, new Vector2(13f, 13f));
            dotTemplate.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            dotTemplate.Fill = Color.clear;
            var dotLayout = dotTemplate.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = dotLayout.preferredHeight = 13f;
            var range = Text(area, "Range", "1 - 10", 30f, true, new Vector2(0.5f, 0f), new Vector2(0f, 0.5f),
                new Vector2(40f, 96f), new Vector2(170f, 44f), TextAlignmentOptions.MidlineLeft, spacing: 8f);

            SetReference(screen, "_style", _style);
            SetReference(screen, "_backButton", back);
            SetReference(screen, "_totalStars", totalText);
            SetReferences(screen, "_tiles", tiles);
            SetReference(screen, "_grid", gridGroup);
            SetReference(screen, "_previousButton", previous);
            SetReference(screen, "_nextButton", next);
            SetReference(screen, "_range", range);
            SetReference(screen, "_dots", dots);
            SetReference(screen, "_dotTemplate", dotTemplate);
            SetReference(screen, "_emptyLabel", empty);
            return screen;
        }

        /// <summary>One tile (a grid cell): the button with the number and padlock, the star slots under it.</summary>
        private static LevelTile LevelTileView(RectTransform grid, int index, UiIconSet icons)
        {
            var holder = new GameObject("Tile" + index, typeof(RectTransform), typeof(CanvasGroup));
            holder.transform.SetParent(grid, false);

            var body = Shape(holder.transform, "Button", Center, Vector2.zero, Vector2.zero, true);
            body.CornerRadius = 3f;
            var bodyRect = body.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0.22f);
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = bodyRect.offsetMax = Vector2.zero;
            var number = Text(bodyRect, "Number", (index + 1).ToString(), 72f, true, Center, Center, new Vector2(0f, 10f),
                new Vector2(160f, 90f), TextAlignmentOptions.Center);
            number.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(number, 0.35f); // Development levels show a word instead of the number.
            var padlock = Icon(bodyRect, "Lock", icons.Lock, new Vector2(0.5f, 0f), new Vector2(0f, 26f), 30f);
            var bestTime = Text(bodyRect, "BestTime", "", 22f, false, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(150f, 28f), TextAlignmentOptions.Bottom, _style.MutedText);
            bestTime.textWrappingMode = TextWrappingModes.NoWrap;
            bestTime.gameObject.SetActive(false);
            var button = MakeButton(body, false, number, padlock);

            var stars = new GameObject("Stars", typeof(RectTransform)).GetComponent<RectTransform>();
            stars.SetParent(holder.transform, false);
            stars.anchorMin = Vector2.zero;
            stars.anchorMax = new Vector2(1f, 0.17f);
            stars.offsetMin = stars.offsetMax = Vector2.zero;
            var starsLayout = stars.gameObject.AddComponent<HorizontalLayoutGroup>();
            starsLayout.spacing = 6f;
            starsLayout.childAlignment = TextAnchor.MiddleCenter;
            starsLayout.childControlWidth = starsLayout.childControlHeight = false;
            var starImages = new List<Object>();
            for (var i = 0; i < 3; i++)
                starImages.Add(Icon(stars, "Star" + i, icons.Star, Center, Vector2.zero, 32f));

            var tile = body.gameObject.AddComponent<LevelTile>();
            SetReference(tile, "_style", _style);
            SetReference(tile, "_button", button);
            SetReference(tile, "_number", number);
            SetReference(tile, "_lock", padlock.gameObject);
            SetReference(tile, "_bestTime", bestTime);
            SetReference(tile, "_group", holder.GetComponent<CanvasGroup>());
            SetReference(tile, "_stars", stars.gameObject);
            SetReferences(tile, "_starImages", starImages);
            SetReference(tile, "_star", icons.Star);
            SetReference(tile, "_starFilled", icons.StarFilled);
            return tile;
        }

        private static LoadingScreen BuildLoading(Transform parent)
        {
            var screen = Screen<LoadingScreen>(parent, "LoadingScreen", Background);
            Backdrop(screen.transform);
            Text(screen.transform, "Label", Loc(TextKeys.Loading), 40f, true, Center, Center, new Vector2(0f, 6f), new Vector2(600f, 60f),
                TextAlignmentOptions.Center, spacing: 24f);
            OrnamentLine(screen.transform, Center, new Vector2(-200f, -40f), 400f);
            return screen;
        }

        private static HudScreen BuildHud(Transform parent)
        {
            var screen = Screen<HudScreen>(parent, "HudScreen", Color.clear);
            screen.GetComponent<Image>().raycastTarget = false;
            screen.gameObject.AddComponent<SafeAreaFitter>(); // Notches and rounded corners.

            // First child: under the texts. Its texture is made by the screen, it is switched on only while pulsing.
            var damageObject = new GameObject("DamageFlash", typeof(RectTransform));
            damageObject.transform.SetParent(screen.transform, false);
            Stretch(damageObject.GetComponent<RectTransform>());
            var damageFlash = damageObject.AddComponent<RawImage>();
            damageFlash.raycastTarget = false;
            damageFlash.enabled = false;

            var icons = UiIcons();
            var root = screen.transform;

            // Top left: heart ring, level name and HP number, the arrow-tipped bar.
            var heartRing = Shape(root, "HeartRing", TopLeft, new Vector2(82f, -78f), new Vector2(84f, 84f));
            heartRing.Kind = UiShapeKind.Capsule;
            heartRing.Fill = Fade(_style.Fill, 0.5f / _style.Fill.a);
            var heartFill = Icon(heartRing.transform, "HeartFill", icons.HeartFill, Center, Vector2.zero, 42f);
            heartFill.color = _style.Health;
            Icon(heartRing.transform, "Heart", icons.Heart, Center, Vector2.zero, 42f);

            var levelName = Text(root, "LevelName", "LEVEL", 30f, true, TopLeft, TopLeft, new Vector2(146f, -36f),
                new Vector2(330f, 38f), TextAlignmentOptions.BottomLeft, spacing: 10f);
            // Top centre: the play time (between the HP bar and Map, also at 4:3).
            var timer = Text(root, "Timer", "0:00", 34f, true, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -56f), new Vector2(180f, 44f), TextAlignmentOptions.Center, spacing: 4f);
            timer.textWrappingMode = TextWrappingModes.NoWrap;
            var health = Text(root, "Health", "100 / 100", 28f, false, TopLeft, new Vector2(1f, 1f), new Vector2(622f, -36f),
                new Vector2(160f, 38f), TextAlignmentOptions.BottomRight);

            var frame = Shape(root, "HealthFrame", TopLeft, new Vector2(146f + 238f, -94f), new Vector2(476f, 16f));
            frame.Kind = UiShapeKind.Arrow;
            frame.CornerRadius = 13f;
            frame.Fill = Color.clear;
            frame.StrokeWidth = _style.ThinStroke + 0.5f;
            var bar = Rect(frame.transform, "Bar", Center, Vector2.zero, Vector2.zero);
            bar.anchorMin = Vector2.zero;
            bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(4f, 4f);
            bar.offsetMax = new Vector2(-15f, -4f);
            var trail = BarFill(bar, "Trail", Fade(_style.Text, 0.6f));
            var fill = BarFill(bar, "Fill", _style.Health);

            // Under it: fragments, zombies killed, keys.
            var stats = Row(root, "Stats", TopLeft, new Vector2(52f, -128f), 30f);
            var fragments = Counter(stats, "Fragments", icons.Fragment, out var fragmentsText);
            var kills = Counter(stats, "Kills", icons.Skull, out var killsText);
            var keys = new GameObject("Keys", typeof(RectTransform));
            keys.transform.SetParent(stats, false);
            var keysLayout = keys.AddComponent<HorizontalLayoutGroup>();
            keysLayout.spacing = 6f;
            keysLayout.childControlWidth = keysLayout.childControlHeight = true;
            keysLayout.childForceExpandWidth = keysLayout.childForceExpandHeight = false;
            var keyTemplate = new GameObject("KeyTemplate", typeof(RectTransform)).AddComponent<Image>();
            keyTemplate.transform.SetParent(keys.transform, false);
            keyTemplate.sprite = icons.Key;
            keyTemplate.preserveAspect = true;
            keyTemplate.raycastTarget = false;
            var keyLayout = keyTemplate.gameObject.AddComponent<LayoutElement>();
            keyLayout.preferredWidth = keyLayout.preferredHeight = 36f;
            keyTemplate.gameObject.SetActive(false); // Cloned per carried key.

            // Top centre: the message between diamonds.
            var messageRow = Row(root, "MessageRow", new Vector2(0.5f, 1f), new Vector2(0f, -176f), 14f, new Vector2(0.5f, 1f));
            messageRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Ornament(messageRow, false);
            var message = Text(messageRow, "Message", "", 32f, false, Center, Center, Vector2.zero, new Vector2(10f, 40f),
                TextAlignmentOptions.Center);
            message.textWrappingMode = TextWrappingModes.NoWrap;
            var messageFitter = message.gameObject.AddComponent<LayoutElement>();
            messageFitter.minHeight = 40f;
            Ornament(messageRow, true);
            messageRow.gameObject.SetActive(false); // Shown with a message.

            // Top right: Map and Pause.
            // Spaced so that their widened touch areas (RoundButton) meet but do not overlap.
            var pause = RoundButton(root, "PauseButton", icons.Pause, new Vector2(1f, 1f), new Vector2(-82f, -78f), 84f, 40f);
            var map = RoundButton(root, "MapButton", icons.Map, new Vector2(1f, 1f), new Vector2(-214f, -78f), 84f, 40f);

            // Bottom right (no on-screen controls): weapon slots, keys 1 and 2.
            var weaponPanel = Rect(root, "WeaponPanel", new Vector2(1f, 0f), new Vector2(-40f - 158f, 40f + 55f),
                new Vector2(316f, 110f));
            var meleeSlot = WeaponSlot(weaponPanel, "MeleeSlot", new Vector2(-79f, 0f), "1", icons.Melee);
            var rangedSlot = WeaponSlot(weaponPanel, "RangedSlot", new Vector2(79f, 0f), "2", icons.Ranged, "R");

            SetReference(screen, "_style", _style);
            SetReference(screen, "_levelName", levelName);
            SetReference(screen, "_timer", timer);
            SetReference(screen, "_healthText", health);
            SetReference(screen, "_healthFrame", frame);
            SetReference(screen, "_heartRing", heartRing);
            SetReference(screen, "_healthFill", fill);
            SetReference(screen, "_healthTrail", trail);
            SetReference(screen, "_fragments", fragments);
            SetReference(screen, "_fragmentsText", fragmentsText);
            SetReference(screen, "_kills", kills);
            SetReference(screen, "_killsText", killsText);
            SetReference(screen, "_keys", keys.GetComponent<RectTransform>());
            SetReference(screen, "_keyTemplate", keyTemplate);
            SetReference(screen, "_messageRow", messageRow.gameObject);
            SetReference(screen, "_message", message);
            SetReference(screen, "_pauseButton", pause);
            SetReference(screen, "_mapButton", map);
            SetReference(screen, "_weaponPanel", weaponPanel.gameObject);
            SetReference(screen, "_meleeSlot", meleeSlot);
            SetReference(screen, "_rangedSlot", rangedSlot);
            SetReference(screen, "_damageFlash", damageFlash);
            return screen;
        }

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);

        /// <summary>A bar part filling its parent from the left; the screen sets its right anchor.</summary>
        private static RectTransform BarFill(RectTransform bar, string name, Color color)
        {
            var shape = Shape(bar, name, Center, Vector2.zero, Vector2.zero);
            shape.StrokeWidth = 0f;
            shape.Fill = color;
            var rect = shape.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>Icon and a number, side by side (in a row layout).</summary>
        private static GameObject Counter(Transform row, string name, Sprite icon, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(row, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var image = Icon(go.transform, "Icon", icon, Center, Vector2.zero, 34f);
            var imageLayout = image.gameObject.AddComponent<LayoutElement>();
            imageLayout.preferredWidth = imageLayout.preferredHeight = 34f;
            text = Text(go.transform, "Text", "0/0", 28f, false, Center, Center, Vector2.zero, new Vector2(10f, 34f),
                TextAlignmentOptions.MidlineLeft);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return go;
        }

        /// <summary>A line and a diamond (mirrored after the text when <paramref name="after"/>).</summary>
        private static void Ornament(Transform row, bool after)
        {
            var line = Shape(row, after ? "LineAfter" : "LineBefore", Center, Vector2.zero, new Vector2(96f, 1.5f));
            line.StrokeWidth = 0f;
            line.Fill = Fade(_style.Line, 0.6f);
            var lineLayout = line.gameObject.AddComponent<LayoutElement>();
            lineLayout.preferredWidth = 96f;
            lineLayout.preferredHeight = 1.5f;

            var holder = new GameObject(after ? "DiamondAfter" : "DiamondBefore", typeof(RectTransform));
            holder.transform.SetParent(row, false);
            var holderLayout = holder.AddComponent<LayoutElement>();
            holderLayout.preferredWidth = holderLayout.preferredHeight = 16f;
            var diamond = Shape(holder.transform, "Diamond", Center, Vector2.zero, new Vector2(11f, 11f));
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            diamond.Fill = Color.clear;
            diamond.Stroke = _style.Accent;
            if (after) line.transform.SetAsLastSibling();
        }

        /// <summary>A row of children laid out left to right, sized by its content.</summary>
        private static Transform Row(Transform parent, string name, Vector2 anchor, Vector2 position, float spacing,
            Vector2? pivot = null)
        {
            var rect = Rect(parent, name, anchor, position, new Vector2(10f, 40f));
            rect.pivot = pivot ?? new Vector2(0f, 1f);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static HudWeaponSlot WeaponSlot(Transform parent, string name, Vector2 position, string hint, Sprite fallback,
            string reloadHint = null)
        {
            var frame = Shape(parent, name, Center, position, new Vector2(150f, 110f));
            frame.CornerRadius = 3f;
            var icon = Icon(frame.transform, "Icon", fallback, Center, new Vector2(0f, 8f), 84f);
            var ammo = Text(frame.transform, "Ammo", "", 24f, false, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-10f, 8f), new Vector2(110f, 28f), TextAlignmentOptions.BottomRight);
            ammo.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(ammo, 0.6f);
            var hintText = Text(frame.transform, "Hint", hint, 22f, true, TopLeft, TopLeft, new Vector2(10f, -6f),
                new Vector2(30f, 26f), TextAlignmentOptions.TopLeft);
            var reload = BarFill((RectTransform)frame.transform, "Reload", _style.Accent);
            reload.offsetMin = new Vector2(6f, 4f);
            reload.anchorMax = new Vector2(0f, 0f);
            reload.offsetMax = new Vector2(0f, 7f);
            reload.gameObject.SetActive(false);

            var slot = frame.gameObject.AddComponent<HudWeaponSlot>();
            SetReference(slot, "_style", _style);
            SetReference(slot, "_frame", frame);
            SetReference(slot, "_icon", icon);
            SetReference(slot, "_fallbackIcon", fallback);
            SetReference(slot, "_ammo", ammo);
            SetReference(slot, "_hint", hintText);
            if (reloadHint != null)
            {
                var reloadText = Text(frame.transform, "ReloadHint", reloadHint, 22f, true, Vector2.one, Vector2.one,
                    new Vector2(-10f, -6f), new Vector2(30f, 26f), TextAlignmentOptions.TopRight);
                reloadText.enabled = false;
                SetReference(slot, "_reloadHint", reloadText);
            }
            SetReference(slot, "_reload", reload);
            return slot;
        }

        /// <summary>A caption by key (<see cref="TextKeys"/>) for any helper taking a text: see <see cref="Text"/>.</summary>
        private static string Loc(string key) => KeyMark + key;

        /// <summary>
        /// Text placed by anchor and pivot (no layout). A <see cref="Loc"/> text gets the base-language text from the
        /// translation table and a <see cref="LocalizedText"/> with its key (listed in <see cref="UIRoot"/>).
        /// </summary>
        private static TMP_Text Text(Transform parent, string name, string text, float size, bool bold, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 box, TextAlignmentOptions alignment, Color? color = null,
            float spacing = 0f)
        {
            string key = null;
            if (!string.IsNullOrEmpty(text) && text[0] == KeyMark)
            {
                key = text.Substring(1);
                text = _strings?.Find(key, _strings.Languages[0]) ?? key;
            }

            var rect = Rect(parent, name, anchor, position, box);
            rect.pivot = pivot;
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = bold ? _style.BoldFont : _style.Font;
            label.text = text;
            if (key != null)
            {
                var localized = rect.gameObject.AddComponent<LocalizedText>();
                var serialized = new SerializedObject(localized);
                serialized.FindProperty("_key").stringValue = key;
                serialized.FindProperty("_text").objectReferenceValue = label;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Localized.Add(localized);
            }

            label.fontSize = size;
            label.characterSpacing = spacing;
            label.alignment = alignment;
            label.color = color ?? _style.Text;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// Long translations shrink the text down to <paramref name="min"/> of its size instead of overflowing its box
        /// (TextMeshPro re-fits only when the text changes).
        /// </summary>
        private static TMP_Text AutoSize(TMP_Text label, float min = 0.7f)
        {
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Round(label.fontSize * min);
            return label;
        }

        /// <summary>
        /// Round icon button (HUD, back buttons). Touches count up to <paramref name="hitPadding"/> outside the circle:
        /// on a phone the 84-unit buttons are ~5 mm across, smaller than a fingertip.
        /// </summary>
        private static UiButton RoundButton(Transform parent, string name, Sprite icon, Vector2 anchor, Vector2 position,
            float diameter, float iconSize, float hitPadding = 24f)
        {
            var body = Shape(parent, name, anchor, position, new Vector2(diameter, diameter), true);
            body.Kind = UiShapeKind.Capsule;
            body.HitPadding = hitPadding;
            var image = Icon(body.transform, "Icon", icon, Center, Vector2.zero, iconSize);
            return MakeButton(body, false, image);
        }

        /// <summary>Turns a shape into a <see cref="UiButton"/> painting it and its content by state.</summary>
        private static UiButton MakeButton(UiShape body, bool accent, params Graphic[] content)
        {
            body.raycastTarget = true;
            var button = body.gameObject.AddComponent<UiButton>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = body;
            SetReference(button, "_style", _style);
            SetReference(button, "_body", body);
            SetReferences(button, "_content", content.Cast<Object>().ToList());
            var serialized = new SerializedObject(button);
            serialized.FindProperty("_accent").boolValue = accent;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            button.Repaint();
            return button;
        }

        /// <summary>
        /// ТЗ §64 Android: a separate overlay canvas under the application UI (screens with a dimmed background cover
        /// it), shown by the HUD. Units are 0.1 mm (see <see cref="TouchControls"/>), inside the safe area.
        /// Stick zone on the left part of the screen (the stick floats there or rests near its corner); attack
        /// bottom-right under the thumb, use above it, melee and ranged to its left; map and pause are HUD buttons at
        /// the top. Everything is mirrored for the left-handed layout. They emulate gamepad controls bound by InputService.
        /// The player may move and resize the stick and buttons (TouchLayoutHandle on each, disabled) in the layout
        /// editor: a dim background under them and a toolbar at the top, both hidden until editing.
        /// </summary>
        private static TouchControls BuildTouchControls()
        {
            var go = new GameObject("TouchControls", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1;
            go.AddComponent<GraphicRaycaster>();
            var controls = go.AddComponent<TouchControls>();

            // Layout editing: under the controls, over the application UI (the canvas goes on top then).
            var editBackground = new GameObject("EditBackground", typeof(RectTransform));
            editBackground.transform.SetParent(go.transform, false);
            Stretch(editBackground.GetComponent<RectTransform>());
            editBackground.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            editBackground.SetActive(false);

            var content = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(go.transform, false);
            Stretch(content);
            content.gameObject.AddComponent<SafeAreaFitter>();
            var group = content.gameObject.AddComponent<CanvasGroup>(); // Opacity: not the editor toolbar.

            var zone = new GameObject("StickZone", typeof(RectTransform)).GetComponent<RectTransform>();
            zone.SetParent(content, false);
            zone.anchorMin = Vector2.zero;
            zone.anchorMax = new Vector2(0.45f, 0.72f);
            zone.pivot = Vector2.zero;
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            zone.gameObject.AddComponent<Image>().color = Color.clear; // Catches touches, draws nothing visible.

            // Stick: thin ring, an inner hairline, the knob, aim-mode diamonds around (TouchStickMarks).
            var ringShape = Shape(zone, "Ring", Vector2.zero, new Vector2(230f, 230f), new Vector2(240f, 240f), true);
            ringShape.Kind = UiShapeKind.Capsule;
            ringShape.Fill = Fade(_style.Fill, 0.35f / _style.Fill.a);
            var ring = ringShape.rectTransform; // Its touches go up to the zone (the stick) or a layout handle.
            var hairline = SectorShape(ring, "Hairline", Center, Vector2.zero, 106.5f, 108f, 0f, 360f);
            hairline.StrokeWidth = 0f;
            hairline.Fill = Fade(_style.Line, 0.25f);
            var knobShape = Shape(ring, "Knob", Center, Vector2.zero, new Vector2(92f, 92f));
            knobShape.Kind = UiShapeKind.Capsule;
            knobShape.Fill = _style.PressedFill;
            var knob = knobShape.rectTransform;
            var marks = ring.gameObject.AddComponent<TouchStickMarks>();
            var markShapes = new List<Object>();
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45f * Mathf.Deg2Rad;
                var mark = Shape(ring, "Mark" + i, Center, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 136f,
                    new Vector2(12f, 12f));
                mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                mark.Fill = Color.clear;
                markShapes.Add(mark);
            }

            SetReference(marks, "_style", _style);
            SetReferences(marks, "_marks", markShapes);

            var stick = zone.gameObject.AddComponent<TouchStick>();
            SetReference(stick, "_ring", ring);
            SetReference(stick, "_knob", knob);
            SetReference(stick, "_marks", marks);
            var serializedStick = new SerializedObject(stick);
            serializedStick.FindProperty("_restFromCorner").vector2Value = new Vector2(230f, 230f);
            serializedStick.FindProperty("_radius").floatValue = 100f;
            serializedStick.ApplyModifiedPropertiesWithoutUndo();

            var (cluster, clusterRoot, use) = BuildAttackCluster(content);

            // In TouchElement order (Melee and Ranged are part of the Attack cluster).
            var mirrored = new List<RectTransform> { zone, clusterRoot, use };

            var editor = BuildTouchLayoutEditor(go.transform, controls, editBackground);
            AddLayoutHandle(ring.gameObject, TouchElement.Stick, editor);
            AddLayoutHandle(clusterRoot.gameObject, TouchElement.Attack, editor);
            AddLayoutHandle(use.gameObject, TouchElement.Use, editor);

            SetReference(controls, "_canvas", canvas);
            SetReference(controls, "_group", group);
            SetReference(controls, "_stick", stick);
            SetReference(controls, "_area", content);
            SetReference(controls, "_editor", editor);
            SetReference(controls, "_cluster", cluster);
            SetReference(controls, "_marks", marks);
            SetReferences(controls, "_mirrored", mirrored.Cast<Object>().ToList());
            go.SetActive(false); // The HUD shows it on touch devices.
            return controls;
        }

        private static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);

        /// <summary>
        /// The attack cluster (design mock-up): Attack is a quarter circle (radius 175, 0.1 mm units) with its right
        /// angle at the cluster's bottom-right corner, 4 mm from the safe area's corner; Ranged (top) and Melee (left)
        /// are ring sectors 190–270 along its arc with magazine ticks outside and the reload arc inside; Use is a circle
        /// apart on the left. Content (shapes, icons) is anchored at the corner and mirrored for left-handers.
        /// </summary>
        private static (TouchAttackCluster Cluster, RectTransform Root, RectTransform Use) BuildAttackCluster(RectTransform area)
        {
            const float radius = 175f, inner = 190f, outer = 270f, rangedStart = 93f, meleeStart = 137f, sweep = 40f;
            const float tickRadius = 284f, size = 300f;
            var icons = UiIcons();

            var root = Rect(area, "AttackCluster", BottomRight, new Vector2(-40f - size * 0.5f, 40f + size * 0.5f),
                new Vector2(size, size));
            var content = Rect(root, "Content", Center, Vector2.zero, Vector2.zero);
            Stretch(content);

            var attack = SectorShape(content, "AttackButton", BottomRight, Vector2.zero, 0f, radius, 90f, 90f, true);
            AddTouchButton(attack.gameObject, "<Gamepad>/buttonSouth");
            var arc = SectorShape(content, "AttackHairline", BottomRight, Vector2.zero, radius - 11.5f, radius - 10f, 90f, 90f);
            arc.StrokeWidth = 0f;
            arc.Fill = Fade(_style.Line, 0.25f);
            Icon(content, "AttackIcon", icons.Attack, BottomRight, new Vector2(-74f, 74f), 62f);

            var ranged = SectorShape(content, "RangedButton", BottomRight, Vector2.zero, inner, outer, rangedStart, sweep, true);
            AddTouchButton(ranged.gameObject, "<Gamepad>/rightShoulder");
            var melee = SectorShape(content, "MeleeButton", BottomRight, Vector2.zero, inner, outer, meleeStart, sweep, true);
            AddTouchButton(melee.gameObject, "<Gamepad>/leftShoulder");
            var rangedIcon = Icon(content, "RangedIcon", icons.Ranged, BottomRight, Polar(rangedStart + sweep * 0.5f, 230f), 86f);
            var meleeIcon = Icon(content, "MeleeIcon", icons.Melee, BottomRight, Polar(meleeStart + sweep * 0.5f, 230f), 80f);

            var reload = SectorShape(content, "Reload", BottomRight, Vector2.zero, 180f, 184.5f, rangedStart, sweep);
            reload.StrokeWidth = 0f;
            reload.Fill = _style.Accent;
            reload.enabled = false;

            var ticks = new List<Object>();
            for (var i = 0; i < 12; i++)
            {
                var tick = Shape(content, "Tick" + i, BottomRight, Vector2.zero, new Vector2(3.5f, 13f));
                tick.StrokeWidth = 0f;
                tick.gameObject.SetActive(false);
                ticks.Add(tick);
            }

            // Use: a circle on the left of the cluster, its own layout element.
            // Left of the Melee slot, clear of its icon also when the controls are scaled up.
            var useShape = Shape(area, "UseButton", BottomRight, new Vector2(-430f, 140f), new Vector2(110f, 110f), true);
            useShape.Kind = UiShapeKind.Capsule;
            AddTouchButton(useShape.gameObject, "<Gamepad>/buttonWest");
            var useIcon = Icon(useShape.transform, "Icon", icons.Door, Center, Vector2.zero, 54f);

            var cluster = root.gameObject.AddComponent<TouchAttackCluster>();
            SetReference(cluster, "_style", _style);
            SetReference(cluster, "_content", content);
            SetReference(cluster, "_attack", attack);
            SetReference(cluster, "_melee", melee);
            SetReference(cluster, "_ranged", ranged);
            SetReference(cluster, "_meleeIcon", meleeIcon);
            SetReference(cluster, "_rangedIcon", rangedIcon);
            SetReference(cluster, "_reload", reload);
            SetReferences(cluster, "_ticks", ticks);
            SetReference(cluster, "_use", useShape);
            SetReference(cluster, "_useIcon", useIcon);
            SetReference(cluster, "_doorIcon", icons.Door);
            SetReference(cluster, "_pickUpIcon", icons.PickUp);
            SetReference(cluster, "_meleeIconFallback", icons.Melee);
            SetReference(cluster, "_rangedIconFallback", icons.Ranged);
            var serialized = new SerializedObject(cluster);
            serialized.FindProperty("_center").vector2Value = Vector2.zero;
            serialized.FindProperty("_tickRadius").floatValue = tickRadius;
            serialized.FindProperty("_rangedStart").floatValue = rangedStart;
            serialized.FindProperty("_rangedSweep").floatValue = sweep;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return (cluster, root, useShape.rectTransform);
        }

        private static void AddTouchButton(GameObject target, string controlPath)
        {
            target.AddComponent<OnScreenButton>().controlPath = controlPath;
            target.AddComponent<TouchPress>();
        }

        private static Vector2 Polar(float degrees, float distance)
        {
            var a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * distance;
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private static UiIconSet UiIcons()
        {
            var icons = AssetDatabase.LoadAssetAtPath<UiIconSet>(UiIconsBuilder.SetPath);
            return icons != null ? icons : UiIconsBuilder.Build();
        }

        /// <summary>A shape in the standard look (dark fill, light outline); clicks only when <paramref name="raycast"/>.</summary>
        private static UiShape Shape(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size,
            bool raycast = false)
        {
            var rect = Rect(parent, name, anchor, position, size);
            var shape = rect.gameObject.AddComponent<UiShape>();
            _style.Paint(shape);
            shape.raycastTarget = raycast;
            return shape;
        }

        /// <summary>A sector whose centre is at <paramref name="center"/> from the anchor; its rect is its bounds.</summary>
        private static UiShape SectorShape(Transform parent, string name, Vector2 anchor, Vector2 center, float inner,
            float outer, float start, float sweep, bool raycast = false)
        {
            var bounds = UiShapeMath.SectorBounds(inner, outer, start, sweep);
            var shape = Shape(parent, name, anchor, center + bounds.center, bounds.size, raycast);
            shape.SetSector(new Vector2(-bounds.xMin / bounds.width, -bounds.yMin / bounds.height), inner, outer, start, sweep);
            return shape;
        }

        private static Image Icon(Transform parent, string name, Sprite sprite, Vector2 anchor, Vector2 position, float size)
        {
            var rect = Rect(parent, name, anchor, position, new Vector2(size, size));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = _style.Text;
            return image;
        }

        private static void AddLayoutHandle(GameObject target, TouchElement element, TouchLayoutEditor editor)
        {
            var handle = target.AddComponent<TouchLayoutHandle>();
            var serialized = new SerializedObject(handle);
            serialized.FindProperty("_element").enumValueIndex = (int)element;
            serialized.FindProperty("_editor").objectReferenceValue = editor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            handle.enabled = false; // TouchControls enables it while editing.
        }

        /// <summary>
        /// Toolbar of the layout editor at the top of the safe area (canvas units are 0.1 mm): hint, size of the selected
        /// control, Reset layout, Done.
        /// </summary>
        private static TouchLayoutEditor BuildTouchLayoutEditor(Transform parent, TouchControls controls, GameObject background)
        {
            var panel = new GameObject("LayoutEditor", typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            Stretch(panel.GetComponent<RectTransform>());
            panel.AddComponent<SafeAreaFitter>();

            // Accent frame around the selected control; placed by the editor every frame.
            var selection = Shape(panel.transform, "Selection", Center, Vector2.zero, new Vector2(100f, 100f));
            selection.Fill = Color.clear;
            selection.Stroke = _style.Accent;
            selection.StrokeWidth = 3f;
            selection.CornerRadius = 10f;

            var bar = PanelBox(panel.transform, "Toolbar", new Vector2(0.5f, 1f), new Vector2(0f, -12f), 660f,
                new RectOffset(22, 22, 14, 16), 8f);

            // Two lines for longer translations.
            AutoSize(Text(bar, "Hint", Loc(TextKeys.LayoutHint), 22f, false, Center, Center, Vector2.zero,
                new Vector2(600f, 56f), TextAlignmentOptions.Center, _style.MutedText), 0.8f).gameObject
                .AddComponent<LayoutElement>().preferredHeight = 56f;

            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(bar, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().preferredHeight = 72f;

            var sizeColumn = new GameObject("Size", typeof(RectTransform));
            sizeColumn.transform.SetParent(row.transform, false);
            var sizeLayout = sizeColumn.AddComponent<VerticalLayoutGroup>();
            sizeLayout.childControlWidth = true;
            sizeLayout.childControlHeight = true;
            sizeLayout.childForceExpandHeight = false;
            sizeColumn.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var sizeLabel = Text(sizeColumn.transform, "SizeLabel", "Size", 22f, true, Center, Center, Vector2.zero,
                new Vector2(200f, 28f), TextAlignmentOptions.MidlineLeft, spacing: 2f);
            sizeLabel.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(sizeLabel);
            sizeLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            var size = StyledSlider(sizeColumn.transform, "SizeSlider", 300f, 34f);
            size.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var reset = TextButton(row.transform, "ResetLayoutButton", Loc(TextKeys.LayoutReset), false, null, 60f, 22f);
            var done = TextButton(row.transform, "DoneButton", Loc(TextKeys.LayoutDone), true, null, 60f, 22f);
            foreach (var button in new[] { reset, done })
                button.GetComponent<LayoutElement>().preferredWidth = 140f;

            var editor = panel.AddComponent<TouchLayoutEditor>();
            SetReference(editor, "_controls", controls);
            SetReference(editor, "_selection", selection.rectTransform);
            SetReference(editor, "_background", background);
            SetReference(editor, "_size", size);
            SetReference(editor, "_sizeLabel", sizeLabel);
            SetReference(editor, "_resetButton", reset);
            SetReference(editor, "_doneButton", done);
            panel.SetActive(false);
            return editor;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }


        private static ConfirmExitScreen BuildConfirmExit(Transform parent)
        {
            var screen = Screen<ConfirmExitScreen>(parent, "ConfirmExitScreen", Dim);
            var panel = PanelBox(screen.transform, "Panel", Center, Vector2.zero, 660f, new RectOffset(56, 56, 52, 56), 34f);
            SetReference(screen.GetComponent<UiAppear>(), "_panel", panel);
            Heading(panel, "Title", Loc(TextKeys.ConfirmExitTitle), 54f);
            var row = ButtonRow(panel, "Buttons", 20f, 86f);
            var no = TextButton(row, "NoButton", Loc(TextKeys.No), false, null, 86f);
            var yes = TextButton(row, "YesButton", Loc(TextKeys.Yes), true, null, 86f);

            SetReference(screen, "_yesButton", yes);
            SetReference(screen, "_noButton", no);
            return screen;
        }

        private static PauseScreen BuildPause(Transform parent)
        {
            var screen = Screen<PauseScreen>(parent, "PauseScreen", Dim);
            var icons = UiIcons();
            var panel = PanelBox(screen.transform, "Panel", Center, Vector2.zero, 660f, new RectOffset(56, 56, 48, 56), 18f);
            SetReference(screen.GetComponent<UiAppear>(), "_panel", panel);
            Heading(panel, "Title", Loc(TextKeys.PauseTitle), 58f);
            var resume = TextButton(panel, "ResumeButton", Loc(TextKeys.PauseResume), true, icons.Resume, 86f);
            var retry = TextButton(panel, "RetryButton", Loc(TextKeys.PauseRestart), false, icons.Retry, 86f);
            var settings = TextButton(panel, "SettingsButton", Loc(TextKeys.Settings), false, icons.Settings, 86f);
            var exit = TextButton(panel, "ExitButton", Loc(TextKeys.PauseExit), false, icons.Exit, 86f);

            var debugGroup = ButtonRow(panel, "DebugGroup", 14f, 56f);
            var complete = TextButton(debugGroup, "DebugCompleteButton", Loc(TextKeys.PauseDebugComplete), false, null, 56f, 20f);
            var fail = TextButton(debugGroup, "DebugFailButton", Loc(TextKeys.PauseDebugFail), false, null, 56f, 20f);

            SetReference(screen, "_resumeButton", resume);
            SetReference(screen, "_retryButton", retry);
            SetReference(screen, "_settingsButton", settings);
            SetReference(screen, "_exitButton", exit);
            SetReference(screen, "_debugGroup", debugGroup.gameObject);
            SetReference(screen, "_debugCompleteButton", complete);
            SetReference(screen, "_debugFailButton", fail);
            return screen;
        }

        /// <summary>
        /// Result (mock-up): the title and level name, three star slots with what each is for, a hairline, then Menu,
        /// Retry and Next (shown after a win when there is a next level).
        /// </summary>
        private static ResultScreen BuildResult(Transform parent)
        {
            var screen = Screen<ResultScreen>(parent, "ResultScreen", Dim);
            var icons = UiIcons();
            var panel = PanelBox(screen.transform, "Panel", Center, Vector2.zero, 940f, new RectOffset(64, 64, 52, 56), 24f);
            SetReference(screen.GetComponent<UiAppear>(), "_panel", panel);
            var title = Heading(panel, "Title", "LEVEL COMPLETE", 62f);
            var levelName = Text(panel, "LevelName", "", 28f, true, Center, Center, Vector2.zero, new Vector2(600f, 34f),
                TextAlignmentOptions.Center, _style.MutedText, 10f);
            levelName.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var starsRow = new GameObject("Stars", typeof(RectTransform));
            starsRow.transform.SetParent(panel, false);
            var starsLayout = starsRow.AddComponent<HorizontalLayoutGroup>();
            starsLayout.spacing = 40f;
            starsLayout.childAlignment = TextAnchor.MiddleCenter;
            starsLayout.childControlWidth = starsLayout.childControlHeight = true;
            starsLayout.childForceExpandWidth = starsLayout.childForceExpandHeight = false;
            starsRow.AddComponent<LayoutElement>().preferredHeight = 170f;
            var stars = new List<Object>();
            var labels = new List<Object>();
            for (var i = 0; i < 3; i++)
            {
                var column = new GameObject("Star" + i, typeof(RectTransform));
                column.transform.SetParent(starsRow.transform, false);
                var columnLayout = column.AddComponent<VerticalLayoutGroup>();
                columnLayout.spacing = 14f;
                columnLayout.childAlignment = TextAnchor.UpperCenter;
                columnLayout.childControlWidth = columnLayout.childControlHeight = true;
                columnLayout.childForceExpandWidth = columnLayout.childForceExpandHeight = false;
                var columnElement = column.AddComponent<LayoutElement>();
                // (940 − 2 × 64 padding − 2 × 40 spacing) / 3: the label has this width whatever its text.
                columnElement.preferredWidth = 244f;
                var holder = new GameObject("Holder", typeof(RectTransform));
                holder.transform.SetParent(column.transform, false);
                var holderLayout = holder.AddComponent<LayoutElement>();
                holderLayout.preferredWidth = holderLayout.preferredHeight = 108f;
                stars.Add(Icon(holder.transform, "Icon", icons.Star, Center, Vector2.zero, 108f));
                var label = Text(column.transform, "Label", "", 26f, true, Center, Center, Vector2.zero, new Vector2(230f, 34f),
                    TextAlignmentOptions.Center, spacing: 6f);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                AutoSize(label);
                var labelElement = label.gameObject.AddComponent<LayoutElement>();
                labelElement.preferredHeight = 34f;
                labelElement.preferredWidth = 244f;
                labels.Add(label);
            }

            var time = Text(panel, "Time", "", 28f, true, Center, Center, Vector2.zero, new Vector2(700f, 36f),
                TextAlignmentOptions.Center, _style.MutedText, 6f);
            time.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(time);
            time.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            Hairline(panel, 0.2f);
            var row = ButtonRow(panel, "Buttons", 18f, 86f);
            var menu = TextButton(row, "MenuButton", Loc(TextKeys.ResultMenu), false, icons.Menu, 86f);
            var retry = TextButton(row, "RetryButton", Loc(TextKeys.ResultRetry), false, icons.Retry, 86f);
            var next = TextButton(row, "NextButton", Loc(TextKeys.ResultNext), true, icons.ChevronRight, 86f);

            SetReference(screen, "_style", _style);
            SetReference(screen, "_title", title);
            SetReference(screen, "_levelName", levelName);
            SetReference(screen, "_starsRow", starsRow);
            SetReferences(screen, "_stars", stars);
            SetReferences(screen, "_starLabels", labels);
            SetReference(screen, "_time", time);
            SetReference(screen, "_star", icons.Star);
            SetReference(screen, "_starFilled", icons.StarFilled);
            SetReference(screen, "_retryButton", retry);
            SetReference(screen, "_menuButton", menu);
            SetReference(screen, "_nextButton", next);
            return screen;
        }

        /// <summary>
        /// Tutorial layer (over the HUD): the reminder line at the top centre under the HUD message, the arrow (tip at its
        /// pivot, pointing right; the screen turns it), the highlight ring, and the step window — dim, panel, title,
        /// text, Continue — shown only while the flow waits for Continue. Only the window catches touches.
        /// </summary>
        private static TutorialScreen BuildTutorial(Transform parent)
        {
            var screen = Screen<TutorialScreen>(parent, "TutorialScreen", Color.clear);
            screen.GetComponent<Image>().raycastTarget = false;
            var icons = UiIcons();
            var root = screen.transform;

            var hintBox = Shape(root, "HintRow", new Vector2(0.5f, 1f), new Vector2(0f, -236f), new Vector2(1040f, 84f));
            hintBox.rectTransform.pivot = new Vector2(0.5f, 1f);
            hintBox.Fill = new Color(0.055f, 0.051f, 0.047f, 0.82f);
            hintBox.Stroke = Fade(_style.Accent, 0.8f);
            hintBox.CornerRadius = 2f;
            var hint = Text(hintBox.transform, "Hint", "", 30f, true, Center, Center, Vector2.zero, Vector2.zero,
                TextAlignmentOptions.Center, spacing: 2f);
            Stretch(hint.rectTransform);
            hint.rectTransform.offsetMin = new Vector2(28f, 6f);
            hint.rectTransform.offsetMax = new Vector2(-28f, -6f);
            hint.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(hint, 0.65f);
            hintBox.gameObject.SetActive(false);

            // Arrow: a shaft and a triangular head (an Arrow shape whose tip is its whole length), accent with a dark rim.
            var pointer = Rect(root, "Pointer", Center, Vector2.zero, new Vector2(116f, 60f));
            pointer.pivot = new Vector2(1f, 0.5f);
            var rim = new Color(0f, 0f, 0f, 0.65f);
            var shaft = Shape(pointer, "Shaft", new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(64f, 24f));
            shaft.Fill = _style.Accent;
            shaft.Stroke = rim;
            shaft.StrokeWidth = 3f;
            shaft.CornerRadius = 4f;
            var head = Shape(pointer, "Head", new Vector2(0f, 0.5f), new Vector2(86f, 0f), new Vector2(60f, 60f));
            head.Kind = UiShapeKind.Arrow;
            head.CornerRadius = 60f;
            head.Fill = _style.Accent;
            head.Stroke = rim;
            head.StrokeWidth = 3f;
            pointer.gameObject.SetActive(false);

            var ring = Shape(root, "HighlightRing", Center, Vector2.zero, new Vector2(100f, 100f));
            ring.Kind = UiShapeKind.Capsule;
            ring.Fill = Color.clear;
            ring.Stroke = Color.white; // Tinted by the screen (accent, pulsing).
            ring.StrokeWidth = 4f;
            ring.gameObject.SetActive(false);

            // The window: last, over everything of the layer.
            var dialog = new GameObject("Dialog", typeof(RectTransform));
            dialog.transform.SetParent(root, false);
            Stretch(dialog.GetComponent<RectTransform>());
            dialog.AddComponent<Image>().color = Dim; // Catches every touch: the game is paused under it.
            var appear = dialog.AddComponent<UiAppear>();
            var panel = PanelBox(dialog.transform, "Panel", Center, new Vector2(0f, 40f), 940f, new RectOffset(64, 64, 46, 52), 26f);
            SetReference(appear, "_panel", panel);
            var title = Heading(panel, "Title", Loc(TextKeys.HudTutorial), 44f);
            title.color = _style.Accent;
            var body = Text(panel, "Text", "", 34f, false, Center, Center, Vector2.zero, new Vector2(812f, 100f),
                TextAlignmentOptions.Center);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6f;
            Hairline(panel, 0.2f);
            var row = ButtonRow(panel, "Buttons", 0f, 86f);
            var continueButton = TextButton(row, "ContinueButton", Loc(TextKeys.TutorialContinue), true, icons.ChevronRight, 86f);
            dialog.SetActive(false);

            SetReference(screen, "_style", _style);
            SetReference(screen, "_hintRow", hintBox.gameObject);
            SetReference(screen, "_hint", hint);
            SetReference(screen, "_pointer", pointer);
            SetReference(screen, "_ring", ring);
            SetReference(screen, "_dialog", dialog);
            SetReference(screen, "_dialogTitle", title);
            SetReference(screen, "_dialogText", body);
            SetReference(screen, "_continueButton", continueButton);
            return screen;
        }

        /// <summary>The controls the tutorial rings: on the touch canvas, the HUD's Map and the desktop weapon slots.</summary>
        private static void LinkTutorialHighlights(UIRoot ui, TouchControls touch)
        {
            var screen = ui.Tutorial;
            var area = touch.transform.Find("SafeArea");
            SetReference(screen, "_stick", area.Find("StickZone/Ring"));
            SetReference(screen, "_touchAttack", area.Find("AttackCluster/Content/AttackButton"));
            SetReference(screen, "_touchMelee", area.Find("AttackCluster/Content/MeleeButton"));
            SetReference(screen, "_touchRanged", area.Find("AttackCluster/Content/RangedButton"));
            SetReference(screen, "_touchUse", area.Find("UseButton"));
            var hud = ui.Hud.transform;
            SetReference(screen, "_map", hud.Find("MapButton"));
            SetReference(screen, "_meleeSlot", hud.Find("WeaponPanel/MeleeSlot"));
            SetReference(screen, "_rangedSlot", hud.Find("WeaponPanel/RangedSlot"));
        }

        /// <summary>ТЗ §60: fullscreen map; the texture (one pixel per cell) is set by the level's MapPresenter.</summary>
        private static MapScreen BuildMap(Transform parent)
        {
            var screen = Screen<MapScreen>(parent, "MapScreen", MapBackground);
            var icons = UiIcons();

            // Narrower than the gap between the switches on the left and Close on the right, also at 4:3.
            var caption = Text(screen.transform, "Caption", "MAP", 40f, true, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -90f), new Vector2(680f, 56f), TextAlignmentOptions.Center, spacing: 8f);
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(caption, 0.6f);
            // Close and the hint inside the safe area: system bars over the page (the VK app) must not cover them.
            var buttons = new GameObject("ButtonsSafeArea", typeof(RectTransform));
            buttons.transform.SetParent(screen.transform, false);
            Stretch(buttons.GetComponent<RectTransform>());
            buttons.AddComponent<SafeAreaFitter>();
            var close = RoundButton(buttons.transform, "CloseButton", icons.Close, new Vector2(1f, 1f), new Vector2(-100f, -90f),
                84f, 34f);
            // Left of Close, spaced so that the widened touch areas meet but do not overlap.
            var hint = RoundButton(buttons.transform, "HintButton", icons.Search, new Vector2(1f, 1f), new Vector2(-232f, -90f),
                84f, 36f);
            AdMark(hint.transform, icons.Ad); // Every hint costs a rewarded ad.
            var hintStatus = Text(screen.transform, "HintStatus", "", 26f, false, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -136f), new Vector2(760f, 34f), TextAlignmentOptions.Center, _style.MutedText);
            hintStatus.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(hintStatus, 0.6f);
            hintStatus.gameObject.SetActive(false);

            // Switches and the map inside the safe area: a front camera or a notch on the left must not cover a switch.
            var safe = new GameObject("SafeArea", typeof(RectTransform));
            safe.transform.SetParent(screen.transform, false);
            Stretch(safe.GetComponent<RectTransform>());
            safe.AddComponent<SafeAreaFitter>();

            // Area below the caption, right of the switches; the image keeps the level's aspect ratio inside it.
            var area = new GameObject("MapArea", typeof(RectTransform));
            area.transform.SetParent(safe.transform, false);
            var areaRect = area.GetComponent<RectTransform>();
            Stretch(areaRect);
            areaRect.offsetMin = new Vector2(450f, 40f);
            areaRect.offsetMax = new Vector2(-40f, -160f);

            var imageObject = new GameObject("MapImage", typeof(RectTransform));
            imageObject.transform.SetParent(area.transform, false);
            var image = imageObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var fitter = imageObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;

            // First child of the image: under the icons (they are created later, at level loading).
            var routeObject = new GameObject("Route", typeof(RectTransform));
            routeObject.transform.SetParent(imageObject.transform, false);
            var route = routeObject.AddComponent<MapRouteGraphic>();
            route.raycastTarget = false;
            Stretch(route.rectTransform);

            // Icon switches (one per MapLayer, in its order) stacked at the left of the safe area, with a margin, below
            // the FPS counter (over every screen at about -182..-210). Children of SafeArea named "<name>Toggle" (tests).
            (string name, string caption)[] layers =
            {
                ("ShowPlayer", TextKeys.MapPlayer), ("ShowFragments", TextKeys.MapPieces), ("ShowZombies", TextKeys.MapZombies),
                ("ShowWeapons", TextKeys.MapWeapons), ("ShowMedkits", TextKeys.MapMedkits), ("ShowKeys", TextKeys.MapKeys),
            };
            var toggles = new List<Object>();
            var unlocks = new List<Object>();
            for (var i = 0; i < layers.Length; i++)
            {
                var toggle = LabeledSwitch(safe.transform, layers[i].name, Loc(layers[i].caption), TopLeft,
                    new Vector2(80f, -250f - 64f * i));
                toggles.Add(toggle);
                // Zombies, weapons, medkits, keys are bought per level with a rewarded ad: while locked, a button over
                // the switch (child: it gets the tap) with the ad mark at the end of the row.
                unlocks.Add(i >= (int)Maze.Presentation.Map.MapLayer.Zombies ? UnlockOverlay(toggle.transform, icons.Ad) : null);
            }

            var mapIcons = AssetDatabase.LoadAssetAtPath<MapIconSet>(MapIconsBuilder.SetPath);
            if (mapIcons == null) mapIcons = MapIconsBuilder.Build();

            SetReference(screen, "_icons", mapIcons);
            SetReferences(screen, "_layers", toggles);
            SetReferences(screen, "_unlockButtons", unlocks);
            SetReference(screen, "_hintButton", hint);
            SetReference(screen, "_hintStatus", hintStatus);
            SetReference(screen, "_route", route);
            SetReference(screen, "_image", image);
            SetReference(screen, "_fitter", fitter);
            SetReference(screen, "_caption", caption);
            SetReference(screen, "_closeButton", close);
            return screen;
        }

        /// <summary>
        /// Small FPS text on the left of the safe area, under the HUD counters and above the stick (corners may be cut
        /// off on some devices), over every screen; ignores touches. Hidden until the setting switches it on.
        /// </summary>
        /// <summary>
        /// "Rotate your device" (a phone browser held in portrait): an opaque layer over everything that eats touches —
        /// a faint portrait phone, an arrow, the landscape phone in the accent, the caption under them. Sized for a
        /// portrait canvas (about 1000 units wide there).
        /// </summary>
        private static RotateDeviceScreen BuildRotateDevice(Transform parent)
        {
            var root = new GameObject("RotateDevice", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            Stretch(root.GetComponent<RectTransform>());

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            Stretch(content.GetComponent<RectTransform>());
            content.AddComponent<Image>().color = Background; // raycast target: nothing under it gets touched

            var portrait = Shape(content.transform, "Portrait", Center, new Vector2(-230f, 90f), new Vector2(110f, 190f));
            portrait.CornerRadius = 18f;
            portrait.Fill = Color.clear;
            portrait.Stroke = Fade(_style.Line, 0.35f);
            portrait.StrokeWidth = 4f;

            var shaft = Shape(content.transform, "ArrowShaft", Center, new Vector2(-60f, 90f), new Vector2(90f, 8f));
            shaft.Kind = UiShapeKind.Capsule;
            shaft.Fill = _style.Line;
            shaft.StrokeWidth = 0f;
            var head = Shape(content.transform, "ArrowHead", Center, new Vector2(0f, 90f), new Vector2(40f, 40f));
            head.Kind = UiShapeKind.Arrow;
            head.CornerRadius = 40f;
            head.Fill = _style.Line;
            head.StrokeWidth = 0f;

            var landscape = Shape(content.transform, "Landscape", Center, new Vector2(200f, 90f), new Vector2(190f, 110f));
            landscape.CornerRadius = 18f;
            landscape.Fill = Color.clear;
            landscape.Stroke = _style.Accent;
            landscape.StrokeWidth = 5f;

            AutoSize(Text(content.transform, "Title", Loc(TextKeys.RotateDevice), 54f, true, Center, Center,
                new Vector2(0f, -110f), new Vector2(900f, 80f), TextAlignmentOptions.Center, null, 2f));

            var screen = root.AddComponent<RotateDeviceScreen>();
            SetReference(screen, "_content", content);
            content.SetActive(false);
            return screen;
        }

        private static FpsCounter BuildFpsCounter(Transform parent)
        {
            var area = new GameObject("FpsCounter", typeof(RectTransform));
            area.transform.SetParent(parent, false);
            Stretch(area.GetComponent<RectTransform>());
            area.AddComponent<SafeAreaFitter>();

            // Left, under the HUD counters (they end at about -165) and above the stick zone.
            var label = Text(area.transform, "Text", "FPS -", 22f, true, TopLeft, TopLeft, new Vector2(56f, -182f),
                new Vector2(200f, 28f), TextAlignmentOptions.TopLeft, _style.MutedText, 4f);

            var counter = area.AddComponent<FpsCounter>();
            SetReference(counter, "_text", label);
            area.SetActive(false);
            return counter;
        }

        /// <summary>
        /// Settings (mock-up): an opaque screen over the main menu or the pause. Back and the title at the top; tabs
        /// Controls / Shooting / Other (SettingsTab order) with the open one underlined in the accent; rows "caption —
        /// control (value)"; Reset tab at the bottom right.
        /// </summary>
        private static SettingsScreen BuildSettings(Transform parent)
        {
            var screen = Screen<SettingsScreen>(parent, "SettingsScreen", Background);
            Backdrop(screen.transform);
            var icons = UiIcons();
            var area = SafeArea(screen.transform);

            var back = RoundButton(area, "BackButton", icons.ChevronLeft, TopLeft, new Vector2(100f, -90f), 84f, 36f);
            Text(area, "Title", Loc(TextKeys.Settings), 46f, true, TopLeft, new Vector2(0f, 0.5f), new Vector2(172f, -90f),
                new Vector2(600f, 60f), TextAlignmentOptions.MidlineLeft, spacing: 16f);

            var column = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(area, false);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 1f);
            column.anchoredPosition = new Vector2(0f, -170f);
            column.sizeDelta = new Vector2(1180f, 0f);
            var columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            columnLayout.childControlWidth = columnLayout.childControlHeight = true;
            columnLayout.childForceExpandHeight = false;
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var tabs = new GameObject("Tabs", typeof(RectTransform));
            tabs.transform.SetParent(column, false);
            var tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 48f;
            tabsLayout.childAlignment = TextAnchor.LowerLeft;
            tabsLayout.childControlWidth = tabsLayout.childControlHeight = true;
            tabsLayout.childForceExpandWidth = tabsLayout.childForceExpandHeight = false;
            tabs.AddComponent<LayoutElement>().preferredHeight = 66f;
            var tabButtons = new List<Object>();
            var tabLabels = new List<Object>();
            var tabUnderlines = new List<Object>();
            foreach (var (name, caption) in new[] { ("ControlsTab", Loc(TextKeys.SettingsTabControls)), ("ShootingTab", Loc(TextKeys.SettingsTabShooting)), ("OtherTab", Loc(TextKeys.SettingsTabOther)) })
            {
                var (button, label, underline) = TabButton(tabs.transform, name, caption);
                tabButtons.Add(button);
                tabLabels.Add(label);
                tabUnderlines.Add(underline);
            }

            Hairline(column, 0.2f);

            var pages = new GameObject("Pages", typeof(RectTransform));
            pages.transform.SetParent(column, false);
            var pagesLayout = pages.AddComponent<VerticalLayoutGroup>();
            pagesLayout.childControlWidth = pagesLayout.childControlHeight = true;
            pagesLayout.childForceExpandHeight = false;
            pagesLayout.padding = new RectOffset(0, 0, 10, 0);

            var controlsPage = Page(pages.transform, "ControlsPage");
            var (deadZone, deadZoneLabel) = SliderSetting(controlsPage, "DeadZone", Loc(TextKeys.SettingsDeadZone));
            var (sensitivity, sensitivityLabel) = SliderSetting(controlsPage, "Sensitivity", Loc(TextKeys.SettingsStickResponse));
            var (size, sizeLabel) = SliderSetting(controlsPage, "Size", Loc(TextKeys.SettingsControlsSize));
            var (opacity, opacityLabel) = SliderSetting(controlsPage, "Opacity", Loc(TextKeys.SettingsControlsOpacity));
            var floating = SwitchSetting(controlsPage, "FloatingStick", Loc(TextKeys.SettingsFloatingStick));
            var leftHanded = SwitchSetting(controlsPage, "LeftHanded", Loc(TextKeys.SettingsLeftHanded));
            var editLayout = ButtonSetting(controlsPage, "EditLayoutButton", Loc(TextKeys.SettingsButtonLayout), Loc(TextKeys.SettingsEdit));

            var shootingPage = Page(pages.transform, "ShootingPage");
            var aimRow = SettingRow(shootingPage, "AimMode", Loc(TextKeys.SettingsAimMode));
            var aimButtons = new List<Object>();
            foreach (var (name, caption) in new[] { ("AimFreeButton", Loc(TextKeys.SettingsAimFree)), ("AimEightButton", Loc(TextKeys.SettingsAimEight)), ("AimFourButton", Loc(TextKeys.SettingsAimFour)) })
            {
                var segment = TextButton(aimRow, name, caption, false, null, 60f, 24f);
                segment.GetComponent<LayoutElement>().preferredWidth = 150f;
                aimButtons.Add(segment);
            }

            var aimAssist = SwitchSetting(shootingPage, "AimAssist", Loc(TextKeys.SettingsAimAssist));

            var otherPage = Page(pages.transform, "OtherPage");
            var languageRow = SettingRow(otherPage, "Language", Loc(TextKeys.SettingsLanguage));
            var languageButton = TextButton(languageRow, "LanguageButton", "", false, icons.ChevronRight, 60f, 26f);
            languageButton.GetComponent<LayoutElement>().preferredWidth = 420f;
            var languageName = languageButton.transform.Find("Label").GetComponent<TMP_Text>();
            languageName.characterSpacing = 2f;
            languageButton.transform.Find("Icon").localRotation = Quaternion.Euler(0f, 0f, -90f); // a chevron down
            var (music, musicLabel) = SliderSetting(otherPage, "MusicVolume", Loc(TextKeys.SettingsMusic));
            var (sfx, sfxLabel) = SliderSetting(otherPage, "SfxVolume", Loc(TextKeys.SettingsSound));
            var showFps = SwitchSetting(otherPage, "ShowFps", Loc(TextKeys.SettingsShowFps));
            // Support contact: only sites that want one (SettingsScreen.SetSupport shows it).
            var supportRow = SettingRow(otherPage, "Support", Loc(TextKeys.SettingsSupport));
            var supportValue = Text(supportRow, "SupportValue", "", 28f, true, Center, Center, Vector2.zero,
                new Vector2(560f, 40f), TextAlignmentOptions.MidlineRight, _style.MutedText);
            supportValue.gameObject.AddComponent<LayoutElement>().preferredWidth = 560f;
            supportRow.gameObject.SetActive(false);
            // Privacy policy: only where the platform has one (the Android stores; SettingsScreen.SetPrivacyPolicy).
            var privacyRow = SettingRow(otherPage, "Privacy", Loc(TextKeys.SettingsPrivacy));
            var privacyButton = TextButton(privacyRow, "PrivacyButton", Loc(TextKeys.SettingsPrivacyOpen), false, icons.ChevronRight, 60f, 26f);
            privacyButton.GetComponent<LayoutElement>().preferredWidth = 260f;
            privacyRow.gameObject.SetActive(false);

            var reset = TextButton(area, "ResetButton", Loc(TextKeys.SettingsResetTab), false, icons.Retry, 76f, 26f);
            var resetRect = (RectTransform)reset.transform;
            resetRect.anchorMin = resetRect.anchorMax = resetRect.pivot = new Vector2(1f, 0f);
            resetRect.anchoredPosition = new Vector2(-64f, 56f);
            resetRect.sizeDelta = new Vector2(400f, 76f);

            SetReference(screen, "_style", _style);
            SetReferences(screen, "_tabButtons", tabButtons);
            SetReferences(screen, "_tabLabels", tabLabels);
            SetReferences(screen, "_tabUnderlines", tabUnderlines);
            SetReferences(screen, "_pages", new List<Object> { controlsPage.gameObject, shootingPage.gameObject, otherPage.gameObject });
            SetReference(screen, "_editLayoutButton", editLayout);
            SetReference(screen, "_languageName", languageName);
            SetReference(screen, "_languageButton", languageButton);
            SetReference(screen, "_languagePicker", BuildLanguagePicker(screen.transform)); // last: over the page
            SetReference(screen, "_deadZone", deadZone);
            SetReference(screen, "_deadZoneLabel", deadZoneLabel);
            SetReference(screen, "_sensitivity", sensitivity);
            SetReference(screen, "_sensitivityLabel", sensitivityLabel);
            SetReference(screen, "_size", size);
            SetReference(screen, "_sizeLabel", sizeLabel);
            SetReference(screen, "_opacity", opacity);
            SetReference(screen, "_opacityLabel", opacityLabel);
            SetReference(screen, "_floatingStick", floating);
            SetReference(screen, "_leftHanded", leftHanded);
            SetReferences(screen, "_aimButtons", aimButtons);
            SetReference(screen, "_aimAssist", aimAssist);
            SetReference(screen, "_musicVolume", music);
            SetReference(screen, "_musicVolumeLabel", musicLabel);
            SetReference(screen, "_sfxVolume", sfx);
            SetReference(screen, "_sfxVolumeLabel", sfxLabel);
            SetReference(screen, "_showFps", showFps);
            SetReference(screen, "_supportRow", supportRow.gameObject);
            SetReference(screen, "_supportValue", supportValue);
            SetReference(screen, "_privacyRow", privacyRow.gameObject);
            SetReference(screen, "_privacyButton", privacyButton);
            SetReference(screen, "_resetButton", reset);
            SetReference(screen, "_backButton", back);
            return screen;
        }

        private static ErrorScreen BuildError(Transform parent)
        {
            var screen = Screen<ErrorScreen>(parent, "ErrorScreen", Background);
            Backdrop(screen.transform);
            var panel = PanelBox(screen.transform, "Panel", Center, Vector2.zero, 980f, new RectOffset(60, 60, 52, 56), 24f);
            SetReference(screen.GetComponent<UiAppear>(), "_panel", panel);
            Heading(panel, "Title", Loc(TextKeys.ErrorTitle), 46f);
            var message = Text(panel, "Message", "Error", 26f, false, Center, Center, Vector2.zero, new Vector2(860f, 220f),
                TextAlignmentOptions.TopLeft, _style.MutedText);
            message.gameObject.AddComponent<LayoutElement>().preferredHeight = 220f;
            var back = TextButton(panel, "BackButton", Loc(TextKeys.ErrorBack), true, icons: null, height: 86f);

            SetReference(screen, "_message", message);
            SetReference(screen, "_backButton", back);
            return screen;
        }

        // ---- uGUI helpers ----

        private static T Screen<T>(Transform parent, string name, Color background) where T : UIScreen
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());
            go.AddComponent<Image>().color = background;
            var screen = go.AddComponent<T>();
            go.AddComponent<UiAppear>(); // Fades in when shown (adds its CanvasGroup).
            go.SetActive(false); // ScreenRouter shows the right one.
            return screen;
        }

        /// <summary>The menu background picture (faint maze, torch glow, vignette), covering the screen.</summary>
        private static void Backdrop(Transform screen)
        {
            var rect = Rect(screen, "Backdrop", Center, Vector2.zero, new Vector2(1920f, 1080f));
            rect.SetAsFirstSibling();
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = UiIcons().Backdrop;
            image.raycastTarget = false;
            var fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;
        }

        /// <summary>A child following the safe area (notches, rounded corners).</summary>
        private static RectTransform SafeArea(Transform screen)
        {
            var rect = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(screen, false);
            Stretch(rect);
            rect.gameObject.AddComponent<SafeAreaFitter>();
            return rect;
        }

        /// <summary>A diamond in the accent, a line, a diamond in the line colour; <paramref name="start"/> is the left end.</summary>
        private static void OrnamentLine(Transform parent, Vector2 anchor, Vector2 start, float width)
        {
            var holder = Rect(parent, "Ornament", anchor, start, new Vector2(width, 16f));
            holder.pivot = new Vector2(0f, 0.5f);
            holder.anchoredPosition = start;
            var left = Shape(holder, "DiamondLeft", new Vector2(0f, 0.5f), new Vector2(7f, 0f), new Vector2(11f, 11f));
            left.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            left.Fill = Color.clear;
            left.Stroke = _style.Accent;
            var line = Shape(holder, "Line", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 44f, 1.5f));
            line.StrokeWidth = 0f;
            line.Fill = Fade(_style.Line, 0.6f);
            var right = Shape(holder, "DiamondRight", new Vector2(1f, 0.5f), new Vector2(-7f, 0f), new Vector2(11f, 11f));
            right.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            right.Fill = Color.clear;
        }

        /// <summary>
        /// A panel (dark fill, thin outline, a diamond on the top and bottom edges) laying its children out top to bottom,
        /// as tall as they need. The anchor is also the pivot.
        /// </summary>
        private static Transform PanelBox(Transform parent, string name, Vector2 anchor, Vector2 position, float width,
            RectOffset padding, float spacing)
        {
            var shape = Shape(parent, name, anchor, position, new Vector2(width, 100f));
            shape.rectTransform.pivot = anchor;
            shape.Fill = new Color(0.055f, 0.051f, 0.047f, 0.93f);
            shape.Stroke = Fade(_style.Line, 0.8f);
            shape.CornerRadius = 2f;
            shape.raycastTarget = true; // Clicks on the panel do not fall through.
            var layout = shape.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            shape.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var top in new[] { true, false })
            {
                var diamond = Shape(shape.transform, top ? "DiamondTop" : "DiamondBottom", new Vector2(0.5f, top ? 1f : 0f),
                    Vector2.zero, new Vector2(13f, 13f));
                diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                diamond.Fill = Background;
                diamond.Stroke = Fade(_style.Line, 0.9f);
                diamond.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }

            return shape.transform;
        }

        private static TMP_Text Heading(Transform parent, string name, string text, float size)
        {
            var label = Text(parent, name, text, size, true, Center, Center, Vector2.zero, new Vector2(800f, size * 1.3f),
                TextAlignmentOptions.Center, spacing: size * 0.25f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(label);
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = size * 1.3f;
            return label;
        }

        private static void Hairline(Transform parent, float alpha)
        {
            var line = Shape(parent, "Hairline", Center, Vector2.zero, new Vector2(100f, 1.5f));
            line.StrokeWidth = 0f;
            line.Fill = Fade(_style.Line, alpha);
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = 1.5f;
        }

        /// <summary>Equal-width children side by side.</summary>
        private static Transform ButtonRow(Transform parent, string name, float spacing, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return go.transform;
        }

        /// <summary>
        /// A button with a caption (bold, spaced) and an optional icon on the right; in the accent for the main action.
        /// Sized by its layout (preferred height <paramref name="height"/>).
        /// </summary>
        private static UiButton TextButton(Transform parent, string name, string text, bool accent, Sprite icons,
            float height, float fontSize = 30f)
        {
            var body = Shape(parent, name, Center, Vector2.zero, new Vector2(300f, height), true);
            body.CornerRadius = 2f;
            body.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var label = Text(body.transform, "Label", text, fontSize, true, Center, Center, Vector2.zero, Vector2.zero,
                icons != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center, spacing: fontSize * 0.45f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(label);
            var labelRect = label.rectTransform;
            Stretch(labelRect);
            if (icons == null) return MakeButton(body, accent, label);

            labelRect.offsetMin = new Vector2(30f, 0f);
            labelRect.offsetMax = new Vector2(-70f, 0f);
            var icon = Icon(body.transform, "Icon", icons, new Vector2(1f, 0.5f), new Vector2(-38f, 0f), fontSize * 1.1f);
            return MakeButton(body, accent, label, icon);
        }

        /// <summary>Small underlined text that works as a button (debug actions).</summary>
        private static Button LinkButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, float width)
        {
            var label = Text(parent, name, text, 22f, true, anchor, new Vector2(1f, 0f), position, new Vector2(width, 34f),
                TextAlignmentOptions.BottomRight, _style.MutedText, 4f);
            label.fontStyle = FontStyles.Underline;
            label.raycastTarget = true;
            var button = label.gameObject.AddComponent<Button>();
            button.targetGraphic = label;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            return button;
        }

        /// <summary>
        /// The language list over the settings: a dimmed blocker (a tap closes the list) and a panel with a scrolling
        /// column of buttons — one built here, the rest cloned by <see cref="LanguagePicker"/> for every language.
        /// </summary>
        private static LanguagePicker BuildLanguagePicker(Transform screen)
        {
            var go = new GameObject("LanguagePicker", typeof(RectTransform));
            go.transform.SetParent(screen, false);
            Stretch(go.GetComponent<RectTransform>());

            var overlay = new GameObject("Overlay", typeof(RectTransform));
            overlay.transform.SetParent(go.transform, false);
            Stretch(overlay.GetComponent<RectTransform>());

            var blocker = Shape(overlay.transform, "Blocker", Center, Vector2.zero, Vector2.zero, true);
            Stretch(blocker.rectTransform);
            blocker.Fill = new Color(0f, 0f, 0f, 0.45f);
            blocker.StrokeWidth = 0f;
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.targetGraphic = blocker;

            var panel = Shape(overlay.transform, "Panel", Center, Vector2.zero, new Vector2(560f, 660f), true);
            panel.Fill = new Color(0.055f, 0.051f, 0.047f, 0.97f);
            panel.Stroke = Fade(_style.Line, 0.8f);
            panel.CornerRadius = 2f;

            var viewport = Rect(panel.transform, "Viewport", Center, Vector2.zero, Vector2.zero);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(20f, 20f);
            viewport.offsetMax = new Vector2(-20f, -20f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.viewport = viewport;
            scroll.content = content;

            var item = TextButton(content, "Language0", "", false, null, 66f, 28f);
            item.transform.Find("Label").GetComponent<TMP_Text>().characterSpacing = 2f;

            var picker = go.AddComponent<LanguagePicker>();
            SetReference(picker, "_root", overlay);
            SetReference(picker, "_blocker", blockerButton);
            SetReference(picker, "_scroll", scroll);
            SetReference(picker, "_item", item);
            overlay.SetActive(false);
            return picker;
        }

        /// <summary>A settings tab: caption and an accent underline (shown for the open tab).</summary>
        private static (Button Button, TMP_Text Label, GameObject Underline) TabButton(Transform parent, string name, string text)
        {
            var hit = Shape(parent, name, Center, Vector2.zero, new Vector2(240f, 66f), true);
            hit.Fill = Color.clear;
            hit.StrokeWidth = 0f;
            var element = hit.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 240f;
            element.preferredHeight = 66f;
            var label = Text(hit.transform, "Label", text, 30f, true, Center, Center, Vector2.zero, Vector2.zero,
                TextAlignmentOptions.MidlineLeft, _style.MutedText, 12f);
            Stretch(label.rectTransform);
            var underline = Shape(hit.transform, "Underline", new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 3f));
            var underlineRect = underline.rectTransform;
            underlineRect.anchorMin = new Vector2(0f, 0f);
            underlineRect.anchorMax = new Vector2(1f, 0f);
            underlineRect.pivot = new Vector2(0.5f, 0f);
            underlineRect.anchoredPosition = Vector2.zero;
            underlineRect.sizeDelta = new Vector2(-50f, 3f);
            underlineRect.offsetMin = new Vector2(0f, 0f);
            underlineRect.offsetMax = new Vector2(-60f, 3f);
            underline.StrokeWidth = 0f;
            underline.Fill = _style.Accent;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            return (button, label, underline.gameObject);
        }

        /// <summary>A settings page: rows top to bottom.</summary>
        private static Transform Page(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return go.transform;
        }

        /// <summary>A settings row: the caption on the left; controls added to it go to the right. A faint line under it.</summary>
        private static Transform SettingRow(Transform page, string name, string caption)
        {
            var row = new GameObject(name + "Row", typeof(RectTransform));
            row.transform.SetParent(page, false);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.AddComponent<LayoutElement>().preferredHeight = 80f;

            var label = Text(row.transform, "Caption", caption, 30f, false, Center, Center, Vector2.zero, new Vector2(10f, 40f),
                TextAlignmentOptions.MidlineLeft);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var line = Shape(row.transform, "Line", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 1f));
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 1f);
            line.StrokeWidth = 0f;
            line.Fill = Fade(_style.Line, 0.1f);
            line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return row.transform;
        }

        /// <summary>Slider and its value on the right (the screen writes the value text).</summary>
        private static (Slider Slider, TMP_Text Value) SliderSetting(Transform page, string name, string caption)
        {
            var row = SettingRow(page, name, caption);
            var slider = StyledSlider(row, name + "Slider", 480f, 40f);
            var element = slider.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 480f;
            element.preferredHeight = 40f;
            var value = Text(row, name + "Value", "0%", 28f, true, Center, Center, Vector2.zero, new Vector2(110f, 40f),
                TextAlignmentOptions.MidlineRight);
            value.gameObject.AddComponent<LayoutElement>().preferredWidth = 110f;
            return (slider, value);
        }

        private static Toggle SwitchSetting(Transform page, string name, string caption)
        {
            var row = SettingRow(page, name, caption);
            var toggle = Switch(row, name + "Toggle");
            var element = toggle.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 88f;
            element.preferredHeight = 44f;
            return toggle;
        }

        private static UiButton ButtonSetting(Transform page, string name, string caption, string text)
        {
            var row = SettingRow(page, name, caption);
            var button = TextButton(row, name, text, false, null, 60f, 24f);
            button.GetComponent<LayoutElement>().preferredWidth = 180f;
            return button;
        }

        /// <summary>A thin track, the accent fill and a diamond handle (uGUI Slider, look only here).</summary>
        private static Slider StyledSlider(Transform parent, string name, float width, float height)
        {
            var root = Rect(parent, name, Center, Vector2.zero, new Vector2(width, height));
            // Invisible touch zone over the whole slider and beyond it (a finger rarely hits the thin line or the handle):
            // a press anywhere moves the handle there and drags from it.
            var hit = Shape(root, "HitArea", Center, Vector2.zero, Vector2.zero, true);
            Stretch(hit.rectTransform);
            hit.rectTransform.offsetMin = new Vector2(-16f, -18f);
            hit.rectTransform.offsetMax = new Vector2(16f, 18f);
            hit.Fill = Color.clear;
            hit.StrokeWidth = 0f;
            var track = Shape(root, "Track", Center, Vector2.zero, Vector2.zero);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, 2f);
            track.StrokeWidth = 0f;
            track.Fill = Fade(_style.Line, 0.35f);

            var fillArea = Rect(root, "Fill Area", Center, Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 5f);
            var fill = Shape(fillArea, "Fill", Center, Vector2.zero, Vector2.zero);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.sizeDelta = Vector2.zero;
            fill.StrokeWidth = 0f;
            fill.Fill = _style.Accent;

            var handleArea = Rect(root, "Handle Slide Area", Center, Vector2.zero, Vector2.zero);
            Stretch(handleArea);
            // The slider stretches its handle over the area's height: the diamond is a fixed-size child of it.
            var handle = Rect(handleArea, "Handle", Center, Vector2.zero, new Vector2(22f, 0f));
            var diamond = Shape(handle, "Diamond", Center, Vector2.zero, new Vector2(22f, 22f), true);
            diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            diamond.Fill = Background;
            diamond.Stroke = _style.Accent;
            diamond.StrokeWidth = 2f;
            diamond.HitPadding = 12f;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle;
            slider.targetGraphic = diamond;
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        /// <summary>A pill switch (uGUI Toggle) painted by <see cref="UiSwitch"/>.</summary>
        private static Toggle Switch(Transform parent, string name)
        {
            var pill = Shape(parent, name, Center, Vector2.zero, new Vector2(88f, 44f), true);
            pill.Kind = UiShapeKind.Capsule;
            pill.Fill = Color.clear;
            pill.StrokeWidth = 2f;
            pill.HitPadding = 10f;
            var knob = Shape(pill.transform, "Knob", Center, new Vector2(-22f, 0f), new Vector2(28f, 28f));
            knob.Kind = UiShapeKind.Capsule;
            AttachSwitch(pill.gameObject, pill, knob, new Vector2(-22f, 22f));
            var toggle = pill.GetComponent<Toggle>();
            toggle.targetGraphic = pill;
            return toggle;
        }

        /// <summary>
        /// Transparent button over a map layer switch row (and the ad mark at its end), shown while the layer is locked.
        /// </summary>
        private static Button UnlockOverlay(Transform row, Sprite adIcon)
        {
            var overlay = Shape(row, "UnlockButton", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(400f, 60f), true);
            overlay.rectTransform.pivot = new Vector2(0f, 0.5f);
            overlay.Fill = Color.clear;
            overlay.StrokeWidth = 0f;
            overlay.raycastTarget = true;
            var mark = Icon(overlay.transform, "Ad", adIcon, new Vector2(0f, 0.5f), new Vector2(372f, 0f), 34f);
            mark.color = _style.Accent;
            var button = overlay.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = overlay;
            overlay.gameObject.SetActive(false);
            return button;
        }

        /// <summary>A small ad mark at the bottom right of a round button (a rewarded ad comes first).</summary>
        private static void AdMark(Transform button, Sprite adIcon)
        {
            var back = Shape(button, "AdMark", new Vector2(1f, 0f), new Vector2(-6f, 6f), new Vector2(34f, 34f));
            back.Kind = UiShapeKind.Capsule;
            back.StrokeWidth = 0f;
            back.Fill = Background;
            back.raycastTarget = false;
            var icon = Icon(back.transform, "Icon", adIcon, Center, Vector2.zero, 24f);
            icon.color = _style.Accent;
        }

        /// <summary>A switch with a caption on its right, placed absolutely (map screen).</summary>
        private static Toggle LabeledSwitch(Transform parent, string name, string caption, Vector2 anchor, Vector2 position)
        {
            var hit = Shape(parent, name + "Toggle", anchor, position, new Vector2(350f, 60f), true);
            hit.rectTransform.pivot = new Vector2(0f, 0.5f);
            hit.rectTransform.anchoredPosition = position;
            hit.Fill = Color.clear;
            hit.StrokeWidth = 0f;
            var pill = Shape(hit.transform, "Pill", new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(88f, 44f));
            pill.Kind = UiShapeKind.Capsule;
            pill.Fill = Color.clear;
            pill.StrokeWidth = 2f;
            var knob = Shape(pill.transform, "Knob", Center, new Vector2(-22f, 0f), new Vector2(28f, 28f));
            knob.Kind = UiShapeKind.Capsule;
            var label = Text(hit.transform, "Label", caption, 26f, true, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(104f, 0f), new Vector2(246f, 40f), TextAlignmentOptions.MidlineLeft, spacing: 6f);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(label);
            AttachSwitch(hit.gameObject, pill, knob, new Vector2(-22f, 22f));
            var toggle = hit.GetComponent<Toggle>();
            toggle.targetGraphic = hit;
            return toggle;
        }

        private static void AttachSwitch(GameObject target, UiShape pill, UiShape knob, Vector2 knobX)
        {
            var toggle = target.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.graphic = null;
            var view = target.AddComponent<UiSwitch>();
            SetReference(view, "_style", _style);
            SetReference(view, "_pill", pill);
            SetReference(view, "_knob", knob);
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_knobX").vector2Value = knobX;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null)
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized field '{field}'.");
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReferences(Object target, string field, IReadOnlyList<Object> values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
                throw new System.InvalidOperationException($"{target.GetType().Name} has no serialized array '{field}'.");
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            var ours = new[] { BootstrapPath, GamePath };
            var scenes = new List<EditorBuildSettingsScene>(ours.Select(path => new EditorBuildSettingsScene(path, true)));
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => !ours.Contains(s.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
