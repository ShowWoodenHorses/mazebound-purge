namespace Purge.Presentation.Localization
{
    /// <summary>
    /// Keys of the texts in <c>Data/Localization/Strings.tsv</c>. Every key the game uses is a constant here or starts
    /// with one of the <c>*Prefix</c> families (content: weapon ids, key colours, touch controls): Purge → Dev → Build
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
        public const string MenuContinue = "menu.continue";
        public const string MenuPlay = "menu.play";
        public const string ConfirmNewRun = "confirm.new_run";
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
        public const string HudLocked = "hud.locked";
        public const string HudDoorUnlocked = "hud.door_unlocked";
        public const string HudDoorBlocked = "hud.door_blocked";
        public const string HudDoorFrozen = "hud.door_frozen";

        // Level objects (stage 7в).
        public const string HudRefuge = "hud.refuge";
        public const string HudRefugeOver = "hud.refuge.over";
        public const string HudChestLocked = "hud.chest.locked";
        public const string HudChestKey = "hud.chest.key";
        public const string HudChallenge = "hud.chest.challenge";
        public const string HudChallengeDone = "hud.chest.challenge_done";

        // Waves (stage 6b).
        public const string HudWave = "hud.wave";
        public const string HudWaveStart = "hud.wave.start";
        public const string HudWaveCleared = "hud.wave.cleared";
        public const string HudWaveCountdown = "hud.wave.countdown";
        public const string HudWaveSkip = "hud.wave.skip";
        public const string HudKeySpace = "hud.key.space";

        // Experience and cards (stage 6c).
        public const string HudRunLevel = "hud.run.level";
        public const string CardsTitle = "cards.title";
        public const string CardsSubtitle = "cards.subtitle";
        public const string CardsNew = "cards.new";
        public const string CardsLevel = "cards.level";
        public const string CardsReroll = "cards.reroll";

        // Death window (stage 6d).
        public const string DeathTitle = "death.title";
        public const string DeathRevive = "death.revive";
        public const string DeathReviveSkill = "death.revive_skill";
        public const string DeathGiveUp = "death.give_up";

        /// <summary>"card.card_damage": card name; "card.card_damage.text" — what a level gives ({0}, {1}… — its numbers).</summary>
        public const string CardPrefix = "card.";
        public static readonly string CardTextSuffix = ".text"; // Not a key: not a constant.

        /// <summary>"card.rarity.common": a card's rarity caption.</summary>
        public const string CardRarityPrefix = "card.rarity.";

        /// <summary>"ammo.ammo_9mm": ammo type name by its definition id (falls back to the id).</summary>
        public const string AmmoPrefix = "ammo.";

        /// <summary>"hud.locked.red": locked door whose key has that colour tag (falls back to <see cref="HudLocked"/>).</summary>
        public const string HudLockedPrefix = "hud.locked.";

        /// <summary>"weapon.katana": weapon name by its definition id (falls back to the id).</summary>
        public const string WeaponPrefix = "weapon.";

        // Exit, pause, result, error.
        public const string PauseTitle = "pause.title";
        public const string PauseResume = "pause.resume";
        public const string PauseRestart = "pause.restart";
        public const string PauseExit = "pause.exit";
        public const string PauseDebugComplete = "pause.debug_complete";
        public const string PauseDebugFail = "pause.debug_fail";
        public const string ResultComplete = "result.complete";
        public const string ResultFailed = "result.failed";
        public const string ResultWaves = "result.waves";
        public const string ResultKills = "result.kills";
        public const string ResultTime = "result.time";
        public const string ResultScore = "result.score";
        public const string ResultMoney = "result.money";
        public const string ResultDouble = "result.double";
        public const string ResultMenu = "result.menu";
        public const string ResultRetry = "result.retry";
        public const string ResultNext = "result.next";
        public const string ResultTutorialComplete = "result.tutorial_complete";
        public const string ErrorTitle = "error.title";
        public const string ErrorBack = "error.back";

        // Map.
        public const string MapPlayer = "map.player";
        public const string MapZombies = "map.zombies";
        public const string MapWeapons = "map.weapons";
        public const string MapMedkits = "map.medkits";
        public const string MapKeys = "map.keys";
        public const string MapObjects = "map.objects";
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
        public const string SettingsScheme = "settings.scheme";
        public const string SettingsSchemeTwoSticks = "settings.scheme_two_sticks";
        public const string SettingsSchemeAuto = "settings.scheme_auto";
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
        public const string SettingsTabGame = "settings.tab.game";
        public const string SettingsVibration = "settings.vibration";
        public const string SettingsCameraDistance = "settings.camera_distance";
        public const string SettingsCameraDistanceValue = "settings.camera_distance.value";
        public const string SettingsCameraShake = "settings.camera_shake";
        public const string SettingsResolution = "settings.resolution";
        public const string SettingsCompare = "settings.compare";
        public const string SettingsCompareArrows = "settings.compare.arrows";
        public const string SettingsCompareColor = "settings.compare.color";
        public const string SettingsCompareOff = "settings.compare.off";

        // The panel over a weapon on the ground (stage 6e2).
        public const string CompareDps = "compare.dps";
        public const string CompareMagazine = "compare.magazine";
        public const string CompareRange = "compare.range";
        public const string CompareMobility = "compare.mobility";

        // Touch layout editor.
        public const string LayoutHint = "layout.hint";
        public const string LayoutSize = "layout.size";
        public const string LayoutReset = "layout.reset";
        public const string LayoutDone = "layout.done";

        /// <summary>"layout.element.stick": name of a <c>TouchElement</c> (lower case of the enum name).</summary>
        public const string LayoutElementPrefix = "layout.element.";
    }
}
