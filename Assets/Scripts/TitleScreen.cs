using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ChompChompPanic
{
    /// <summary>
    /// The launch screen: shows the logo over a dark backdrop with a vertical menu underneath.
    /// Navigate with arrows / WASD / d-pad / left stick or the mouse; confirm with Enter, Space, click or the south button.
    /// Add entries in <see cref="BuildMenu"/>.
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

        struct MenuItem
        {
            public string Label;
            public Action Run;
        }

        readonly List<MenuItem> items = new();
        int selected;
        float stickRepeatAt;
        Vector2 lastMouse;
        GUIStyle itemStyle;
        GUIStyle footerStyle;

        void Awake()
        {
            Time.timeScale = 1f;
            var cam = Camera.main;
            if (cam != null)
                cam.backgroundColor = backgroundColor;
            BuildMenu();
        }

        void BuildMenu()
        {
            items.Add(new MenuItem { Label = "Start Game", Run = () => SceneManager.LoadScene(gameScene) });
#if !UNITY_WEBGL
            items.Add(new MenuItem { Label = "Quit", Run = Quit });
#endif
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            int move = 0;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) move--;
                if (pad.dpad.down.wasPressedThisFrame) move++;

                // Left stick: step once when pushed, then repeat while held.
                float y = pad.leftStick.y.ReadValue();
                if (Mathf.Abs(y) < 0.5f)
                    stickRepeatAt = 0f;
                else if (Time.unscaledTime >= stickRepeatAt)
                {
                    move += y > 0f ? -1 : 1;
                    stickRepeatAt = Time.unscaledTime + (stickRepeatAt == 0f ? 0.4f : 0.15f);
                }
            }

            if (move != 0)
                Select((selected + move + items.Count) % items.Count);

            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (confirm)
                Confirm(selected);
        }

        void Select(int index)
        {
            if (index == selected)
                return;
            selected = index;
            SoundPlayer.Play(menuMoveSound, soundVolume);
        }

        void Confirm(int index)
        {
            selected = index;
            SoundPlayer.Play(menuSelectSound, soundVolume, 1f, 0f);
            items[index].Run();
        }

        void OnGUI()
        {
            if (itemStyle == null)
            {
                itemStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                footerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter };
                itemStyle.normal.textColor = footerStyle.normal.textColor = Color.white;
            }
            float h = Screen.height;
            itemStyle.fontSize = Mathf.RoundToInt(h * 0.05f);
            footerStyle.fontSize = Mathf.RoundToInt(h * 0.022f);

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

            // Menu entries, stacked under the logo.
            float rowH = h * 0.075f;
            float rowW = Mathf.Min(Screen.width * 0.8f, h * 0.6f);
            // Runtime IMGUI doesn't reliably get MouseMove events, so hover follows any change in mouse position.
            var mouse = Event.current.mousePosition;
            bool mouseMoved = Event.current.type == EventType.Repaint && mouse != lastMouse;
            if (Event.current.type == EventType.Repaint)
                lastMouse = mouse;
            for (int i = 0; i < items.Count; i++)
            {
                var row = new Rect((Screen.width - rowW) * 0.5f, logoBottom + h * 0.03f + i * rowH, rowW, rowH);
                if (mouseMoved && row.Contains(mouse))
                    Select(i);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(mouse))
                {
                    Event.current.Use();
                    Confirm(i);
                    return;
                }

                bool isSelected = i == selected;
                float pulse = isSelected ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.04f : 1f;
                string label = isSelected ? $"> {items[i].Label} <" : items[i].Label;
                itemStyle.fontSize = Mathf.RoundToInt(h * 0.05f * pulse);
                DrawShadowed(row, label, itemStyle, isSelected ? selectedColor : itemColor);
            }

            DrawShadowed(new Rect(0, 0, Screen.width, h * 0.98f), "Arrows / WASD / D-pad to choose, Enter / Space / A to confirm",
                footerStyle, new Color(1f, 1f, 1f, 0.45f));
        }

        static void DrawShadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = GUI.contentColor;
            float offset = Mathf.Max(1f, style.fontSize * 0.06f);
            GUI.contentColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, style);
            GUI.contentColor = color;
            GUI.Label(rect, text, style);
            GUI.contentColor = old;
        }
    }
}
