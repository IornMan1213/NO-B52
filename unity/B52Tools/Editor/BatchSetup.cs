using System;
using UnityEditor;
using UnityEngine;
using Blueprinter;

namespace B52Tools
{
    // Headless versions of Blueprinter > Project Setup steps. Each step is a separate
    // -executeMethod run because importing assemblies forces a recompile/domain reload.
    public static class BatchSetup
    {
        const string GameExe = @"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\NuclearOption.exe";
        const string RippedAssets = @"C:\Users\jayea\no-ripped\NuclearOption\ExportedProject\Assets";
        const string GameVersion = "0.34.1";

        public static void Step2ImportAssemblies()
        {
            EditorPrefs.SetString(BlueprinterSettings.GameExecutablePrefsKey, GameExe);
            if (!GameAssemblies.TryGetManagedFolder(GameExe, out var managed))
                throw new Exception("Managed folder not found for " + GameExe);
            GameAssemblies.Import(managed, GameVersion);
            Debug.Log("[B52] Step2 done. Installed=" + GameAssemblies.IsInstalled);
        }

        public static void Step3ImportAssets()
        {
            if (!AssetRipperImporter.IsAssetsFolder(RippedAssets))
                throw new Exception("Not an AssetRipper Assets folder: " + RippedAssets);
            AssetRipperImporter.Import(RippedAssets);
            Debug.Log("[B52] Step3 done.");
        }

        public static void Step4RefreshOps()
        {
            OpReferenceIndex.Refresh();
            Debug.Log("[B52] Step4 done.");
        }

        public static void Step5BuildDoNotShip()
        {
            ModBuilder.BuildGameAssets();
            Debug.Log("[B52] Step5 done.");
        }
    }
}
