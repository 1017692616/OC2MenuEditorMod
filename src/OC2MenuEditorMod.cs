using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OC2MenuEditorMod
{
    internal static class PluginInfo
    {
        internal const string Guid = "local.centos.overcooked2.menu-editor";
        internal const string Name = "OC2 Menu Editor";
        internal const string Version = "0.1.2";
        internal const string Author = "连营&大幽灵&小松鼠 centoscj@gmail.com";
    }

    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKey { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigKey = Config.Bind(
                "菜单编辑",
                "配置快捷键",
                new KeyboardShortcut(KeyCode.F6),
                "打开菜单编辑窗口，默认 F6。修改后重启游戏生效。\nOpen the menu editor. Default: F6.");

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;
            MenuFilterFeature.EnsureLoaded();
            MenuGenerationFeature.EnsureBootstrap();
            Log.LogInfo(PluginInfo.Name + " " + PluginInfo.Version + " loaded. Author: " + PluginInfo.Author);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            MenuFilterFeature.OnSceneLoaded();
            MenuGenerationFeature.OnSceneLoaded();
        }

        private void Update()
        {
            MenuFilterFeature.Update();
        }

        private void OnGUI()
        {
            MenuFilterFeature.DrawStatus();
            MenuFilterFeature.Draw();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
        }
    }
}
