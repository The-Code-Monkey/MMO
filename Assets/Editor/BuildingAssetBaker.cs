using System;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the generic wall and roof-tile maps and builds their URP Lit materials. The kit
    /// prefabs stay shared; <see cref="GrayboxZoneGeometry"/> applies these materials to the town
    /// building instances, so boundary walls and scatter pieces keep their dev look.
    /// </summary>
    public static class BuildingAssetBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Building Assets";

        public const string TextureFolder = "Assets/Textures/Building";
        public const string MaterialFolder = "Assets/Materials/Building";

        public const string WallAlbedoPath = TextureFolder + "/Wall_Generic_Albedo.png";
        public const string WallNormalPath = TextureFolder + "/Wall_Generic_Normal.png";
        public const string WallMaskPath = TextureFolder + "/Wall_Generic_Mask.png";
        public const string WallMaterialPath = MaterialFolder + "/Wall_Generic.mat";

        public const string RoofAlbedoPath = TextureFolder + "/RoofTiles_Albedo.png";
        public const string RoofNormalPath = TextureFolder + "/RoofTiles_Normal.png";
        public const string RoofMaskPath = TextureFolder + "/RoofTiles_Mask.png";
        public const string RoofMaterialPath = MaterialFolder + "/Roof_Tiles.mat";

        [MenuItem(MenuPath)]
        public static void RebuildBuildingAssets()
        {
            GroundAssetBaker.EnsureFolder(TextureFolder);
            GroundAssetBaker.EnsureFolder(MaterialFolder);

            RebuildSurface("wall", WallAlbedoPath, WallNormalPath, WallMaskPath, WallMaterialPath, "Wall_Generic");
            RebuildSurface("roof", RoofAlbedoPath, RoofNormalPath, RoofMaskPath, RoofMaterialPath, "Roof_Tiles");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Rebuilt building assets: {WallMaterialPath}, {RoofMaterialPath}");
        }

        private static void RebuildSurface(
            string surfaceName,
            string albedoPath,
            string normalPath,
            string maskPath,
            string materialPath,
            string materialName)
        {
            GroundAssetBaker.ConfigureTexture(albedoPath, normalMap: false, srgb: true, alpha: false);
            GroundAssetBaker.ConfigureTexture(normalPath, normalMap: true, srgb: false, alpha: false);
            GroundAssetBaker.ConfigureTexture(maskPath, normalMap: false, srgb: false, alpha: true);
            GroundAssetBaker.EnsureMaterial(materialPath, materialName, albedoPath, normalPath, maskPath);
            Debug.Log($"Rebuilt {surfaceName} material: {materialPath}");
        }
    }
}
