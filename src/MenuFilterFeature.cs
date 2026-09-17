using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OC2MenuEditorMod
{
    [Serializable]
    internal sealed class LevelSettings
    {
        internal string Scene;
        internal int ActivePreset;
        internal readonly List<List<string>> Presets = new List<List<string>>();
        internal int InitialCount;
        internal readonly List<string> InitialExcluded = new List<string>();
    }

    internal static class MenuFilterFeature
    {
        private const string FileName = "local.centos.overcooked2.menu-editor.cfg";
        private const string Header = "OC2MenuEditor|1";
        private const int PresetCount = 5;
        private static readonly List<LevelSettings> s_levels = new List<LevelSettings>();
        private static bool s_loaded;
        private static bool s_open;
        private static int s_mode = 0;
        private static string s_scene;
        private static string s_countText = "0";
        private static string s_countScene;
        private static Vector2 s_scroll;
        private static Rect s_window = new Rect(80f, 70f, 610f, 680f);
        private static GUIStyle s_statusStyle;

        internal static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            try
            {
                string path = Path.Combine(Paths.ConfigPath, FileName);
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || lines[0] != Header) return;
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('|');
                    if (p.Length != 4 || p[0] != "L") continue;
                    LevelSettings level = new LevelSettings();
                    level.Scene = Decode(p[1]);
                    int.TryParse(p[2], out level.ActivePreset);
                    DecodePresets(level, p[3]);
                    if (!string.IsNullOrEmpty(level.Scene)) s_levels.Add(level);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Menu editor config load failed: " + ex.Message); }
        }

        internal static void OnSceneLoaded()
        {
            s_scene = SceneManager.GetActiveScene().name;
            if (!IsInLevel()) s_open = false;
        }

        internal static void Update()
        {
            if (!IsInLevel()) { s_open = false; return; }
            if (RuntimeHotkeyPressed())
            {
                s_open = !s_open;
                Plugin.Log.LogInfo("Menu editor " + (s_open ? "opened" : "closed") + ".");
            }
        }

        internal static void Draw()
        {
            if (s_open && IsInLevel()) s_window = GUI.Window(916205, s_window, DrawWindow, "关卡菜单设置 【" + PluginInfo.Author + "】");
        }

        internal static void DrawStatus()
        {
            string status = GetInitialExclusionStatus();
            if (string.IsNullOrEmpty(status)) return;
            if (s_statusStyle == null)
            {
                s_statusStyle = new GUIStyle(GUI.skin.label);
                s_statusStyle.fontSize = 18;
                s_statusStyle.fontStyle = FontStyle.Bold;
                s_statusStyle.normal.textColor = Color.white;
            }
            GUI.Label(new Rect(18f, 18f, 720f, 28f), status, s_statusStyle);
        }

        internal static bool HasConfiguredHostMenu()
        {
            List<string> ids = GetActivePreset(false);
            return ids != null && ids.Count > 0;
        }

        internal static bool ShouldUseInHostMenu(RecipeList.Entry entry)
        {
            if (entry == null || entry.m_order == null) return true;
            List<string> ids = GetActivePreset(false);
            return ids == null || ids.Count == 0 || ids.Contains(entry.m_order.m_uID.ToString());
        }

        internal static bool ShouldExcludeFromInitialMenu(RecipeList.Entry entry)
        {
            if (entry == null || entry.m_order == null) return false;
            LevelSettings level = GetLevel(false);
            return level != null && level.InitialCount > 0 && level.InitialExcluded.Contains(entry.m_order.m_uID.ToString());
        }

        internal static int GetInitialExclusionCount()
        {
            LevelSettings level = GetLevel(false);
            return level == null ? 0 : Mathf.Clamp(level.InitialCount, 0, 9999);
        }

        internal static bool HasActiveInitialExclusion()
        {
            LevelSettings level = GetLevel(false);
            return level != null && level.InitialCount > 0 && level.InitialExcluded.Count > 0;
        }

        internal static string GetInitialExclusionStatus()
        {
            LevelSettings level = GetLevel(false);
            if (level == null || level.InitialCount <= 0 || level.InitialExcluded.Count == 0) return string.Empty;
            return "已编辑前" + level.InitialCount + "道菜不显示" + new HashSet<string>(level.InitialExcluded).Count + "个特定菜单";
        }

        internal static List<RecipeList.Entry> GetKnownRecipeEntries()
        {
            List<RecipeList.Entry> result = new List<RecipeList.Entry>();
            HashSet<int> ids = new HashSet<int>();
            ClientKitchenFlowControllerBase controller = UnityEngine.Object.FindObjectOfType<ClientKitchenFlowControllerBase>();
            KitchenLevelConfigBase level = controller == null ? null : controller.GetLevelConfig() as KitchenLevelConfigBase;
            RoundDataBase data = level == null ? null : level.GetRoundData();
            AddRoundData(data, result, ids);
            if (result.Count == 0 && level != null)
            {
                List<OrderDefinitionNode> orders = level.GetAllRecipes();
                if (orders != null) for (int i = 0; i < orders.Count; i++) AddEntry(result, ids, orders[i]);
            }
            return result;
        }

        private static void AddRoundData(RoundDataBase data, List<RecipeList.Entry> result, HashSet<int> ids)
        {
            RoundData round = data as RoundData;
            if (round != null) AddRecipeList(round.m_recipes, result, ids);
            ScriptedRoundData scripted = data as ScriptedRoundData;
            if (scripted != null && scripted.m_manualOrder != null)
                for (int i = 0; i < scripted.m_manualOrder.Length; i++) AddEntry(result, ids, scripted.m_manualOrder[i]);
            DynamicRoundData dynamic = data as DynamicRoundData;
            if (dynamic != null && dynamic.Phases != null)
                for (int i = 0; i < dynamic.Phases.Length; i++) if (dynamic.Phases[i] != null) AddRecipeList(dynamic.Phases[i].Recipes, result, ids);
        }

        private static void AddRecipeList(RecipeList recipes, List<RecipeList.Entry> result, HashSet<int> ids)
        {
            if (recipes == null) return;
            if (recipes.m_recipes != null) for (int i = 0; i < recipes.m_recipes.Length; i++) AddEntry(result, ids, recipes.m_recipes[i]);
            if (recipes.m_freestyle != null) for (int i = 0; i < recipes.m_freestyle.Length; i++) AddEntry(result, ids, recipes.m_freestyle[i]);
        }

        private static void AddEntry(List<RecipeList.Entry> result, HashSet<int> ids, OrderDefinitionNode order)
        {
            if (order == null || !ids.Add(order.m_uID)) return;
            RecipeList.Entry entry = new RecipeList.Entry();
            entry.m_order = order;
            result.Add(entry);
        }

        private static void AddEntry(List<RecipeList.Entry> result, HashSet<int> ids, RecipeList.Entry entry)
        {
            if (entry == null || entry.m_order == null || !ids.Add(entry.m_order.m_uID)) return;
            RecipeList.Entry copy = new RecipeList.Entry();
            copy.Copy(entry);
            result.Add(copy);
        }

        private static void DrawWindow(int id)
        {
            string scene = CurrentScene();
            LevelSettings level = GetLevel(true);
            List<RecipeList.Entry> entries = GetKnownRecipeEntries();
            GUILayout.Label("关卡：" + scene + "    按 " + ShortcutText() + " 关闭");
            GUILayout.Label("作者：" + PluginInfo.Author);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(s_mode == 0, "设置菜单（仅主机）", GUI.skin.button)) s_mode = 0;
            if (GUILayout.Toggle(s_mode == 1, "前 N 道菜排除", GUI.skin.button)) s_mode = 1;
            GUILayout.EndHorizontal();
            if (s_mode == 0)
            {
                GUILayout.BeginHorizontal();
                for (int i = 0; i < PresetCount; i++)
                {
                    if (GUILayout.Toggle(level.ActivePreset == i, i.ToString(), GUI.skin.button) && level.ActivePreset != i) { level.ActivePreset = i; Save(); MenuGenerationFeature.OnConfigurationChanged(); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("当前预设：" + level.ActivePreset + "（0–4 共五组）");
            }
            else DrawCount(level, scene);
            GUILayout.Label(s_mode == 0 ? "勾选本关卡允许出现的菜谱；取消全部会恢复游戏原菜单。" : "前 N 道实际生成的订单不会出现下方勾选的菜品；仅主机生效。");
            s_scroll = GUILayout.BeginScrollView(s_scroll, GUILayout.Height(475f));
            List<string> selected = s_mode == 0 ? GetActivePreset(true) : level.InitialExcluded;
            for (int i = 0; i < entries.Count; i++)
            {
                RecipeList.Entry entry = entries[i];
                if (entry == null || entry.m_order == null) continue;
                string key = entry.m_order.m_uID.ToString();
                bool oldValue = selected.Contains(key);
                GUILayout.BeginHorizontal(GUILayout.Height(58f));
                bool newValue = GUILayout.Toggle(oldValue, string.Empty, GUILayout.Width(24f));
                DrawRecipeSprites(entry, 46f);
                GUILayout.Label(entry.m_order.name ?? ("#" + key));
                GUILayout.EndHorizontal();
                if (newValue != oldValue) { if (newValue) selected.Add(key); else selected.Remove(key); Save(); MenuGenerationFeature.OnConfigurationChanged(); }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全选")) SetSelection(level, entries, true);
            if (GUILayout.Button("全不选")) SetSelection(level, entries, false);
            if (GUILayout.Button("恢复默认")) { if (s_mode == 0) GetActivePreset(true).Clear(); else { level.InitialCount = 0; level.InitialExcluded.Clear(); s_countText = "0"; } Save(); MenuGenerationFeature.OnConfigurationChanged(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("配置已自动保存");
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private static void DrawCount(LevelSettings level, string scene)
        {
            if (s_countScene != scene) { s_countScene = scene; s_countText = Mathf.Max(0, level.InitialCount).ToString(); }
            GUILayout.BeginHorizontal();
            GUILayout.Label("前 N 道：", GUILayout.Width(82f));
            s_countText = GUILayout.TextField(s_countText ?? "0", GUILayout.Width(80f));
            int count;
            if (!int.TryParse(s_countText, out count)) count = 0;
            count = Mathf.Clamp(count, 0, 9999);
            if (s_countText != count.ToString()) s_countText = count.ToString();
            if (level.InitialCount != count) { level.InitialCount = count; Save(); MenuGenerationFeature.OnConfigurationChanged(); }
            GUILayout.EndHorizontal();
        }

        private static void DrawRecipeSprites(RecipeList.Entry entry, float size)
        {
            List<Sprite> sprites = RecipeSpriteHelper.Collect(entry);
            for (int i = 0; i < sprites.Count; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || sprite.texture == null) continue;
                Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                Rect source = sprite.textureRect;
                Rect uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
                    source.width / sprite.texture.width, source.height / sprite.texture.height);
                GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
            }
        }

        private static void SetSelection(LevelSettings level, List<RecipeList.Entry> entries, bool selected)
        {
            List<string> target = s_mode == 0 ? GetActivePreset(true) : level.InitialExcluded;
            target.Clear();
            if (selected) for (int i = 0; i < entries.Count; i++) if (entries[i] != null && entries[i].m_order != null) target.Add(entries[i].m_order.m_uID.ToString());
            Save(); MenuGenerationFeature.OnConfigurationChanged();
        }

        private static LevelSettings GetLevel(bool create)
        {
            EnsureLoaded();
            string scene = CurrentScene();
            LevelSettings level = s_levels.Find(x => x != null && x.Scene == scene);
            if (level == null && create) { level = new LevelSettings(); level.Scene = scene; for (int i = 0; i < PresetCount; i++) level.Presets.Add(new List<string>()); s_levels.Add(level); Save(); }
            if (level != null) while (level.Presets.Count < PresetCount) level.Presets.Add(new List<string>());
            return level;
        }

        private static List<string> GetActivePreset(bool create)
        {
            LevelSettings level = GetLevel(create);
            if (level == null) return null;
            level.ActivePreset = Mathf.Clamp(level.ActivePreset, 0, PresetCount - 1);
            return level.Presets[level.ActivePreset];
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>(); lines.Add(Header);
                for (int i = 0; i < s_levels.Count; i++)
                {
                    LevelSettings l = s_levels[i]; if (l == null || string.IsNullOrEmpty(l.Scene)) continue;
                    List<string> presetData = new List<string>();
                    for (int j = 0; j < PresetCount; j++) presetData.Add(EncodeList(l.Presets[j]));
                    lines.Add("L|" + Encode(l.Scene) + "|" + Mathf.Clamp(l.ActivePreset, 0, PresetCount - 1) + "|" + Encode(string.Join(";", presetData.ToArray()) + "~" + Mathf.Clamp(l.InitialCount, 0, 9999) + "~" + EncodeList(l.InitialExcluded)));
                }
                File.WriteAllLines(Path.Combine(Paths.ConfigPath, FileName), lines.ToArray());
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Menu editor config save failed: " + ex.Message); }
        }

        private static void DecodePresets(LevelSettings level, string encoded)
        {
            string[] fields = Decode(encoded).Split('~');
            string[] presets = fields.Length > 0 ? fields[0].Split(';') : new string[0];
            for (int i = 0; i < PresetCount; i++) level.Presets.Add(i < presets.Length ? DecodeList(presets[i]) : new List<string>());
            if (fields.Length > 1) int.TryParse(fields[1], out level.InitialCount);
            if (fields.Length > 2) level.InitialExcluded.AddRange(DecodeList(fields[2]));
        }

        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty)); }
        private static string Decode(string value) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty)); } catch { return string.Empty; } }
        private static string EncodeList(List<string> values) { return Encode(values == null ? string.Empty : string.Join("\u001f", values.ToArray())); }
        private static List<string> DecodeList(string value) { string decoded = Decode(value); return string.IsNullOrEmpty(decoded) ? new List<string>() : new List<string>(decoded.Split(new[] { '\u001f' }, StringSplitOptions.RemoveEmptyEntries)); }
        private static string CurrentScene() { if (string.IsNullOrEmpty(s_scene)) s_scene = SceneManager.GetActiveScene().name; return string.IsNullOrEmpty(s_scene) ? "unknown" : s_scene; }
        private static bool IsInLevel() { return UnityEngine.Object.FindObjectOfType<RecipeFlowGUI>() != null || MenuGenerationFeature.IsInRound; }
        private static bool RuntimeHotkeyPressed()
        {
            try
            {
                KeyboardShortcut shortcut = Plugin.ConfigKey == null ? new KeyboardShortcut(KeyCode.F6) : Plugin.ConfigKey.Value;
                if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey)) return false;
                foreach (KeyCode modifier in shortcut.Modifiers) if (!Input.GetKey(modifier)) return false;
                return true;
            }
            catch { return Input.GetKeyDown(KeyCode.F6); }
        }
        private static string ShortcutText() { return Plugin.ConfigKey == null ? "F6" : Plugin.ConfigKey.Value.ToString(); }
    }

    internal static class RecipeSpriteHelper
    {
        internal static List<Sprite> Collect(RecipeList.Entry entry)
        {
            List<Sprite> sprites = new List<Sprite>();
            if (entry == null || entry.m_order == null || entry.m_order.m_orderGuiDescription == null) return sprites;
            RecipeWidgetUIController.RecipeTileData[] tiles = entry.m_order.m_orderGuiDescription;
            int head = FindHead(tiles);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (i == head || tiles[i] == null || tiles[i].m_tileDefinition == null) continue;
                Add(sprites, tiles[i].m_tileDefinition.m_mainPictures);
                Add(sprites, tiles[i].m_tileDefinition.m_modifierPictures);
            }
            if (sprites.Count == 0)
                for (int i = 0; i < tiles.Length; i++) if (tiles[i] != null && tiles[i].m_tileDefinition != null) Add(sprites, tiles[i].m_tileDefinition.m_mainPictures);
            return sprites;
        }

        private static int FindHead(RecipeWidgetUIController.RecipeTileData[] tiles)
        {
            if (tiles == null || tiles.Length == 0) return -1;
            bool[] hasParent = new bool[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null || tiles[i].m_children == null) continue;
                for (int j = 0; j < tiles[i].m_children.Count; j++)
                {
                    int child = tiles[i].m_children[j];
                    if (child >= 0 && child < hasParent.Length) hasParent[child] = true;
                }
            }
            for (int i = 0; i < hasParent.Length; i++) if (!hasParent[i]) return i;
            return -1;
        }

        private static void Add(List<Sprite> target, List<Sprite> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) if (source[i] != null && !target.Contains(source[i])) target.Add(source[i]);
        }
    }
}
