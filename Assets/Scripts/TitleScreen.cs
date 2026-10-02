using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChompChompPanic
{
    /// <summary>
    /// The launch screen: shows the logo over a dark backdrop with a vertical menu underneath,
    /// and the game version (Player Settings > Version) in the bottom-right corner.
    /// Input and drawing are handled by <see cref="Menu"/>. Add entries in <see cref="BuildMenu"/>.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [SerializeField] Texture2D logo;
        [SerializeField, Tooltip("Scene loaded by Start Game (must be in the build profile's scene list)")]
        string gameScene = "SampleScene";
        [SerializeField] Color backgroundColor = new(0.04f, 0.04f, 0.08f);
        [SerializeField] Color itemColor = new(0.85f, 0.85f, 0.9f);
        [SerializeField, Tooltip("Selected entry, matched to the logo's orange")]
        Color selectedColor = new(1f, 0.72f, 0.15f);
        [SerializeField, Tooltip("Moving between entries (from ArtSource/Audio/build_sfx.py)")]
        AudioClip menuMoveSound;
        [SerializeField, Tooltip("Confirming an entry")]
        AudioClip menuSelectSound;
        [SerializeField, Range(0f, 1f)] float soundVolume = 0.6f;

        readonly Menu menu = new();
        GUIStyle footerStyle;
        GUIStyle versionStyle;

        void Awake()
        {
            Time.timeScale = 1f;
            var cam = Camera.main;
            if (cam != null)
                cam.backgroundColor = backgroundColor;
            MusicPlayer.Stop();
            menu.MoveSound = menuMoveSound;
            menu.SelectSound = menuSelectSound;
            menu.SoundVolume = soundVolume;
            menu.ItemColor = itemColor;
            menu.SelectedColor = selectedColor;
            BuildMenu();
        }

        void BuildMenu()
        {
            var items = new List<Menu.Item>
            {
                Menu.Button("Start Game", () => SceneManager.LoadScene(gameScene)),
                Menu.Button("Options", () => menu.ShowOptions(BuildMenu)),
            };
            if (Menu.CanQuit)
                items.Add(Menu.Button("Quit", Menu.QuitGame));
            menu.Show(null, null, items.ToArray());
        }

        void Update() => menu.Update();

        void OnGUI()
        {
            if (footerStyle == null)
            {
                footerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter };
                footerStyle.normal.textColor = Color.white;
                versionStyle = new GUIStyle(footerStyle) { alignment = TextAnchor.LowerRight };
            }
            float h = Screen.height;
            footerStyle.fontSize = Mathf.RoundToInt(h * 0.022f);
            versionStyle.fontSize = footerStyle.fontSize;

            // Logo: fit into the top ~62% of the screen, bobbing gently.
            float logoBottom = h * 0.64f;
            if (logo != null)
            {
                float aspect = (float)logo.width / logo.height;
                float logoH = Mathf.Min(logoBottom - h * 0.02f, Screen.width * 0.9f / aspect);
                float logoW = logoH * aspect;
                float bob = Mathf.Sin(Time.unscaledTime * 1.6f) * h * 0.006f;
                GUI.DrawTexture(new Rect((Screen.width - logoW) * 0.5f, logoBottom - logoH + bob, logoW, logoH), logo, ScaleMode.ScaleToFit);
            }

            // Menu entries, stacked under the logo. There's no room for a heading on the options page here.
            menu.Draw(logoBottom + h * 0.03f, showTitle: false);

            Menu.DrawShadowed(new Rect(0, 0, Screen.width, h * 0.98f), Menu.Hint, footerStyle, new Color(1f, 1f, 1f, 0.45f));
            Menu.DrawShadowed(new Rect(0, 0, Screen.width - h * 0.02f, h * 0.98f), $"v{Application.version}", versionStyle,
                new Color(1f, 1f, 1f, 0.3f));
        }
    }
}
