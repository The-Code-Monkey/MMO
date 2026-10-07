using System;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The Main Menu's login form: a username, a password, a sign-in button and a status line,
    /// built in code by <see cref="MainMenuFactory"/> and driven by the scene bootstrap.
    ///
    /// <para>
    /// The view is deliberately dumb — it forwards the raw entered values through
    /// <see cref="LoginSubmitted"/> and paints whatever status it is told. Validation is the
    /// connector's job, so a submit from anywhere (the button, a test, an enter key later) goes
    /// through exactly one decision point. Wiring is explicit (<see cref="SetFields"/>, "public so
    /// a factory or a test can wire them", house style) and idempotent: re-calling it replaces the
    /// fields and never stacks duplicate button listeners.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Login View")]
    public sealed class LoginView : MonoBehaviour
    {
        [SerializeField] private InputField _usernameField;
        [SerializeField] private InputField _passwordField;
        [SerializeField] private Button _loginButton;
        [SerializeField] private Text _statusText;

        private bool _wired;

        /// <summary>Raised when the player submits the form, with the raw username and password.</summary>
        public event Action<string, string> LoginSubmitted;

        public InputField UsernameField => _usernameField;
        public InputField PasswordField => _passwordField;
        public Button LoginButton => _loginButton;
        public Text StatusText => _statusText;

        public string Username => _usernameField != null ? _usernameField.text : string.Empty;
        public string Password => _passwordField != null ? _passwordField.text : string.Empty;

        /// <summary>
        /// Points the view at its fields and wires the submit. Idempotent: safe to call again (the
        /// factory does after creating, tests may re-wire with fakes).
        /// </summary>
        public void SetFields(InputField username, InputField password, Button login, Text status)
        {
            if (_wired && _loginButton != null)
                _loginButton.onClick.RemoveListener(Submit);

            _usernameField = username;
            _passwordField = password;
            _loginButton = login;
            _statusText = status;

            if (_loginButton != null)
                _loginButton.onClick.AddListener(Submit);

            _wired = true;
        }

        /// <summary>Shows a status line. Null clears it.</summary>
        public void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message ?? string.Empty;
        }

        /// <summary>Locks the form while a login is in flight.</summary>
        public void SetBusy(bool busy)
        {
            if (_loginButton != null)
                _loginButton.interactable = !busy;
        }

        /// <summary>The submit path, also wired to the button. Public so tests can drive it directly.</summary>
        public void Submit()
            => LoginSubmitted?.Invoke(Username, Password);

        private void OnDestroy()
        {
            if (_loginButton != null)
                _loginButton.onClick.RemoveListener(Submit);
        }
    }
}