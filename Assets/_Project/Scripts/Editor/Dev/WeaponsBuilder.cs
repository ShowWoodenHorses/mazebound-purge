using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Maze.Core.Definitions;
using Maze.Core.Level;
using Maze.Core.Visual;
using Maze.Presentation.Visual;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Maze.Editor.Dev
{
    /// <summary>
    /// Maze → Dev → Build Weapons: the weapons of the game from the models in Art/Weapons (Synty: pivot at the grip,
    /// pointing along +Z). Every prefab there (except Pickups) is a weapon; its folders tell the kind: under a
    /// <c>Melee</c> folder — melee, otherwise a gun, automatic when a folder on its path is <c>Auto</c>.
    /// <list type="number">
    /// <item><see cref="WeaponDefinition"/> per model in Data/Weapons. A model already in <see cref="WeaponVisualCatalog"/>
    /// keeps its definition; a new one gets <c>Data/Weapons/&lt;name&gt;.asset</c> (name = prefab name without
    /// <c>SM_Wep_</c>) with draft values of its kind (<see cref="DraftFor"/>). Values are tuned in the asset and never
    /// overwritten.</item>
    /// <item>Held prefab = the model + <see cref="WeaponModel"/> with a Muzzle (guns) or Tip (melee) mark at the far
    /// end of the mesh; <see cref="WeaponVisualCatalog"/> (Data/Weapons/WeaponVisuals, "Weapons/Visual") maps
    /// definitions to them.</item>
    /// <item>Pickup prefab (Art/Weapons/Pickups) = the model lying on its side, so its silhouette is seen from above,
    /// diagonal in the cell, on the floor; the placeholder theme's Weapon set gets one variant per definition, and
    /// saved levels whose weapon visuals no longer fit are reassigned.</item>
    /// <item><see cref="CharacterWeaponRig"/> on the player prefab: palm centres and the melee socket, from the hand
    /// and finger bones (independent of any pose).</item>
    /// <item>Gun's <see cref="WeaponModel.GripLeft"/> (Grip_L): where the left palm is on the gun in the shooting pose
    /// (<see cref="GripPoseClip"/>), posed as in game — the left hand IK pulls the hand there in every other pose.</item>
    /// </list>
    /// </summary>
    internal static class WeaponsBuilder
    {
        private const string ModelsPath = "Assets/_Project/Art/Weapons";
        private const string PickupsPath = ModelsPath + "/Pickups";
        private const string MeleeFolder = "Melee";
        private const string AutomaticFolder = "Auto";
        private const string ModelPrefix = "SM_Wep_";
        private const string DataPath = "Assets/_Project/Data/Weapons";
        private const string CatalogPath = DataPath + "/WeaponVisuals.asset";
        private const string WeaponSetPath = "Assets/_Project/Data/Themes/PlaceholderWeaponSet.asset";

        /// <summary>Same as the characters, so a weapon on the floor is as big as in the hands.</summary>
        private const float PickupScale = 0.75f;

        private const float PickupYaw = 45f; // diagonal: the longest guns fit the cell

        /// <summary>Pose where the clip holds a gun properly with both hands: the left grip mark is taken from it.</summary>
        private const string GripPoseClip = "Player_rifle_weapon_shoot";
        private const float GripPoseTime = 0.5f; // normalized

        private sealed class Weapon
        {
            public string ModelPath;
            public WeaponDefinition Definition;
        }

        [MenuItem("Maze/Dev/Build Weapons")]
        public static void Build()
        {
            try
            {
                var catalog = LoadOrCreate<WeaponVisualCatalog>(CatalogPath);
                var weapons = CollectWeapons(catalog, out var created);
                var icons = new Dictionary<WeaponDefinition, Sprite>();
                foreach (var old in catalog.MutableWeapons)
                    if (old?.Definition != null && old.Icon != null) icons[old.Definition] = old.Icon;
                catalog.MutableWeapons.Clear();
                var set = AssetDatabase.LoadAssetAtPath<VisualSet>(WeaponSetPath);
                if (set == null)
                    throw new InvalidOperationException($"'{WeaponSetPath}' not found: run Maze → Dev → Create Placeholder Theme first.");
                set.MutableVariants.Clear();
                set.DefaultVariantId = null;

                var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                var visualsGroup = settings.FindGroup(LevelDesigner.LevelSync.VisualsGroup) ?? settings.DefaultGroup;
                EnsureFolder(PickupsPath);

                var pickups = new HashSet<string>();
                foreach (var weapon in weapons)
                {
                    var definition = weapon.Definition;
                    MarkModel(weapon.ModelPath, definition.Slot);
                    icons.TryGetValue(definition, out var icon);
                    catalog.MutableWeapons.Add(new WeaponVisualDefinition(definition, Reference(weapon.ModelPath), icon));

                    var pickupPath = BuildPickup(weapon.ModelPath);
                    pickups.Add(pickupPath);
                    var pickupGuid = AssetDatabase.AssetPathToGUID(pickupPath);
                    if (settings.FindAssetEntry(pickupGuid) == null)
                        settings.CreateOrMoveEntry(pickupGuid, visualsGroup);
                    set.MutableVariants.Add(new VisualVariant("weapon_" + definition.Id, definition: definition,
                        prefab: new AssetReferenceGameObject(pickupGuid)));
                }

                var removedPickups = DeleteUnusedPickups(pickups, settings);
                EditorUtility.SetDirty(catalog);
                EditorUtility.SetDirty(set);
                EditorUtility.SetDirty(settings);
                var playerPrefab = PlayerAnimationsBuilder.PlayerPrefabPath();
                RigPlayer(playerPrefab);
                MarkGrips(playerPrefab, weapons);
                var reassigned = ReassignLevels(set);
                var problem = LevelDesigner.LevelSync.SyncShared(settings);
                if (problem != null) Debug.LogWarning("[Maze] " + problem);
                AssetDatabase.SaveAssets();
                UiIconsBuilder.BuildWeaponIcons(catalog); // Models may have changed.
                WarnUnusedDefinitions(weapons);
                var news = created.Count > 0 ? $" ({string.Join(", ", created)}: draft values, tune them in {DataPath})" : "";
                var removed = removedPickups > 0 ? $", {removedPickups} unused removed" : "";
                Debug.Log($"[Maze] Weapons built: {weapons.Count} weapon(s), {created.Count} new{news}; held and pickup " +
                          $"prefabs{removed}, player rig; {reassigned} weapon visual(s) in levels reassigned.");
            }
            catch (Exception e)
            {
                Debug.LogError("[Maze] Building weapons failed: " + e.Message);
                throw;
            }
        }

        /// <summary>
        /// Every prefab under <see cref="ModelsPath"/> except pickups: the catalog's ones first in its order, new ones
        /// after them by path. A model in the catalog keeps its definition; a new one gets one by its name.
        /// </summary>
        private static List<Weapon> CollectWeapons(WeaponVisualCatalog catalog, out List<string> created)
        {
            var order = new List<string>();
            var known = new Dictionary<string, WeaponDefinition>();
            foreach (var entry in catalog.MutableWeapons)
            {
                var guid = entry?.HeldPrefab?.AssetGUID;
                if (entry?.Definition == null || string.IsNullOrEmpty(guid) || known.ContainsKey(guid)) continue;
                known[guid] = entry.Definition;
                order.Add(guid);
            }

            int Rank(string path)
            {
                var index = order.IndexOf(AssetDatabase.AssetPathToGUID(path));
                return index < 0 ? int.MaxValue : index;
            }

            var models = AssetDatabase.FindAssets("t:Prefab", new[] { ModelsPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.StartsWith(PickupsPath + "/", StringComparison.Ordinal))
                .OrderBy(Rank)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToList();
            if (models.Count == 0)
                throw new InvalidOperationException($"No weapon models in {ModelsPath}.");

            created = new List<string>();
            var weapons = new List<Weapon>();
            var ids = new Dictionary<string, string>();
            foreach (var path in models)
            {
                var slot = SlotOf(path);
                if (!known.TryGetValue(AssetDatabase.AssetPathToGUID(path), out var definition))
                {
                    definition = DefinitionFor(path, slot, out var isNew);
                    if (isNew) created.Add(definition.name);
                }

                if (definition.Slot != slot)
                    Debug.LogWarning($"[Maze] Weapon '{definition.name}' is {definition.Slot}, but its model '{path}' lies in " +
                                     $"a {slot} folder: the definition stays {definition.Slot}.");
                if (ids.TryGetValue(definition.Id, out var other))
                    throw new InvalidOperationException($"Weapons '{other}' and '{path}' have the same id '{definition.Id}': " +
                                                        "change the id in one of the definitions.");
                ids[definition.Id] = path;
                weapons.Add(new Weapon { ModelPath = path, Definition = definition });
            }

            return weapons;
        }

        private static WeaponSlot SlotOf(string modelPath) =>
            HasFolder(modelPath, MeleeFolder) ? WeaponSlot.Melee : WeaponSlot.Ranged;

        /// <summary>Whether a folder between <see cref="ModelsPath"/> and the file is <paramref name="folder"/>.</summary>
        private static bool HasFolder(string modelPath, string folder)
        {
            var parts = modelPath.Substring(ModelsPath.Length + 1).Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
                if (string.Equals(parts[i], folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// Data/Weapons/&lt;name&gt;.asset for a model not in the catalog. An existing asset of that name is used as it
        /// is (only an empty id is filled in); a new one gets the draft values of the model's kind.
        /// </summary>
        private static WeaponDefinition DefinitionFor(string modelPath, WeaponSlot slot, out bool isNew)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            if (name.StartsWith(ModelPrefix, StringComparison.Ordinal)) name = name.Substring(ModelPrefix.Length);
            var path = $"{DataPath}/{name}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            isNew = definition == null;
            if (!isNew && !string.IsNullOrEmpty(definition.Id))
                return definition;

            if (isNew)
            {
                definition = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var draft = DraftFor(slot, HasFolder(modelPath, AutomaticFolder));
            definition.Configure(IdOf(name), slot, draft.Magazine);
            definition.ConfigureCombat(draft.Damage, draft.AttackSpeed, draft.FireInterval, draft.ReloadTime, draft.Mode,
                draft.ProjectileSpeed, draft.MeleeRange, draft.MeleeArc, draft.SoundRadius);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private struct Draft
        {
            public float Damage, AttackSpeed, MeleeRange, MeleeArc, FireInterval, ReloadTime, ProjectileSpeed, SoundRadius;
            public int Magazine;
            public FireMode Mode;
        }

        /// <summary>Starting values of a new weapon by kind: zombies have 30 HP; the player's anims fit any cooldown.</summary>
        private static Draft DraftFor(WeaponSlot slot, bool automatic)
        {
            var draft = new Draft
            {
                AttackSpeed = 1f, MeleeRange = 1.2f, MeleeArc = 100f, FireInterval = 0.3f, ReloadTime = 2f,
                ProjectileSpeed = 25f, Magazine = 10, Mode = FireMode.Single,
            };
            if (slot == WeaponSlot.Melee)
            {
                draft.Damage = 12f;
                draft.AttackSpeed = 1.2f;
                draft.MeleeRange = 1.3f;
                draft.SoundRadius = 4f;
            }
            else if (automatic)
            {
                draft.Mode = FireMode.Automatic;
                draft.Damage = 8f;
                draft.FireInterval = 0.12f;
                draft.Magazine = 30;
                draft.SoundRadius = 7f;
            }
            else
            {
                draft.Damage = 20f;
                draft.FireInterval = 0.7f;
                draft.Magazine = 6;
                draft.ProjectileSpeed = 28f;
                draft.SoundRadius = 8f;
            }

            return draft;
        }

        /// <summary>Weapon id from an asset name: "FireAxe_01" → "fire_axe_01".</summary>
        internal static string IdOf(string name)
        {
            var id = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (!char.IsLetterOrDigit(c))
                {
                    if (id.Length > 0 && id[id.Length - 1] != '_') id.Append('_');
                    continue;
                }

                if (char.IsUpper(c) && i > 0 && char.IsLower(name[i - 1]) && id.Length > 0 && id[id.Length - 1] != '_')
                    id.Append('_');
                id.Append(char.ToLowerInvariant(c));
            }

            return id.ToString().Trim('_');
        }

        /// <summary>
        /// Pickup prefabs no weapon uses any more (a renamed or removed model) leave Pickups and Addressables, so they do
        /// not ship: the folder is this builder's own.
        /// </summary>
        private static int DeleteUnusedPickups(HashSet<string> used, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings)
        {
            var removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PickupsPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path)) continue;
                settings.RemoveAssetEntry(guid);
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }

            return removed;
        }

        /// <summary>Definitions in Data/Weapons without a model have no look in game: reported.</summary>
        private static void WarnUnusedDefinitions(List<Weapon> weapons)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition", new[] { DataPath }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && weapons.All(weapon => weapon.Definition != definition))
                    Debug.LogWarning($"[Maze] Weapon definition '{definition.name}' has no model in {ModelsPath}, so it has " +
                                     "no look in game: delete it or put its model back.");
            }
        }

        /// <summary>Adds <see cref="WeaponModel"/> with a mark at the far (+Z) end of the mesh: Muzzle for guns, Tip for melee.</summary>
        private static void MarkModel(string path, WeaponSlot slot)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var markName = slot == WeaponSlot.Ranged ? "Muzzle" : "Tip";
                var obsoleteName = slot == WeaponSlot.Ranged ? "Tip" : "Muzzle";
                var obsolete = root.transform.Find(obsoleteName);
                if (obsolete != null) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);

                var mark = root.transform.Find(markName);
                if (mark == null)
                {
                    mark = new GameObject(markName).transform;
                    mark.SetParent(root.transform, false);
                }

                mark.localPosition = FarEnd(root);
                mark.localRotation = Quaternion.identity;

                var model = root.GetComponent<WeaponModel>() ?? root.AddComponent<WeaponModel>();
                var serialized = new SerializedObject(model);
                serialized.FindProperty("_muzzle").objectReferenceValue = slot == WeaponSlot.Ranged ? mark : null;
                serialized.FindProperty("_tip").objectReferenceValue = slot == WeaponSlot.Melee ? mark : null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Centre of the mesh vertices within 3 cm of the farthest +Z point, in the root's space.</summary>
        private static Vector3 FarEnd(GameObject root)
        {
            var points = new List<Vector3>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                foreach (var vertex in mesh.vertices)
                    points.Add(root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
            }

            if (points.Count == 0)
            {
                // Not readable: the bounds' far face is close enough.
                var bounds = new Bounds();
                var first = true;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (first) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    first = false;
                }

                return root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z));
            }

            var maxZ = float.MinValue;
            foreach (var point in points) maxZ = Mathf.Max(maxZ, point.z);
            var sum = Vector3.zero;
            var count = 0;
            foreach (var point in points)
                if (point.z >= maxZ - 0.03f)
                {
                    sum += point;
                    count++;
                }

            var end = sum / count;
            end.z = maxZ;
            return end;
        }

        /// <summary>
        /// Art/Weapons/Pickups/&lt;model&gt;_Pickup: the held model lying on its side (seen in profile from above),
        /// diagonal, centred in the cell and resting on the floor. Rebuilt in place (GUID kept).
        /// </summary>
        private static string BuildPickup(string heldPath)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(heldPath);
            var path = $"{PickupsPath}/{name}_Pickup.prefab";
            var root = new GameObject(name + "_Pickup");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(heldPath), root.transform);
                model.transform.localScale = Vector3.one * PickupScale;
                model.transform.localRotation = Quaternion.Euler(0f, PickupYaw, 0f) * Quaternion.Euler(0f, 0f, 90f);
                model.transform.localPosition = Vector3.zero;

                var bounds = new Bounds();
                var first = true;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (first) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    first = false;
                }

                model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y + 0.005f, -bounds.center.z);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return path;
        }

        /// <summary>
        /// Palm centres under both hands and the melee socket under the right one, from finger bone positions in hand
        /// space: the grip runs along the knuckles toward the index finger (blade above the thumb), edge toward the
        /// fingers. The Synty hand has a thumb, an index and one merged finger.
        /// </summary>
        private static void RigPlayer(string playerPrefab)
        {
            var root = PrefabUtility.LoadPrefabContents(playerPrefab);
            try
            {
                var animator = root.GetComponent<Animator>();
                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (right == null || left == null)
                    throw new InvalidOperationException("The player avatar has no hand bones.");

                var palmRight = Child(right, "Palm_R");
                palmRight.localPosition = PalmCentre(animator, right, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal);
                palmRight.localRotation = Quaternion.identity;
                var palmLeft = Child(left, "Palm_L");
                palmLeft.localPosition = PalmCentre(animator, left, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal);
                palmLeft.localRotation = Quaternion.identity;

                var finger = right.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position);
                var index = right.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position);
                var forward = (index - finger).normalized;
                var up = Vector3.ProjectOnPlane(Vector3.Lerp(finger, index, 0.5f), forward).normalized;
                var socket = Child(right, "WeaponSocket_Melee");
                socket.localPosition = palmRight.localPosition;
                socket.localRotation = Quaternion.LookRotation(forward, up);

                var rig = root.GetComponent<CharacterWeaponRig>() ?? root.AddComponent<CharacterWeaponRig>();
                var serialized = new SerializedObject(rig);
                serialized.FindProperty("_meleeSocket").objectReferenceValue = socket;
                serialized.FindProperty("_palmRight").objectReferenceValue = palmRight;
                serialized.FindProperty("_palmLeft").objectReferenceValue = palmLeft;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, playerPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Grip_L of every gun: the left palm relative to the gun, in the shooting pose, posed as in game.</summary>
        private static void MarkGrips(string playerPrefab, List<Weapon> weapons)
        {
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefab));
            try
            {
                var rig = player.GetComponent<CharacterWeaponRig>();
                var clip = CharacterAnimationUtility.LoadClip(GripPoseClip);
                clip.SampleAnimation(player, clip.length * GripPoseTime);

                foreach (var weapon in weapons)
                {
                    if (weapon.Definition.Slot != WeaponSlot.Ranged) continue;
                    var path = weapon.ModelPath;

                    var gun = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), rig.PalmRight, false);
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                    gun.transform.rotation = rig.TwoHandedRotation();
                    var position = gun.transform.InverseTransformPoint(rig.PalmLeft.position);
                    var rotation = Quaternion.Inverse(gun.transform.rotation) * rig.PalmLeft.rotation;
                    UnityEngine.Object.DestroyImmediate(gun);

                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var mark = Child(root.transform, "Grip_L");
                        mark.localPosition = position;
                        mark.localRotation = rotation;
                        var serialized = new SerializedObject(root.GetComponent<WeaponModel>());
                        serialized.FindProperty("_gripLeft").objectReferenceValue = mark;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        private static Vector3 PalmCentre(Animator animator, Transform hand, HumanBodyBones finger, HumanBodyBones index)
        {
            var knuckles = Vector3.Lerp(animator.GetBoneTransform(finger).position, animator.GetBoneTransform(index).position, 0.5f);
            return Vector3.Lerp(Vector3.zero, hand.InverseTransformPoint(knuckles), 0.6f);
        }

        private static Transform Child(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>Weapons of saved levels whose visual no longer fits their definition get a new one.</summary>
        private static int ReassignLevels(VisualSet set)
        {
            var reassigned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:LevelData"))
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || level.VisualTheme == null || level.VisualTheme.GetSet(VisualKind.Weapon) != set)
                    continue;

                var changed = false;
                foreach (var weapon in level.Weapons)
                {
                    var variant = set.FindVariant(level.VisualData.GetObjectAssignment(weapon.Id).VariantId);
                    if (variant != null && variant.Matches(VisualCategory.General, weapon.Definition))
                        continue;
                    VisualAssigner.AssignObject(level, weapon);
                    changed = true;
                    reassigned++;
                }

                if (changed) EditorUtility.SetDirty(level);
            }

            return reassigned;
        }

        private static AssetReferenceGameObject Reference(string path) => new AssetReferenceGameObject(AssetDatabase.AssetPathToGUID(path));

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(folder.LastIndexOf('/') + 1));
        }
    }
}
