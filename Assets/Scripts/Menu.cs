using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChompChompPanic
{
    /// <summary>
    /// A vertical IMGUI menu of buttons, on/off toggles and 0-1 sliders, shared by the title screen and the in-game pause menu.
    /// Navigate with arrows / WASD / d-pad / left stick or the mouse; confirm with Enter, Space, click or the south button;
    /// adjust sliders with left/right or by clicking and dragging the bar; flip toggles with confirm or left/right; Esc / east button goes back.
    /// Call <see cref="Update"/> from the owner's Update and <see cref="Draw"/> from its OnGUI. Works while the game is paused.
    /// </summary>
    public class Menu
    {
        /// <summary>One row: a button (<see cref="Run"/>), an on/off toggle (<see cref="IsOn"/> / <see cref="SetOn"/>)
        /// or a 0-1 slider (<see cref="Get"/> / <see cref="Set"/>).</summary>
        public class Item
        {
            public string Label;
            public Action Run;
            public Func<float> Get;
            public Action<float> Set;
            public Func<bool> IsOn;
            public Action<bool> SetOn;
            public bool IsSlider => Get != null;
            public bool IsToggle => IsOn != null;
        }

        /// <summary>Controls hint for the owner to show under the menu.</summary>
        public const string Hint = "Arrows / WASD / D-pad to choose, Left / Right to adjust, Enter / Space / A to confirm, Esc / B to go back";

        const float SliderStep = 0.1f;

        public AudioClip MoveSound;
        public AudioClip SelectSound;
        public float SoundVolume = 0.6f;
        public Color ItemColor = new(0.85f, 0.85f, 0.9f);
        public Color SelectedColor = new(1f, 0.72f, 0.15f);

        /// <summary>Heading drawn above the entries; null for none.</summary>
        public string Title { get; private set; }

        /// <summary>The highlighted entry. Set it after <see cref="Show"/> to start somewhere other than the top.</summary>
        public int Selected
        {
            get => selected;
            set => selected = Mathf.Clamp(value, 0, Mathf.Max(0, items.Count - 1));
        }

        readonly List<Item> items = new();
        Action back;
        int selected;
        float stickRepeatX;
        float stickRepeatY;
        Vector2 lastMouse;
        int dragging = -1;
        GUIStyle itemStyle;
        GUIStyle sliderStyle;
        GUIStyle titleStyle;

        public static Item Button(string label, Action run) => new() { Label = label, Run = run };

        public static Item Slider(string label, Func<float> get, Action<float> set) => new() { Label = label, Get = get, Set = set };

        public static Item Toggle(string label, Func<bool> isOn, Action<bool> setOn) =>
            new() { Label = label, IsOn = isOn, SetOn = setOn, Run = () => setOn(!isOn()) };

        /// <summary>Replace the entries. <paramref name="onBack"/> runs on Esc / east button; null means they do nothing.</summary>
        public void Show(string title, Action onBack, params Item[] entries)
        {
            Title = title;
            back = onBack;
            items.Clear();
            items.AddRange(entries);
            selected = 0;
            dragging = -1;
        }

        /// <summary>The options page: music and sound-effect volume and screen shake, saved when it closes. Back (or Esc) runs <paramref name="onBack"/>.</summary>
        public void ShowOptions(Action onBack)
        {
            void Close()
            {
                GameSettings.Save();
                onBack();
            }

            Show("Options", Close,
                Slider("Music", () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v),
                Slider("Sound Effects", () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v),
                Toggle("Screen Shake", () => GameSettings.ScreenShake, v => GameSettings.ScreenShake = v),
                Button("Back", Close));
        }

        /// <summary>Quit the game (or leave play mode in the editor).</summary>
        public static void QuitGame()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Whether this platform can quit (browsers can't).</summary>
        public static bool CanQuit =>
#if UNITY_WEBGL
            false;
#else
            true;
#endif

        public void Update()
        {
            if (items.Count == 0)
                return;

            int move = 0;
            int adjust = 0;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) move--;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) move++;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) adjust--;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) adjust++;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) move--;
                if (pad.dpad.down.wasPressedThisFrame) move++;
                if (pad.dpad.left.wasPressedThisFrame) adjust--;
                if (pad.dpad.right.wasPressedThisFrame) adjust++;
                move += StickSteps(-pad.leftStick.y.ReadValue(), ref stickRepeatY);
                adjust += StickSteps(pad.leftStick.x.ReadValue(), ref stickRepeatX);
            }

            if (move != 0)
                Select((selected + move + items.Count) % items.Count);
            var item = items[selected];
            if (adjust != 0 && item.IsSlider)
                Adjust(item, item.Get() + adjust * SliderStep);
            else if (adjust != 0 && item.IsToggle)
                Confirm(selected);

            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (confirm && !item.IsSlider)
            {
                Confirm(selected);
                return;
            }

            bool cancel = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (cancel && back != null)
            {
                SoundPlayer.Play(SelectSound, SoundVolume, 1f, 0f);
                back();
            }
        }

        /// <summary>Left stick: one step when pushed, then repeats while held.</summary>
        static int StickSteps(float value, ref float repeatAt)
        {
            if (Mathf.Abs(value) < 0.5f)
            {
                repeatAt = 0f;
                return 0;
            }
            if (Time.unscaledTime < repeatAt)
                return 0;
            repeatAt = Time.unscaledTime + (repeatAt == 0f ? 0.4f : 0.15f);
            return value > 0f ? 1 : -1;
        }

        void Select(int index)
        {
            if (index == selected)
                return;
            selected = index;
            SoundPlayer.Play(MoveSound, SoundVolume);
        }

        void Confirm(int index)
        {
            selected = index;
            SoundPlayer.Play(SelectSound, SoundVolume, 1f, 0f);
            items[index].Run?.Invoke();
        }

        /// <summary>Set a slider, snapped to 5% steps; ticks so the sound-effect slider previews its own volume.</summary>
        void Adjust(Item item, float value)
        {
            value = Mathf.Clamp01(Mathf.Round(value * 20f) / 20f);
            if (Mathf.Approximately(value, item.Get()))
                return;
            item.Set(value);
            SoundPlayer.Play(MoveSound, SoundVolume);
        }

        /// <summary>Draw the title (if <paramref name="showTitle"/>) and entries, centered, starting <paramref name="top"/> pixels down.</summary>
        public void Draw(float top, bool showTitle = true)
        {
            if (itemStyle == null)
            {
                itemStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                sliderStyle = new GUIStyle(itemStyle) { alignment = TextAnchor.MiddleRight };
                titleStyle = new GUIStyle(itemStyle);
                itemStyle.normal.textColor = sliderStyle.normal.textColor = titleStyle.normal.textColor = Color.white;
            }
            float h = Screen.height;
            float rowH = h * 0.075f;
            float rowW = Mathf.Min(Screen.width * 0.9f, h * 0.9f);
            float x = (Screen.width - rowW) * 0.5f;

            if (showTitle && !string.IsNullOrEmpty(Title))
            {
                titleStyle.fontSize = Mathf.RoundToInt(h * 0.07f);
                DrawShadowed(new Rect(0, top, Screen.width, rowH * 1.4f), Title, titleStyle, Color.white);
                top += rowH * 1.6f;
            }

            // Runtime IMGUI doesn't reliably get MouseMove or MouseDrag events, so hover follows any change in mouse
            // position and slider drags poll the button on repaint.
            var e = Event.current;
            var mouse = e.mousePosition;
            bool repaint = e.type == EventType.Repaint;
            bool mouseMoved = repaint && mouse != lastMouse;
            if (repaint)
                lastMouse = mouse;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var row = new Rect(x, top + i * rowH, rowW, rowH);
                var bar = new Rect(row.x + row.width * 0.47f, row.y + row.height * 0.3f, row.width * 0.38f, row.height * 0.4f);

                if (mouseMoved && dragging < 0 && row.Contains(mouse))
                    Select(i);
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(mouse))
                {
                    e.Use();
                    if (!item.IsSlider)
                    {
                        Confirm(i);
                        return;
                    }
                    Select(i);
                    var grab = new Rect(bar.x - rowH * 0.3f, row.y, bar.width + rowH * 0.6f, row.height);
                    if (grab.Contains(mouse))
                    {
                        dragging = i;
                        Adjust(item, (mouse.x - bar.x) / bar.width);
                    }
                }
                if (dragging == i && repaint)
                {
                    if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                        Adjust(item, (mouse.x - bar.x) / bar.width);
                    else
                        dragging = -1;
                }

                bool isSelected = i == selected;
                var color = isSelected ? SelectedColor : ItemColor;
                if (item.IsSlider)
                    DrawSlider(row, bar, item, color);
                else
                {
                    float pulse = isSelected ? 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.04f : 1f;
                    itemStyle.fontSize = Mathf.RoundToInt(h * 0.05f * pulse);
                    string label = item.IsToggle ? $"{item.Label}: {(item.IsOn() ? "On" : "Off")}" : item.Label;
                    DrawShadowed(row, isSelected ? $"> {label} <" : label, itemStyle, color);
                }
            }
        }

        void DrawSlider(Rect row, Rect bar, Item item, Color color)
        {
            sliderStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.045f);
            sliderStyle.alignment = TextAnchor.MiddleRight;
            DrawShadowed(new Rect(row.x, row.y, row.width * 0.43f, row.height), item.Label, sliderStyle, color);

            float value = item.Get();
            var old = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.18f, 0.9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = color;
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * value, bar.height - 4f), Texture2D.whiteTexture);
            GUI.color = old;

            sliderStyle.alignment = TextAnchor.MiddleLeft;
            DrawShadowed(new Rect(bar.xMax + row.height * 0.2f, row.y, row.width * 0.15f, row.height),
                $"{Mathf.RoundToInt(value * 100f)}%", sliderStyle, color);
        }

        /// <summary>A label with a drop shadow, so it reads over any background.</summary>
        public static void DrawShadowed(Rect rect, string text, GUIStyle style, Color color)
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
