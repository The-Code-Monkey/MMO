using System;
using System.IO;
using System.Text;
using MMO.Inventory;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MMO.Editor
{
    /// <summary>
    /// Imports the item artwork as UI sprites and wires each shipped <see cref="ItemDefinition"/>
    /// to the icon whose file name matches its item name. An item with no matching file keeps a
    /// null icon, so the UI falls back to text instead of showing an empty square.
    /// </summary>
    public static class ItemIconBaker
    {
        public const string MenuPath = "Tools/MMO/Rebuild Item Icons";
        public const string TextureFolder = "Assets/Textures/Items";
        public const string ItemFolder = "Assets/Resources/Items";

        [MenuItem(MenuPath)]
        public static void Rebuild()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Exit Play Mode before rebuilding item icons.");
                return;
            }

            GroundAssetBaker.EnsureFolder(TextureFolder);

            int assigned = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemFolder }))
            {
                string itemPath = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                if (item == null)
                    continue;

                Sprite icon = LoadIcon(item.ItemName);
                if (icon == null)
                    continue;

                var serialized = new SerializedObject(item);
                serialized.FindProperty("_icon").objectReferenceValue = icon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                assigned++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Rebuilt item icons: {assigned} item(s) wired to {TextureFolder}.");
        }

        private static Sprite LoadIcon(string itemName)
        {
            string key = FileKey(itemName);
            foreach (string extension in new[] { ".jpeg", ".jpg", ".png" })
            {
                string path = TextureFolder + "/" + key + extension;
                if (!File.Exists(AbsolutePath(path)))
                    continue;

                ConfigureSprite(path);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return null;
        }

        internal static string FileKey(string itemName)
        {
            var builder = new StringBuilder(itemName.Length);
            bool separator = false;
            foreach (char character in itemName.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    if (separator && builder.Length > 0)
                        builder.Append('_');
                    builder.Append(character);
                    separator = false;
                }
                else
                {
                    separator = true;
                }
            }

            return builder.ToString();
        }

        private static void ConfigureSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            if (importer == null)
                throw new InvalidOperationException($"Item icon missing or not importable at '{path}'.");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
        }

        private static string AbsolutePath(string assetPath)
            => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", assetPath);
    }
}
