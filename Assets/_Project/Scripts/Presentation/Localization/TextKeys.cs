namespace Maze.Presentation.Localization
{
    /// <summary>
    /// Keys of the texts in <c>Data/Localization/Strings.tsv</c>. Every key the game uses is a constant here or starts
    /// with one of the <c>*Prefix</c> families (content: weapon ids, key colours, touch controls): Maze → Dev → Build
    /// Localization reports table keys that are neither (unused) and constants missing from the table.
    /// </summary>
    public static class TextKeys
    {
        // Shared.
        public const string Settings = "common.settings";
        public const string Yes = "common.yes";
        public const string No = "common.no";
        public const string Percent = "format.percent";

        // Main menu, level select, loading.
        public const string MenuPlay = "menu.play";
        public const string MenuResetProgress = "menu.reset_progress";
        public const string MenuTutorial = "menu.tutorial";
        public const string MenuLevels = "menu.levels";
        public const string MenuSignIn = "menu.sign_in";
        public const string MenuSignInHint = "menu.sign_in.hint";
        public const string LevelSelectTitle = "level_select.title";
        public const string LevelSelectEmpty = "level_select.empty";
        public const string Loading = "loading";

        // HUD.
        public const string HudLevel = "hud.level";
        public const string HudTutorial = "hud.tutorial";
        public const string HudReload = "hud.reload";
        public const string HudWeaponHere = "hud.weapon_here";
        public const string HudWeaponTaken = "hud.weapon_taken";
        public const string HudFragmentFound = "hud.fragment_found";
        public const string HudLocked = "hud.locked";
        public const string HudDoorUnlocked = "hud.door_unlocked";
        public const string HudDoorBlocked = "hud.door_blocked";
        public const string HudDoorFrozen = "hud.door_frozen";

        /// <summary>"hud.locked.red": locked door whose key has that colour tag (falls back to <see cref="HudLocked"/>).</summary>
        public const string HudLockedPrefix = "hud.locked.";

        /// <summary>"weapon.katana": weapon name by its definition id (falls back to the id).</summary>
        public const string WeaponPrefix = "weapon.";

        // Exit, pause, result, error.
        public const string ConfirmExitTitle = "confirm_exit.title";
        public const string PauseTitle = "pause.title";
        public const string PauseResume = "pause.resume";
        public const string PauseRestart = "pause.restart";
        public const string PauseExit = "pause.exit";
        public const string PauseDebugComplete = "pause.debug_complete";
        public const string PauseDebugFail = "pause.debug_fail";
        public const string ResultComplete = "result.complete";
        public const string ResultFailed = "result.failed";
        public const string ResultExitFound = "result.exit_found";
        public const string ResultNoExit = "result.no_exit";
        public const string ResultZombies = "result.zombies";
        public const string ResultMap = "result.map";
        public const string ResultTime = "result.time";
        public const string ResultBest = "result.best";
        public const string ResultRecord = "result.record";
        public const string ResultMenu = "result.menu";
        public const string ResultRetry = "result.retry";
        public const string ResultNext = "result.next";
        public const string ResultTutorialComplete = "result.tutorial_complete";
        public const string ErrorTitle = "error.title";
        public const string ErrorBack = "error.back";

        // Map.
        public const string MapPlayer = "map.player";
        public const string MapPieces = "map.pieces";
        public const string MapZombies = "map.zombies";
        public const string MapWeapons = "map.weapons";
        public const string MapMedkits = "map.medkits";
        public const string MapKeys = "map.keys";
        public const string HintFragment = "map.hint.fragment";
        public const string HintExit = "map.hint.exit";
        public const string HintKey = "map.hint.key";
        public const string HintDoor = "map.hint.door";
        public const string HintNone = "map.hint.none";
        public const string HintTutorial = "map.hint.tutorial";
        public const string AdUnavailable = "ad.unavailable";
        public const string RotateDevice = "rotate.title";

        // Tutorial window.
        public const string TutorialContinue = "tutorial_ui.continue";

        /// <summary>
        /// "tutorial.move": text of a tutorial step (<c>TutorialScript</c>); "<key>.touch" — its touch-screen variant,
        /// "<key>.hint" (and "<key>.hint.touch") — the reminder line while the step is on.
        /// </summary>
        public const string TutorialPrefix = "tutorial.";
        public static readonly string TouchSuffix = ".touch"; // Not a key: not a constant (Build Localization lists constants).
        public static readonly string HintSuffix = ".hint";
        public const string MapNone = "map.none";
        public const string MapNoneFound = "map.none_found";
        public const string MapCount = "map.count";

        // Settings.
        public const string SettingsTabControls = "settings.tab.controls";
        public const string SettingsTabShooting = "settings.tab.shooting";
        public const string SettingsTabOther = "settings.tab.other";
        public const string SettingsDeadZone = "settings.dead_zone";
        public const string SettingsStickResponse = "settings.stick_response";
        public const string SettingsControlsSize = "settings.controls_size";
        public const string SettingsControlsOpacity = "settings.controls_opacity";
        public const string SettingsFloatingStick = "settings.floating_stick";
        public const string SettingsLeftHanded = "settings.left_handed";
        public const string SettingsButtonLayout = "settings.button_layout";
        public const string SettingsEdit = "settings.edit";
        public const string SettingsAimMode = "settings.aim_mode";
        public const string SettingsAimFree = "settings.aim.free";
        public const string SettingsAimEight = "settings.aim.eight";
        public const string SettingsAimFour = "settings.aim.four";
        public const string SettingsAimAssist = "settings.aim_assist";
        public const string SettingsMusic = "settings.music";
        public const string SettingsSound = "settings.sound";
        public const string SettingsShowFps = "settings.show_fps";
        public const string SettingsLanguage = "settings.language";
        public const string SettingsSupport = "settings.support";
        public const string SettingsPrivacy = "settings.privacy";
        public const string SettingsPrivacyOpen = "settings.privacy.open";
        public const string SettingsResetTab = "settings.reset_tab";

        // Touch layout editor.
        public const string LayoutHint = "layout.hint";
        public const string LayoutSize = "layout.size";
        public const string LayoutReset = "layout.reset";
        public const string LayoutDone = "layout.done";

        /// <summary>"layout.element.stick": name of a <c>TouchElement</c> (lower case of the enum name).</summary>
        public const string LayoutElementPrefix = "layout.element.";
    }
}
