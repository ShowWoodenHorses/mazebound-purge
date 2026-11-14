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
                so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);
            }
        }
    }
}
