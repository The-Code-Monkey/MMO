using System;
using System.IO;
using UnityEngine;

namespace MMO.Client
{
    /// <summary>
    /// Reads and writes <see cref="SettingsModel"/> as JSON on the player's machine.
    ///
    /// <para>
    /// A missing, unreadable or malformed file is never fatal: every failure falls back to the
    /// shipped defaults and logs once, so a bad settings file cannot stop the client from
    /// starting. The path is injectable so tests use a temporary file instead of the real
    /// <c>persistentDataPath</c>.
    /// </para>
    /// </summary>
    public sealed class SettingsStore
    {
        public const string FileName = "settings.json";

        private readonly string _path;

        /// <summary>Creates a store for <paramref name="path"/>, or the standard file when null.</summary>
        public SettingsStore(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, FileName)
                : path;
        }

        /// <summary>The file this store reads and writes.</summary>
        public string FilePath => _path;

        /// <summary>
        /// Loads the saved settings, or the defaults when the file is absent or unusable.
        /// Never throws.
        /// </summary>
        public SettingsModel Load(int qualityLevelCount = -1)
        {
            if (!File.Exists(_path))
                return Defaults(qualityLevelCount);

            try
            {
                return LoadFromJson(File.ReadAllText(_path), qualityLevelCount);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SettingsStore] Could not read '{_path}'; using defaults. {exception.Message}");
                return Defaults(qualityLevelCount);
            }
        }

        /// <summary>
        /// Saves a sanitized copy of <paramref name="settings"/>. A write failure is logged and
        /// swallowed, because losing preferences must not interrupt play.
        /// </summary>
        public void Save(SettingsModel settings, int qualityLevelCount = -1)
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_path, ToJson(settings, qualityLevelCount));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SettingsStore] Could not write '{_path}'. {exception.Message}");
            }
        }

        /// <summary>
        /// Parses JSON into a sanitized model. Malformed input returns the defaults; never throws.
        /// </summary>
        public static SettingsModel LoadFromJson(string json, int qualityLevelCount = -1)
        {
            try
            {
                SettingsModel model = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonUtility.FromJson<SettingsModel>(json);

                return SettingsRules.Sanitize(model ?? SettingsModel.Defaults(), QualityCount(qualityLevelCount));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SettingsStore] Invalid settings JSON; using defaults. {exception.Message}");
                return Defaults(qualityLevelCount);
            }
        }

        /// <summary>Serializes a sanitized copy of <paramref name="settings"/>.</summary>
        public static string ToJson(SettingsModel settings, int qualityLevelCount = -1)
        {
            SettingsModel sanitized = SettingsRules.Sanitize(
                (settings ?? SettingsModel.Defaults()).Clone(), QualityCount(qualityLevelCount));

            return JsonUtility.ToJson(sanitized, prettyPrint: true);
        }

        private static SettingsModel Defaults(int qualityLevelCount)
            => SettingsRules.Sanitize(SettingsModel.Defaults(), QualityCount(qualityLevelCount));

        private static int QualityCount(int qualityLevelCount)
            => qualityLevelCount >= 0 ? qualityLevelCount : QualitySettings.names.Length;
    }
}
