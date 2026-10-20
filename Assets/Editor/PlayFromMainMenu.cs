using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Makes the editor's Play button always boot into the Main Menu scene, whichever zone scene
    /// happens to be open. The game only starts correctly from the menu (it owns the connection
    /// gate and the zone hand-off), so starting Play inside a zone scene produces a broken session.
    ///
    /// <para>
    /// Editor-only by construction: <see cref="EditorSceneManager.playModeStartScene"/> is honoured
    /// solely by the Play button, so dedicated server builds — which load their zone scene from the
    /// command line — are unaffected. Skipped in batch mode so CI runs and PlayMode tests keep
    /// their own scene control.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMainMenu
    {
        static PlayFromMainMenu()
        {
            if (Application.isBatchMode)
                return;

            SceneAsset menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuSceneBaker.ScenePath);
            if (menu != null)
                EditorSceneManager.playModeStartScene = menu;
        }
    }
}
