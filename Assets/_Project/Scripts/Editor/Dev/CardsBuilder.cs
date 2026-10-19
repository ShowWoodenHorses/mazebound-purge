using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Purge.Core.Cards;
using Purge.Core.Content;
using Purge.Core.Definitions;
using Purge.Core.Effects;
using UnityEditor;
using UnityEngine;

namespace Purge.Editor.Dev
{
    /// <summary>
    /// Purge → Dev → Build Cards (stage 6c): makes the first set of cards in <c>Data/Cards</c> that are not there yet
    /// (a card already there keeps whatever was changed in it), gives cards without an icon the drawn one of the same
    /// name (Build UI Icons draws them), and collects every <see cref="CardDefinition"/> of the folder into the
    /// <see cref="CardDeck"/> (Addressable through Sync: <see cref="CardDeck.Address"/>). A new card is an asset in the
    /// folder and its texts in Strings.tsv (<c>card.&lt;id&gt;</c>, <c>card.&lt;id&gt;.text</c>), no code.
    /// </summary>
    internal static class CardsBuilder
    {
        public const string Folder = "Assets/_Project/Data/Cards";
        public const string DeckPath = Folder + "/CardDeck.asset";
        private const string GrenadePath = "Assets/_Project/Data/Throwables/Grenade.asset";
        private const string BurningPath = "Assets/_Project/Data/Effects/Status_Burning.asset";

        private sealed class Draft
        {
            public string File;
            public string Icon;
            public Rarity Rarity;
            public int MaxLevel;
            public Func<CardModule[]> Modules;
        }

        [MenuItem("Purge/Dev/Build Cards")]
        public static void BuildMenu() => Debug.Log("[Purge] " + Build());

        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Cards");
            var grenade = AssetDatabase.LoadAssetAtPath<ThrowableDefinition>(GrenadePath);
            var burning = AssetDatabase.LoadAssetAtPath<StatusDefinition>(BurningPath);
            var icons = UiIconsBuilder.BuildCardIcons();

            var drafts = new[]
            {
                Stat("Card_Damage", "card_damage", Rarity.Common, 5, RunStat.WeaponDamage, 0.1f),
                Stat("Card_FireRate", "card_fire_rate", Rarity.Common, 5, RunStat.FireRate, 0.08f),
                Stat("Card_Reload", "card_reload", Rarity.Common, 5, RunStat.ReloadTime, 0.12f),
                Stat("Card_Magazine", "card_magazine", Rarity.Common, 5, RunStat.Magazine, 0.2f),
                Stat("Card_Speed", "card_speed", Rarity.Common, 5, RunStat.MoveSpeed, 0.06f),
                Stat("Card_Health", "card_health", Rarity.Common, 5, RunStat.MaxHealth, 15f),
                Stat("Card_Magnet", "card_magnet", Rarity.Common, 3, RunStat.PickupRadius, 1f),
                new Draft
                {
                    File = "Card_Throwables", Icon = "card_throwables", Rarity = Rarity.Uncommon, MaxLevel = 5,
                    Modules = () => new CardModule[] { new ThrowablesCard { Count = 2, Fallback = grenade } },
                },
                Stat("Card_HealOnKill", "card_heal_on_kill", Rarity.Uncommon, 3, RunStat.HealOnKill, 1f),
                new Draft
                {
                    File = "Card_Explosives", Icon = "card_explosives", Rarity = Rarity.Uncommon, MaxLevel = 3,
                    Modules = () => new CardModule[]
                    {
                        new StatCard { Stat = RunStat.ExplosionDamage, PerLevel = 0.15f },
                        new StatCard { Stat = RunStat.ExplosionRadius, PerLevel = 0.1f },
                    },
                },
                new Draft
                {
                    File = "Card_Burning", Icon = "card_burning", Rarity = Rarity.Rare, MaxLevel = 3,
                    Modules = () => new CardModule[] { new StatusDamageCard { Status = burning, PerLevel = 0.25f } },
                },
                Stat("Card_Crit", "card_crit", Rarity.Rare, 5, RunStat.CritChance, 0.05f),
            };

            var created = 0;
            foreach (var draft in drafts)
            {
                var path = $"{Folder}/{draft.File}.asset";
                var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardDefinition>();
                    card.Configure(null, draft.Rarity, draft.MaxLevel, draft.Modules());
                    AssetDatabase.CreateAsset(card, path); // the id is given on import (ContentIdAssigner)
                    created++;
                }

                if (card.Icon == null && icons.TryGetValue(draft.Icon, out var icon))
                {
                    card.Icon = icon;
                    EditorUtility.SetDirty(card);
                }
            }

            var cards = AssetDatabase.FindAssets("t:" + nameof(CardDefinition), new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<CardDefinition>)
                .Where(card => card != null)
                .ToList();

            var problems = new List<string>();
            foreach (var card in cards) card.Validate(problems);

            var deck = AssetDatabase.LoadAssetAtPath<CardDeck>(DeckPath);
            if (deck == null)
            {
                deck = ScriptableObject.CreateInstance<CardDeck>();
                AssetDatabase.CreateAsset(deck, DeckPath);
            }

            deck.SetCards(cards);
            EditorUtility.SetDirty(deck);
            AssetDatabase.SaveAssets();
            foreach (var problem in problems) Debug.LogWarning("[Purge] " + problem);
            return $"Cards built: {cards.Count} in the deck, {created} new, {problems.Count} problem(s).";
        }

        private static Draft Stat(string file, string icon, Rarity rarity, int maxLevel, RunStat stat, float perLevel) =>
            new Draft
            {
                File = file, Icon = icon, Rarity = rarity, MaxLevel = maxLevel,
                Modules = () => new CardModule[] { new StatCard { Stat = stat, PerLevel = perLevel } },
            };
    }
}
