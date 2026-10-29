using MMO.Bootstrap;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the whole Main Menu in code: a screen-space canvas carrying the login form — title,
    /// username, password, sign-in button and a status line — wired into a fresh
    /// <see cref="LoginView"/>. Nothing here loads a prefab or a scene: the project commits neither
    /// UI asset, so a scene, a fresh process and a test must produce identical objects (the same
    /// reason <see cref="NetworkHudFactory"/> and the network prefab factories all build in code).
    ///
    /// <para>
    /// The scene layer (see <see cref="MainMenuSceneBaker"/>) keeps the menu's UI out of scene files
    /// entirely: the scene is this factory plus a <see cref="MainMenuBootstrap"/> wired to the
    /// NetworkManager.
    /// </para>
    /// </summary>
    public static class MainMenuFactory
    {
        /// <summary>Name of the root canvas object.</summary>
        public const string MainMenuName = "MainMenu";

        public static readonly Color BackgroundColor = new(0.05f, 0.06f, 0.09f, 1f);
        public static readonly Color PanelColor = new(0f, 0f, 0f, 0.55f);
        public static readonly Color FieldColor = new(0.12f, 0.13f, 0.16f, 0.95f);
        public static readonly Color AccentColor = new(0f, 0.45f, 0.9f, 1f);
        public static readonly Color PlaceholderColor = new(0.65f, 0.68f, 0.72f, 1f);
        public static readonly Color StatusColor = new(0.82f, 0.86f, 0.9f, 1f);

        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;

        /// <summary>
        /// Builds the menu and returns its <see cref="LoginView"/> (mounted on the canvas root, so
        /// destroying the view destroys the whole menu). The bootstrap that responds to submits is
        /// the caller's job.
        /// </summary>
        /// <param name="createEventSystem">
        /// True to ensure a clickable event router exists (one is created only when the scene has
        /// none). Tests that drive the button handler directly pass false.
        /// </param>
        public static LoginView Create(bool createEventSystem = true)
        {
            GameObject root = new GameObject(MainMenuName, typeof(RectTransform));

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            Image backdrop = root.AddComponent<Image>();
            backdrop.color = BackgroundColor;

            GameObject panel = UIBuilder.Rect(
                root.transform, "LoginPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 520f));
            panel.AddComponent<Image>().color = PanelColor;

            CreateTitle(panel.transform);
            CreateLabel(panel.transform, "UsernameLabel", new Vector2(0f, 156f), "Username");
            InputField username = CreateInputField(panel.transform, "UsernameInput", new Vector2(0f, 112f), "Name or handle", false);
            CreateLabel(panel.transform, "PasswordLabel", new Vector2(0f, 50f), "Password");
            InputField password = CreateInputField(panel.transform, "PasswordInput", new Vector2(0f, 6f), "At least 8 characters", true);
            Button button = CreateButton(panel.transform, "LoginButton", new Vector2(0f, -66f));
            Text status = CreateStatus(panel.transform);

            LoginView view = root.AddComponent<LoginView>();
            view.SetFields(username, password, button, status);

            if (createEventSystem)
                HudEventSystem.Ensure();

            return view;
        }

        private static void CreateTitle(Transform parent)
        {
            GameObject title = UIBuilder.Rect(
                parent, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -5f), new Vector2(520f, 90f));
            Text text = title.AddComponent<Text>();
            UIBuilder.ApplyLabel(text, 46, TextAnchor.MiddleCenter, 9);
            text.text = "AethelBourne Online";
            text.fontStyle = FontStyle.Bold;
        }

        private static void CreateLabel(Transform parent, string name, Vector2 position, string text)
        {
            GameObject labelObject = UIBuilder.Rect(
                parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, new Vector2(470f, 28f));
            Text label = labelObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(label, 18, TextAnchor.MiddleLeft, 9);
            label.text = text;
            label.color = new Color(0.85f, 0.87f, 0.9f, 1f);
        }

        private static InputField CreateInputField(
            Transform parent, string name, Vector2 position, string placeholder, bool isPassword)
        {
            GameObject fieldObject = UIBuilder.Rect(
                parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, new Vector2(500f, 52f));

            Image background = fieldObject.AddComponent<Image>();
            background.color = FieldColor;

            GameObject textObject = UIBuilder.Inset(fieldObject.transform, "Text", new Vector2(12f, 4f));
            Text text = textObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(text, 20, TextAnchor.MiddleLeft, 9);
            text.supportRichText = false;

            GameObject placeholderObject = UIBuilder.Inset(fieldObject.transform, "Placeholder", new Vector2(12f, 4f));
            Text placeholderText = placeholderObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(placeholderText, 20, TextAnchor.MiddleLeft, 9);
            placeholderText.supportRichText = false;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = PlaceholderColor;
            placeholderText.text = placeholder;

            InputField field = fieldObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.targetGraphic = background;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = isPassword ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.text = string.Empty;

            return field;
        }

        private static Button CreateButton(Transform parent, string name, Vector2 position)
        {
            GameObject buttonObject = UIBuilder.Rect(
                parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                position, new Vector2(260f, 58f));

            Image background = buttonObject.AddComponent<Image>();
            background.color = AccentColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject labelObject = UIBuilder.Stretch(buttonObject.transform, "Label");
            Text label = labelObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(label, 24, TextAnchor.MiddleCenter, 9);
            label.text = "Sign In";
            label.fontStyle = FontStyle.Bold;

            return button;
        }

        private static Text CreateStatus(Transform parent)
        {
            GameObject statusObject = UIBuilder.Rect(
                parent, "Status", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f), new Vector2(520f, 88f));
            Text status = statusObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(status, 18, TextAnchor.MiddleCenter, 9);
            status.color = StatusColor;
            return status;
        }

    }
}
