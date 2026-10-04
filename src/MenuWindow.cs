using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModMenu
{
    // The menu (Unity IMGUI): a sidebar with "All mods" (every loaded mod) and one entry per mod that draws a page,
    // pinned ones first; the selected page fills the rest of the window. A list scales to any number of mods.
    internal sealed class MenuWindow
    {
        private const int WindowId = 0x4D4D4E31;
        private const float SidebarWidth = 190f;
        private const float MinWidth = 560f;
        private const float MinHeight = 300f;

        // Read by InputBlockPatch, on the main thread.
        internal static bool MouseOver;

        // Clicks only queue their effect; it runs in the next Update. Changing what the window draws in the middle of
        // an IMGUI pass makes the layout pass and the event pass disagree, which Unity reports as errors.
        private readonly List<Action> deferred = new List<Action>();
        private readonly Dictionary<string, Vector2> scrolls = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        private readonly HashSet<string> pinned = new HashSet<string>(StringComparer.Ordinal);
        private List<MenuEntry> entries = new List<MenuEntry>();
        // Mods with a page, in sidebar order: pinned first, then by name.
        private List<MenuEntry> pages = new List<MenuEntry>();
        private Vector2 sidebarScroll;
        // Plugin id of the open page; empty = All mods.
        private string selected = string.Empty;
        private Rect rect;
        private bool visible;
        private bool positioned;
        private bool resizing;
        // Size while the corner is dragged; saved to the options when it is let go.
        private float liveWidth;
        private float liveHeight;
        private string keyName;
        private Key key = Key.F7;
        // Pushed on the game's input while the menu is open; null when closed or unavailable.
        private Amplitude.Framework.Input.InputManager.InputLayer escapeLayer;

        private bool stylesReady;
        private Texture2D windowBackground;
        private Texture2D rowBackground;
        private float backgroundOpacity = -1f;
        private GUIStyle windowStyle;
        private GUIStyle textStyle;
        private GUIStyle mutedStyle;
        private GUIStyle badStyle;
        private GUIStyle headingStyle;
        private GUIStyle boxStyle;
        private GUIStyle itemStyle;
        private GUIStyle selectedItemStyle;
        private GUIStyle pinStyle;

        private static bool AutoScale => Plugin.UiScale.Value < 0.25f;

        // Laid out for 1080p: automatic size grows with the screen height, in quarter steps.
        private static float Scale => AutoScale ? Mathf.Max(1f, Mathf.Round(Screen.height / 1080f * 4f) / 4f) : Mathf.Clamp(Plugin.UiScale.Value, 0.5f, 4f);

        public void Update()
        {
            if (deferred.Count > 0)
            {
                Action[] actions = deferred.ToArray();
                deferred.Clear();
                foreach (Action action in actions)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"Menu action failed: {e}");
                    }
                }
            }
            ReadKey();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && key != Key.None && keyboard[key].wasPressedThisFrame)
            {
                if (visible)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            }
            else if (visible && escapeLayer == null && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                // Without the input layer (see PushEscapeLayer) Esc still closes the menu, but the game sees it too.
                Close();
            }
            MouseOver = visible && IsMouseInside();
        }

        public void Close()
        {
            visible = false;
            RemoveEscapeLayer();
        }

        private void Open()
        {
            visible = true;
            Refresh();
            Select(Plugin.LastTab.Value);
            PushEscapeLayer();
        }

        // While the menu is open, Esc closes it and nothing else. The game's own windows do the same: a layer pushed on
        // top of the game's input gets its "close window" action (Esc) first, and taking it stops the pause menu that
        // the same key would open.
        private void PushEscapeLayer()
        {
            RemoveEscapeLayer();
            try
            {
                Amplitude.Framework.Input.InputManager manager = Amplitude.Framework.Input.InputManager.Instance;
                if (manager == null)
                {
                    return;
                }
                Amplitude.Framework.Input.ActionPath exitWindow = Amplitude.Mercury.InputActions.Shortcuts.ExitWindow;
                var layer = new Amplitude.Framework.Input.InputManager.InputLayer(exitWindow.ActionMapName, default(Amplitude.Framework.Input.InputManager.InputLayerPriority), Amplitude.Framework.Input.InputManager.InputLayer.FallbackType.PassThrough);
                layer.SubscribeButton(exitWindow, OnEscape);
                manager.PushLayer(layer);
                escapeLayer = layer;
            }
            catch (Exception e)
            {
                escapeLayer = null;
                Plugin.Log.LogWarning($"Could not take Esc from the game ({e.Message}); Esc closes the menu, but the game may react to it too.");
            }
        }

        private void RemoveEscapeLayer()
        {
            Amplitude.Framework.Input.InputManager.InputLayer layer = escapeLayer;
            escapeLayer = null;
            if (layer == null)
            {
                return;
            }
            try
            {
                Amplitude.Framework.Input.InputManager.Instance?.RemoveLayer(layer);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not give Esc back to the game: {e.Message}");
            }
        }

        // Called by the game's input manager on Esc: true = taken.
        private bool OnEscape()
        {
            if (!visible)
            {
                return false;
            }
            Close();
            return true;
        }

        public void OnGUI()
        {
            if (!visible)
            {
                return;
            }
            EnsureStyles();
            RefreshBackgrounds();
            Matrix4x4 previous = GUI.matrix;
            float scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;
            if (resizing)
            {
                rect.width = Mathf.Min(liveWidth, screenWidth);
                rect.height = Mathf.Min(liveHeight, screenHeight);
            }
            else
            {
                rect.width = Mathf.Min(Plugin.Width.Value, screenWidth);
                rect.height = Mathf.Min(Plugin.Height.Value, screenHeight);
            }
            if (!positioned)
            {
                // Centred the first time; after that it stays where it was dragged.
                rect.x = Mathf.Max(0f, (screenWidth - rect.width) / 2f);
                rect.y = Mathf.Max(0f, (screenHeight - rect.height) / 2f);
                positioned = true;
            }
            rect = GUI.Window(WindowId, rect, DrawWindow, $"Mods  ({KeyLabel} or Esc to close)", windowStyle);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, screenWidth - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, screenHeight - 40f));
            GUI.matrix = previous;
        }

        private string KeyLabel => key == Key.Backquote ? "`" : key.ToString();

        private void DrawWindow(int id)
        {
            // Asked for first, so its id stays the same whatever the page draws.
            int gripId = GUIUtility.GetControlID(FocusType.Passive);
            // Before the page, so a scroll bar under the corner doesn't take the click.
            HandleResizeGrip(gripId);
            MenuEntry page = pages.FirstOrDefault(t => t.Guid == selected);
            GUILayout.Space(2f);
            GUILayout.BeginHorizontal();
            DrawSidebar();
            GUILayout.Space(6f);
            GUILayout.BeginVertical();
            scrolls.TryGetValue(selected, out Vector2 scroll);
            scrolls[selected] = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            if (page == null)
            {
                DrawAllMods();
            }
            else
            {
                DrawPage(page);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);
            GUI.Box(GripRect, GUIContent.none);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        // ---- sidebar ----

        private void DrawSidebar()
        {
            GUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
            if (GUILayout.Button($"All mods ({entries.Count})", selected.Length == 0 ? selectedItemStyle : itemStyle))
            {
                Defer(() => Select(string.Empty));
            }
            GUILayout.Space(4f);
            sidebarScroll = GUILayout.BeginScrollView(sidebarScroll, GUILayout.ExpandHeight(true));
            bool previousPinned = false;
            foreach (MenuEntry entry in pages)
            {
                bool isPinned = pinned.Contains(entry.Guid);
                if (previousPinned && !isPinned)
                {
                    GUILayout.Space(8f);
                }
                previousPinned = isPinned;
                GUILayout.BeginHorizontal();
                string guid = entry.Guid;
                if (GUILayout.Button(new GUIContent(isPinned ? "★" : "☆", isPinned ? "Unpin" : "Pin to the top"), pinStyle, GUILayout.Width(22f)))
                {
                    Defer(() => TogglePin(guid));
                }
                if (GUILayout.Button(entry.Title, guid == selected ? selectedItemStyle : itemStyle))
                {
                    Defer(() => Select(guid));
                }
                GUILayout.EndHorizontal();
            }
            if (pages.Count == 0)
            {
                GUILayout.Label("No mod has a page here yet.", mutedStyle);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        // ---- pages ----

        private void DrawAllMods()
        {
            GUILayout.Label("Every mod BepInEx loaded. Mods with a page are listed on the left; ★ pins one to the top.", mutedStyle);
            GUILayout.Space(4f);
            foreach (MenuEntry entry in entries)
            {
                GUILayout.BeginHorizontal(boxStyle);
                GUILayout.Label($"{entry.Name}  {entry.Version}", headingStyle, GUILayout.Width(240f));
                if (!entry.Loaded)
                {
                    GUILayout.Label("failed to load (see BepInEx/LogOutput.log)", badStyle, GUILayout.ExpandWidth(true));
                }
                else
                {
                    string status = entry.Status;
                    GUILayout.Label(!string.IsNullOrEmpty(status) ? status : entry.HasPage ? string.Empty : "no page", string.IsNullOrEmpty(status) ? mutedStyle : textStyle, GUILayout.ExpandWidth(true));
                }
                if (entry.HasPage && GUILayout.Button("Open", GUILayout.Width(60f)))
                {
                    string guid = entry.Guid;
                    Defer(() => Select(guid));
                }
                GUILayout.EndHorizontal();
            }
            if (entries.Count == 0)
            {
                GUILayout.Label("No other mod is loaded.", mutedStyle);
            }

            GUILayout.Space(10f);
            GUILayout.Label("Menu", headingStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size", textStyle, GUILayout.Width(300f));
            float size = Scale;
            if (GUILayout.Button("-", GUILayout.Width(26f)))
            {
                Defer(() => Plugin.UiScale.Value = Mathf.Max(0.5f, size - 0.25f));
            }
            GUILayout.Label(AutoScale ? $"auto ({size:0.##}x)" : $"{size:0.##}x", textStyle, GUILayout.Width(100f));
            if (GUILayout.Button("+", GUILayout.Width(26f)))
            {
                Defer(() => Plugin.UiScale.Value = Mathf.Min(4f, size + 0.25f));
            }
            GUI.enabled = !AutoScale;
            if (GUILayout.Button("Auto", GUILayout.Width(52f)))
            {
                Defer(() => Plugin.UiScale.Value = 0f);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Opacity", textStyle, GUILayout.Width(300f));
            if (GUILayout.Button("-", GUILayout.Width(26f)))
            {
                Defer(() => Plugin.Opacity.Value = Mathf.Max(0.3f, Mathf.Round((Plugin.Opacity.Value - 0.05f) * 20f) / 20f));
            }
            GUILayout.Label($"{Plugin.Opacity.Value * 100f:0}%", textStyle, GUILayout.Width(100f));
            if (GUILayout.Button("+", GUILayout.Width(26f)))
            {
                Defer(() => Plugin.Opacity.Value = Mathf.Min(1f, Mathf.Round((Plugin.Opacity.Value + 0.05f) * 20f) / 20f));
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Drag the bottom-right corner to resize. Key: {KeyLabel} (change ToggleKey in BepInEx/config/{Plugin.Guid}.cfg). Mod Menu {Plugin.Version}", mutedStyle);
        }

        // A mod's own page. One that throws is shown as an error from then on, instead of breaking the menu.
        private void DrawPage(MenuEntry entry)
        {
            if (entry.Error != null)
            {
                GUILayout.Label($"{entry.Name}'s page failed and is off until the menu is reopened: {entry.Error}", badStyle);
                return;
            }
            GUILayout.BeginVertical();
            try
            {
                entry.Draw();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                entry.Error = e.GetBaseException().Message;
                Plugin.Log.LogError($"{entry.Name}: its menu page failed: {e}");
            }
            GUILayout.EndVertical();
            // Whatever the page changed, the menu draws on with the defaults.
            GUI.enabled = true;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;
        }

        private Rect GripRect => new Rect(rect.width - 16f, rect.height - 16f, 14f, 14f);

        private void HandleResizeGrip(int id)
        {
            Rect grip = GripRect;
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && grip.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        liveWidth = rect.width;
                        liveHeight = rect.height;
                        resizing = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        liveWidth = Mathf.Max(MinWidth, liveWidth + e.delta.x);
                        liveHeight = Mathf.Max(MinHeight, liveHeight + e.delta.y);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        Defer(() => resizing = false);
                        float width = liveWidth;
                        float height = liveHeight;
                        Defer(() =>
                        {
                            Plugin.Width.Value = width;
                            Plugin.Height.Value = height;
                        });
                        e.Use();
                    }
                    break;
            }
        }

        // ---- helpers ----

        private void Refresh()
        {
            pinned.Clear();
            foreach (string guid in (Plugin.Pinned.Value ?? string.Empty).Split(','))
            {
                if (guid.Trim().Length > 0)
                {
                    pinned.Add(guid.Trim());
                }
            }
            entries = MenuEntry.Discover();
            OrderSidebar();
        }

        // Pinned first, each group by name.
        private void OrderSidebar()
        {
            pages = entries.Where(e => e.HasPage && e.Loaded)
                .OrderBy(e => pinned.Contains(e.Guid) ? 0 : 1)
                .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void TogglePin(string guid)
        {
            if (!pinned.Remove(guid))
            {
                pinned.Add(guid);
            }
            Plugin.Pinned.Value = string.Join(",", pinned.OrderBy(g => g, StringComparer.Ordinal).ToArray());
            OrderSidebar();
        }

        private void Select(string guid)
        {
            selected = pages.Any(t => t.Guid == guid) ? guid : string.Empty;
            if (Plugin.LastTab.Value != selected)
            {
                Plugin.LastTab.Value = selected;
            }
        }

        private void Defer(Action action) => deferred.Add(action);

        private bool IsMouseInside()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return false;
            }
            Vector2 position = mouse.position.ReadValue();
            float scale = Scale;
            var gui = new Vector2(position.x / scale, (Screen.height - position.y) / scale);
            return rect.Contains(gui);
        }

        private void ReadKey()
        {
            string name = Plugin.ToggleKey.Value;
            if (name == keyName)
            {
                return;
            }
            keyName = name;
            if (!Enum.TryParse(name?.Trim(), true, out Key parsed) || parsed == Key.None)
            {
                Plugin.Log.LogWarning($"Unknown ToggleKey '{name}', using F7.");
                parsed = Key.F7;
            }
            key = parsed;
        }

        private void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }
            stylesReady = true;
            textStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            mutedStyle = new GUIStyle(textStyle);
            mutedStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            badStyle = new GUIStyle(textStyle);
            badStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
            headingStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset(0, 0, 2, 2), border = new RectOffset(0, 0, 0, 0) };
            itemStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            selectedItemStyle = new GUIStyle(itemStyle) { fontStyle = FontStyle.Bold };
            selectedItemStyle.normal.textColor = selectedItemStyle.hover.textColor = selectedItemStyle.active.textColor = new Color(0.95f, 0.85f, 0.6f);
            pinStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 2, 2) };
            windowStyle = new GUIStyle(GUI.skin.window) { border = new RectOffset(0, 0, 0, 0) };
            SetTextColor(windowStyle, new Color(0.95f, 0.85f, 0.6f));
        }

        private void RefreshBackgrounds()
        {
            float opacity = Mathf.Clamp(Plugin.Opacity.Value, 0.3f, 1f);
            if (Mathf.Abs(opacity - backgroundOpacity) < 0.001f && windowBackground != null)
            {
                return;
            }
            backgroundOpacity = opacity;
            Replace(ref windowBackground, new Color(0.08f, 0.09f, 0.11f, opacity));
            Replace(ref rowBackground, new Color(0.15f, 0.16f, 0.19f, opacity));
            SetBackground(windowStyle, windowBackground);
            SetBackground(boxStyle, rowBackground);
        }

        private static void Replace(ref Texture2D texture, Color color)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
            texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
        }

        private static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.onNormal.textColor = color;
            style.hover.textColor = color;
            style.onHover.textColor = color;
            style.active.textColor = color;
            style.onActive.textColor = color;
            style.focused.textColor = color;
            style.onFocused.textColor = color;
        }

        private static void SetBackground(GUIStyle style, Texture2D texture)
        {
            style.normal.background = texture;
            style.onNormal.background = texture;
            style.focused.background = texture;
            style.onFocused.background = texture;
            style.hover.background = texture;
            style.onHover.background = texture;
            style.active.background = texture;
            style.onActive.background = texture;
        }
    }
}
