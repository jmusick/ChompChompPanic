using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChompChompPanic
{
    /// <summary>
    /// The launch screen: shows the logo over a dark backdrop with a vertical menu underneath,
    /// and the company credit and game version (Player Settings > Company Name and Version) in the bottom-right corner.
    /// Once a second kaiju is unlocked, Start Game opens a kaiju select page that shows the highlighted kaiju in place of the logo.
    /// Input and drawing are handled by <see cref="Menu"/>. Add entries in <see cref="BuildMenu"/>.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [SerializeField] Texture2D logo;
        [SerializeField, Tooltip("Scene loaded by Start Game (must be in the build profile's scene list)")]
        string gameScene = "SampleScene";
        [SerializeField, Tooltip("Playable kaiju (Assets/Data/KaijuRoster.asset); without it Start Game goes straight in")]
        KaijuRoster kaijuRoster;
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
        GUIStyle headingStyle;
        /// <summary>The kaiju on the select page, in menu order (Back follows them); null on the other pages.</summary>
        List<PlayableKaiju> choices;

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
            choices = null;
            var items = new List<Menu.Item>
            {
                Menu.Button("Start Game", StartGame),
                Menu.Button("Options", () => menu.ShowOptions(BuildMenu)),
            };
            if (Menu.CanQuit)
                items.Add(Menu.Button("Quit", Menu.QuitGame));
            menu.Show(null, null, items.ToArray());
        }

        void StartGame()
        {
            var unlocked = kaijuRoster != null ? kaijuRoster.Unlocked : null;
            if (unlocked is { Count: > 1 })
                ShowKaijuSelect(unlocked);
            else
                SceneManager.LoadScene(gameScene);
        }

        void ShowKaijuSelect(List<PlayableKaiju> unlocked)
        {
            choices = unlocked;
            var items = new List<Menu.Item>();
            foreach (var kaiju in unlocked)
                items.Add(Menu.Button(kaiju.Name, () => Play(kaiju)));
            items.Add(Menu.Button("Back", BuildMenu));
            menu.Show("Choose Your Kaiju", BuildMenu, items.ToArray());
            menu.Selected = Mathf.Max(0, unlocked.IndexOf(kaijuRoster.Selected));
        }

        void Play(PlayableKaiju kaiju)
        {
            kaijuRoster.Selected = kaiju;
            // SoundPlayer survives the scene load, so the roar carries into the game.
            SoundPlayer.Play(kaiju.RoarSound, soundVolume);
            SceneManager.LoadScene(gameScene);
        }

        void Update() => menu.Update();

        void OnGUI()
        {
            if (footerStyle == null)
            {
                footerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter };
                footerStyle.normal.textColor = Color.white;
                versionStyle = new GUIStyle(footerStyle) { alignment = TextAnchor.LowerRight };
                headingStyle = new GUIStyle(footerStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            float h = Screen.height;
            footerStyle.fontSize = Mathf.RoundToInt(h * 0.022f);
            versionStyle.fontSize = footerStyle.fontSize;

            // Logo (or the highlighted kaiju): fit into the top ~62% of the screen, bobbing gently.
            // The kaiju select page has more entries, so its picture leaves the menu more room.
            float logoBottom = choices != null ? h * 0.53f : h * 0.64f;
            if (choices != null)
                DrawKaiju(logoBottom);
            else if (logo != null)
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
            Menu.DrawShadowed(new Rect(0, 0, Screen.width - h * 0.02f, h * 0.95f), $"© {Application.companyName}", versionStyle,
                new Color(1f, 1f, 1f, 0.3f));
            Menu.DrawShadowed(new Rect(0, 0, Screen.width - h * 0.02f, h * 0.98f), $"v{Application.version}", versionStyle,
                new Color(1f, 1f, 1f, 0.3f));
        }

        /// <summary>The select page's heading, how many kaiju are still locked, and the highlighted kaiju's idle animation, big, above the menu.</summary>
        void DrawKaiju(float bottom)
        {
            float h = Screen.height;
            headingStyle.fontSize = Mathf.RoundToInt(h * 0.07f);
            Menu.DrawShadowed(new Rect(0, h * 0.02f, Screen.width, h * 0.1f), menu.Title, headingStyle, Color.white);
            int locked = kaijuRoster.LockedCount;
            if (locked > 0)
            {
                string more = locked == 1 ? "1 more kaiju" : $"{locked} more kaiju";
                Menu.DrawShadowed(new Rect(0, 0, Screen.width, h * 0.15f), $"Survive a session to unlock the next one ({more} to find)",
                    footerStyle, new Color(1f, 0.72f, 0.15f, 0.8f));
            }

            if (menu.Selected >= choices.Count)
                return;
            var sprites = choices[menu.Selected].Sprites;
            var frame = sprites.Idle[Mathf.FloorToInt(Time.unscaledTime * sprites.IdleFps) % sprites.Idle.Length];
            float size = bottom - h * 0.17f;
            var rect = new Rect((Screen.width - size) * 0.5f, h * 0.16f, size, size);
            var texture = frame.texture;
            var uv = frame.textureRect;
            GUI.DrawTextureWithTexCoords(rect, texture,
                new Rect(uv.x / texture.width, uv.y / texture.height, uv.width / texture.width, uv.height / texture.height));
        }
    }
}
