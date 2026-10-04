using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

namespace ModMenu
{
    // One installed BepInEx plugin. A plugin gets a page by declaring, on its plugin class, public members found here by
    // name, so it needs no reference to this mod (see README):
    //   void ModMenuDraw()           the page's content, drawn with GUILayout (required for a page)
    //   string ModMenuTitle { get; } the name in the list (default: the plugin's name)
    //   string ModMenuStatus { get; } one line shown under All mods (optional)
    internal sealed class MenuEntry
    {
        public string Guid = string.Empty;
        public string Name = string.Empty;
        public string Version = string.Empty;
        public bool Loaded;
        public Action Draw;
        public Func<string> TitleSource;
        public Func<string> StatusSource;
        // Set when the page's drawing threw: the page then shows this instead, until the menu is reopened.
        public string Error;

        public bool HasPage => Draw != null;

        public string Title => Read(TitleSource) is string title && title.Length > 0 ? title : Name;

        public string Status => Read(StatusSource);

        // Every plugin BepInEx loaded, this one excluded, those with a page first, by name.
        public static List<MenuEntry> Discover()
        {
            var entries = new List<MenuEntry>();
            foreach (KeyValuePair<string, PluginInfo> pair in Chainloader.PluginInfos)
            {
                PluginInfo info = pair.Value;
                if (info?.Metadata == null || info.Metadata.GUID == Plugin.Guid)
                {
                    continue;
                }
                var entry = new MenuEntry
                {
                    Guid = info.Metadata.GUID,
                    Name = info.Metadata.Name ?? info.Metadata.GUID,
                    Version = info.Metadata.Version?.ToString() ?? string.Empty,
                    Loaded = info.Instance != null,
                };
                if (info.Instance != null)
                {
                    Bind(entry, info.Instance);
                }
                entries.Add(entry);
            }
            entries.Sort((a, b) => a.HasPage != b.HasPage ? (a.HasPage ? -1 : 1) : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
            return entries;
        }

        private static void Bind(MenuEntry entry, BaseUnityPlugin instance)
        {
            Type type = instance.GetType();
            try
            {
                MethodInfo draw = type.GetMethod("ModMenuDraw", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (draw != null && draw.ReturnType == typeof(void))
                {
                    entry.Draw = (Action)Delegate.CreateDelegate(typeof(Action), instance, draw);
                }
                entry.TitleSource = Getter(type, instance, "ModMenuTitle");
                entry.StatusSource = Getter(type, instance, "ModMenuStatus");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"{entry.Name}: could not read its menu members: {e.Message}");
                entry.Draw = null;
            }
        }

        private static Func<string> Getter(Type type, object instance, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            MethodInfo getter = property?.GetGetMethod();
            if (getter == null || property.PropertyType != typeof(string) || getter.GetParameters().Length != 0)
            {
                return null;
            }
            return (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), instance, getter);
        }

        private string Read(Func<string> source)
        {
            if (source == null)
            {
                return null;
            }
            try
            {
                return source();
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug($"{Name}: menu text failed: {e.Message}");
                return null;
            }
        }
    }
}
