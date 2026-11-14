using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Purge.Core.Definitions;
namespace Purge.Editor.Dev
{
    /// <summary>Imports model prices into editable definition fields. Runtime never reads CSV.</summary>
    internal static class ArsenalBuilder
    {
        [MenuItem("Purge/Dev/Build Arsenal")]
        public static void Build()
        {
            Apply("prices.csv",true);Apply("throwables.csv",false);AssetDatabase.SaveAssets();
        }
        private static void SetFork(SerializedProperty fork, ArsenalClass category, bool upper)
        {
            // Existing overrides are authored data, preserved on every rebuild.
            if(fork.FindPropertyRelative("First").enumValueIndex!=0 || fork.FindPropertyRelative("Second").enumValueIndex!=0)return;
            WeaponUpgrade first,second;
            switch(category)
            {
                case ArsenalClass.Pistols:first=upper?WeaponUpgrade.FullMagazineCritical:WeaponUpgrade.Pierce;second=upper?WeaponUpgrade.Ricochet:WeaponUpgrade.DoubleShot;break;
                case ArsenalClass.Rifles:first=upper?WeaponUpgrade.ExplosiveRounds:WeaponUpgrade.Magazine;second=upper?WeaponUpgrade.Burn:WeaponUpgrade.Ramp;break;
                case ArsenalClass.Shotguns:first=upper?WeaponUpgrade.Burn:WeaponUpgrade.Knockback;second=upper?WeaponUpgrade.Pierce:WeaponUpgrade.Wide;break;
                case ArsenalClass.Snipers:first=upper?WeaponUpgrade.FullMagazineCritical:WeaponUpgrade.Pierce;second=upper?WeaponUpgrade.ExplosiveRounds:WeaponUpgrade.Silent;break;
                case ArsenalClass.Canisters:first=upper?WeaponUpgrade.Range:WeaponUpgrade.Wide;second=WeaponUpgrade.Duration;break;
                case ArsenalClass.Energy:first=upper?WeaponUpgrade.Electrify:WeaponUpgrade.Chain;second=upper?WeaponUpgrade.Pierce:WeaponUpgrade.Magazine;break;
                case ArsenalClass.Charges:first=upper?WeaponUpgrade.Cluster:WeaponUpgrade.Radius;second=upper?WeaponUpgrade.Burn:WeaponUpgrade.Magazine;break;
                default:first=upper?WeaponUpgrade.Lifesteal:WeaponUpgrade.Wide;second=upper?WeaponUpgrade.Shatter:WeaponUpgrade.Knockback;break;
            }
            fork.FindPropertyRelative("First").enumValueIndex=(int)first;fork.FindPropertyRelative("Second").enumValueIndex=(int)second;
        }
        private static void Apply(string file,bool weapon)
        {
            var rows=File.ReadAllLines(Path.Combine("Tools/Balance/Model",file));
            for(int i=1;i<rows.Length;i++)
            {
                if(string.IsNullOrWhiteSpace(rows[i]))continue;var cells=rows[i].Split(',');
                var guids=AssetDatabase.FindAssets(cells[0]+" t:"+(weapon?"WeaponDefinition":"ThrowableDefinition"),new[]{"Assets/_Project/Data"});
                UnityEngine.Object asset=null;
                foreach(var guid in guids) { var path=AssetDatabase.GUIDToAssetPath(guid);if(Path.GetFileNameWithoutExtension(path)==cells[0]){asset=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);break;} }
                if(asset==null)throw new InvalidOperationException("Missing arsenal item: "+cells[0]);
                var so=new SerializedObject(asset);so.FindProperty("_purchasePrice").intValue=int.TryParse(cells[2],out var price)?price:0;
                if(weapon)so.FindProperty("_uniqueReward").boolValue=cells[3]=="1";
                var costs = so.FindProperty("_upgradeCosts");
                costs.arraySize = weapon ? 9 : 4;
                if (weapon)
                {
                    var upgrades = File.ReadAllLines("Tools/Balance/Model/upgrades.csv");
                    for (int j = 1; j < upgrades.Length; j++)
                    {
                        var row = upgrades[j].Split(',');
                        if (row.Length < 3 || row[0] != cells[1]) continue;
                        int level = int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture);
                        if (level >= 2 && level <= 10)
                            costs.GetArrayElementAtIndex(level - 2).intValue = int.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                else for (int j = 0; j < 4; j++)
                    costs.GetArrayElementAtIndex(j).intValue = int.Parse(cells[j + 3], System.Globalization.CultureInfo.InvariantCulture);
                var damage = so.FindProperty("_upgradeDamage");
                damage.arraySize = weapon ? 9 : 5;
                if (weapon)
                {
                    foreach(var line in File.ReadAllLines("Tools/Balance/Model/upgrades.csv"))
                    {
                        var row=line.Split(','); if(row.Length<5 || row[0]!=cells[1])continue;
                        int level=int.Parse(row[1]); damage.GetArrayElementAtIndex(level-2).floatValue=float.Parse(row[4],System.Globalization.CultureInfo.InvariantCulture);
                    }
                    var parameterRows=File.ReadAllLines("Tools/Balance/Model/weapon_modifiers.csv");
                    var parameters=so.FindProperty("_upgradeParameters");parameters.arraySize=parameterRows.Length-1;
                    for(int k=1;k<parameterRows.Length;k++){var row=parameterRows[k].Split(',');var value=parameters.GetArrayElementAtIndex(k-1);value.FindPropertyRelative("Name").stringValue=row[0];value.FindPropertyRelative("Value").floatValue=float.Parse(row[1],System.Globalization.CultureInfo.InvariantCulture);}
                    so.FindProperty("_upgradeBurn").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Purge.Core.Effects.StatusDefinition>("Assets/_Project/Data/Effects/Status_Burning.asset");
                    so.FindProperty("_upgradeFreeze").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Purge.Core.Effects.StatusDefinition>("Assets/_Project/Data/Effects/Status_Freeze.asset");
                    var category=((WeaponDefinition)asset).ArsenalClass;
                    SetFork(so.FindProperty("_fork4"),category,false);
                    SetFork(so.FindProperty("_fork8"),category,true);
                }
                else
                {
                    var radius=so.FindProperty("_upgradeRadius");var stack=so.FindProperty("_upgradeStack");radius.arraySize=stack.arraySize=5;
                    var rowsUpgrade=File.ReadAllLines("Tools/Balance/Model/throwable_upgrades.csv");
                    for(int j=1;j<rowsUpgrade.Length;j++)
                    {
                        var row=rowsUpgrade[j].Split(',');int index=int.Parse(row[0])-1;
                        damage.GetArrayElementAtIndex(index).floatValue=float.Parse(row[1],System.Globalization.CultureInfo.InvariantCulture);
                        radius.GetArrayElementAtIndex(index).floatValue=float.Parse(row[2],System.Globalization.CultureInfo.InvariantCulture);
                        stack.GetArrayElementAtIndex(index).intValue=int.Parse(row[3]);
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);
            }
        }
    }
}
