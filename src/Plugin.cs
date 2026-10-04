using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ModMenu
{
    // One key, one window: every installed mod that offers a page is listed in it (see MenuEntry and the README).
    [BepInPlugin(Guid, DisplayName, Version)]
    public class Plugin : BaseUnityPlugin
    {
        // Mods check for this id to leave the key to the menu: Chainloader.PluginInfos.ContainsKey(...).
        public const string Guid = "el2.modmenu";
        public const string DisplayName = "Mod Menu";
        public const string Version = BuildInfo.Version;

        internal static ManualLogSource Log;
        internal static ConfigEntry<string> ToggleKey;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> Width;
        internal static ConfigEntry<float> Height;
        internal static ConfigEntry<string> LastTab;
        internal static ConfigEntry<string> Pinned;

        private Harmony harmony;
        private bool patched;
        private MenuWindow window;

        private void Awake()
        {
            Log = Logger;
            ToggleKey = Config.Bind("Window", "ToggleKey", "F7",
                "Key that opens and closes the menu (a Unity Input System key name: F7, Backquote, Insert...). The game uses F1-F6 and F8-F11.");
            UiScale = Config.Bind("Window", "Size", 0f,
                new ConfigDescription("Size of the menu and its text. 0 = automatic (follows the screen resolution: 1x at 1080p, 2x at 4K); otherwise a multiplier.", new AcceptableValueRange<float>(0f, 4f)));
            Opacity = Config.Bind("Window", "Opacity", 1f,
                new ConfigDescription("Opacity of the menu background (1 = solid).", new AcceptableValueRange<float>(0.3f, 1f)));
            Width = Config.Bind("Window", "Width", 900f,
                new ConfigDescription("Width of the menu at 1x size (drag its bottom-right corner to change it).", new AcceptableValueRange<float>(560f, 3000f)));
            Height = Config.Bind("Window", "Height", 680f,
                new ConfigDescription("Height of the menu at 1x size.", new AcceptableValueRange<float>(300f, 2000f)));
            LastTab = Config.Bind("Window", "LastTab", string.Empty,
                "The mod whose page the menu opens on (its plugin id; empty = All mods).");
            Pinned = Config.Bind("Window", "Pinned", string.Empty,
                "Plugin ids of the mods pinned to the top of the list, comma-separated (set with the ★ next to each mod).");

            harmony = new Harmony(Guid);
            window = new MenuWindow();
            Log.LogInfo($"{DisplayName} {Version} waiting for the game data");
        }

        private void Update()
        {
            if (!patched)
            {
                if (!GameDataReady())
                {
                    return;
                }
                PatchAll();
            }
            try
            {
                window.Update();
            }
            catch (Exception e)
            {
                Log.LogError($"Update failed: {e}");
            }
        }

        private void OnGUI()
        {
            if (!patched)
            {
                return;
            }
            try
            {
                window.OnGUI();
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.LogError($"Drawing the menu failed: {e}");
            }
        }

        private void OnDestroy()
        {
            window?.Close();
            harmony?.UnpatchSelf();
        }

        // Patching runs a class's static constructor; wait until the game data those may read is loaded.
        private static bool GameDataReady()
        {
            try
            {
                return Amplitude.Mercury.Utils.DataUtils != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void PatchAll()
        {
            patched = true;
            int failed = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    failed++;
                    Log.LogError($"Could not patch {type.Name} (game update?); clicks on the menu may also reach the map: {e.GetBaseException().Message}");
                }
            }
            Log.LogInfo($"{DisplayName} {Version} active (game {Application.version}, {failed} patches failed). Press {ToggleKey.Value} in game.");
        }
    }
}
