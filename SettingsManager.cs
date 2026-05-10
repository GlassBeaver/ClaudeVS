using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using System;
using System.Diagnostics;

namespace ClaudeVS
{
    internal static class SettingsManager
    {
        private const string CollectionPath = "ClaudeVS";
        private const string LastCommandKey = "LastCommand";
        private const string DefaultCommand = "claude";
        private const string FontSizeKey = "FontSize";
        private const short DefaultFontSize = 10;
        private const string ThemeKey = "Theme";
        private const string DefaultTheme = "System";
        private const string QuickSwitchClaudePresetsKey = "QuickSwitchPresets";
        private const string QuickSwitchCodexPresetsKey = "QuickSwitchPresetsCodex";

        public static string GetLastCommand()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    return DefaultCommand;
                }

                if (!userSettingsStore.PropertyExists(CollectionPath, LastCommandKey))
                {
                    return DefaultCommand;
                }

                string command = userSettingsStore.GetString(CollectionPath, LastCommandKey);
                return string.IsNullOrWhiteSpace(command) ? DefaultCommand : command;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetLastCommand: {ex}");
                return DefaultCommand;
            }
        }

        public static void SaveLastCommand(string command)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    userSettingsStore.CreateCollection(CollectionPath);
                }

                userSettingsStore.SetString(CollectionPath, LastCommandKey, command);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SaveLastCommand: {ex}");
            }
        }

        public static short GetFontSize()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    return DefaultFontSize;
                }

                if (!userSettingsStore.PropertyExists(CollectionPath, FontSizeKey))
                {
                    return DefaultFontSize;
                }

                int fontSize = userSettingsStore.GetInt32(CollectionPath, FontSizeKey);
                return (short)fontSize;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetFontSize: {ex}");
                return DefaultFontSize;
            }
        }

        public static void SaveFontSize(short fontSize)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    userSettingsStore.CreateCollection(CollectionPath);
                }

                userSettingsStore.SetInt32(CollectionPath, FontSizeKey, fontSize);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SaveFontSize: {ex}");
            }
        }

        public static string GetTheme()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    return DefaultTheme;
                }

                if (!userSettingsStore.PropertyExists(CollectionPath, ThemeKey))
                {
                    return DefaultTheme;
                }

                string theme = userSettingsStore.GetString(CollectionPath, ThemeKey);
                return string.IsNullOrWhiteSpace(theme) ? DefaultTheme : theme;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetTheme: {ex}");
                return DefaultTheme;
            }
        }

        public static void SaveTheme(string theme)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    userSettingsStore.CreateCollection(CollectionPath);
                }

                userSettingsStore.SetString(CollectionPath, ThemeKey, theme);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SaveTheme: {ex}");
            }
        }

        public static void LoadQuickSwitchPresets(string agent, int[] models, bool[] thinking, int[] effort, int maxModelCount, int maxEffortCount)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    return;
                }

                string key = GetQuickSwitchPresetsKey(agent);

                if (!userSettingsStore.PropertyExists(CollectionPath, key))
                {
                    return;
                }

                string presets = userSettingsStore.GetString(CollectionPath, key);
                string[] parts = presets.Split('|');
                for (int i = 0; i < Math.Min(parts.Length, 4); i++)
                {
                    string[] values = parts[i].Split(',');
                    if (values.Length == 3)
                    {
                        if (int.TryParse(values[0], out int m) && m >= 0 && m < maxModelCount)
                            models[i] = m;
                        if (bool.TryParse(values[1], out bool t))
                            thinking[i] = t;
                        if (int.TryParse(values[2], out int e) && e >= 0 && e < maxEffortCount)
                            effort[i] = e;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in LoadQuickSwitchPresets: {ex}");
            }
        }

        public static void SaveQuickSwitchPresets(string agent, int[] models, bool[] thinking, int[] effort)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!userSettingsStore.CollectionExists(CollectionPath))
                {
                    userSettingsStore.CreateCollection(CollectionPath);
                }

                string key = GetQuickSwitchPresetsKey(agent);
                string presets = $"{models[0]},{thinking[0]},{effort[0]}|{models[1]},{thinking[1]},{effort[1]}|{models[2]},{thinking[2]},{effort[2]}|{models[3]},{thinking[3]},{effort[3]}";
                userSettingsStore.SetString(CollectionPath, key, presets);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SaveQuickSwitchPresets: {ex}");
            }
        }

        private static string GetQuickSwitchPresetsKey(string agent)
        {
            return string.Equals(agent, "Codex", StringComparison.OrdinalIgnoreCase) ? QuickSwitchCodexPresetsKey : QuickSwitchClaudePresetsKey;
        }
    }
}
