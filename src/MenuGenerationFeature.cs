using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OC2MenuEditorMod
{
    internal static class MenuGenerationFeature
    {
        private static bool s_inRound;
        private static int s_initialRemaining;
        private static ServerOrderControllerBase s_controller;
        private static RoundDataBase s_roundData;
        private static RoundInstanceDataBase s_roundInstance;
        private static FieldInfo s_roundDataField;
        private static FieldInfo s_roundInstanceField;
        private static FieldInfo s_frequenciesField;
        private static FieldInfo s_countField;
        private static Type s_stateType;
        private static MethodInfo s_addEntryMethod;

        internal static bool IsInRound { get { return s_inRound; } }
        internal static void EnsureBootstrap() { EnsureReflection(); }
        internal static void OnSceneLoaded() { s_inRound = false; s_initialRemaining = 0; s_controller = null; s_roundData = null; s_roundInstance = null; }
        internal static void OnConfigurationChanged() { s_initialRemaining = MenuFilterFeature.GetInitialExclusionCount(); }

        private static void OnRoundActivationChanged(bool enabled)
        {
            s_inRound = enabled;
            if (enabled) s_initialRemaining = MenuFilterFeature.GetInitialExclusionCount();
        }

        private static bool TryConsumeNextOrder(ServerOrderControllerBase controller)
        {
            if (controller == null || !IsAuthority()) return true;
            if (!MenuFilterFeature.HasConfiguredHostMenu() && !MenuFilterFeature.HasActiveInitialExclusion()) return true;
            s_controller = controller;
            if (!CaptureRoundState()) return true;
            RecipeList.Entry[] entries;
            if (!TryGetNextAllowed(out entries)) return !MenuFilterFeature.HasConfiguredHostMenu();
            try
            {
                for (int i = 0; i < entries.Length; i++) if (entries[i] != null) s_addEntryMethod.Invoke(controller, new object[] { CloneEntry(entries[i]) });
                return false;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Menu editor order override failed: " + ex.Message); return true; }
        }

        private static bool TryGetNextAllowed(out RecipeList.Entry[] entries)
        {
            entries = null;
            int[] startFrequencies = GetFrequencies();
            int startCount = GetRecipeCount();
            bool sawInitialRejection = false;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int attempt = 0; attempt < 512; attempt++)
                {
                    int[] frequencies = GetFrequencies();
                    int count = GetRecipeCount();
                    RecipeList.Entry[] candidate = s_roundData.GetNextRecipe(s_roundInstance);
                    if (candidate == null || candidate.Length == 0) break;
                    bool rejected = false;
                    bool rejectedInitial = false;
                    for (int i = 0; i < candidate.Length; i++)
                    {
                        RecipeList.Entry entry = candidate[i];
                        if (entry == null || !MenuFilterFeature.ShouldUseInHostMenu(entry)) { rejected = true; break; }
                        if (i < s_initialRemaining && MenuFilterFeature.ShouldExcludeFromInitialMenu(entry)) { rejected = true; rejectedInitial = true; break; }
                    }
                    if (!rejected)
                    {
                        if (s_initialRemaining > 0) s_initialRemaining = Mathf.Max(0, s_initialRemaining - candidate.Length);
                        entries = candidate;
                        return true;
                    }
                    if (rejectedInitial) sawInitialRejection = true;
                    int afterCount = GetRecipeCount();
                    int[] afterFrequencies = GetFrequencies();
                    bool fixedAdvanced = afterCount > count
                        && (AreEqual(frequencies, afterFrequencies)
                            || (frequencies == null && (s_roundData is ScriptedRoundData || s_roundData is BossRoundData)));
                    RestoreState(frequencies, fixedAdvanced ? afterCount : count);
                }
                if (pass == 0 && sawInitialRejection && s_initialRemaining > 0)
                {
                    RestoreState(startFrequencies, startCount);
                    s_initialRemaining = 0;
                    Plugin.Log.LogWarning("Initial exclusion covered every eligible recipe; restored the normal menu for this round.");
                    continue;
                }
                break;
            }
            if (MenuFilterFeature.HasConfiguredHostMenu()) Plugin.Log.LogWarning("Could not find a recipe allowed by the configured host menu.");
            return false;
        }

        private static bool CaptureRoundState()
        {
            EnsureReflection();
            s_roundData = s_roundDataField == null ? null : s_roundDataField.GetValue(s_controller) as RoundDataBase;
            s_roundInstance = s_roundInstanceField == null ? null : s_roundInstanceField.GetValue(s_controller) as RoundInstanceDataBase;
            return s_roundData != null && s_roundInstance != null;
        }
        private static void EnsureReflection()
        {
            if (s_roundDataField == null) s_roundDataField = AccessTools.Field(typeof(ServerOrderControllerBase), "m_roundData");
            if (s_roundInstanceField == null) s_roundInstanceField = AccessTools.Field(typeof(ServerOrderControllerBase), "m_roundInstanceData");
            if (s_addEntryMethod == null) s_addEntryMethod = AccessTools.Method(typeof(ServerOrderControllerBase), "AddNewOrder", new[] { typeof(RecipeList.Entry) });
            if (s_roundInstance == null) return;
            Type type = s_roundInstance.GetType();
            if (type != s_stateType) { s_stateType = type; s_frequenciesField = null; s_countField = null; }
            if (s_frequenciesField == null) s_frequenciesField = AccessTools.Field(type, "CumulativeFrequencies");
            if (s_countField == null) s_countField = AccessTools.Field(type, "RecipeCount");
        }
        private static int[] GetFrequencies() { EnsureReflection(); int[] value = s_frequenciesField == null || s_roundInstance == null ? null : s_frequenciesField.GetValue(s_roundInstance) as int[]; return value == null ? null : (int[])value.Clone(); }
        private static int GetRecipeCount() { EnsureReflection(); return s_countField == null || s_roundInstance == null ? -1 : (int)s_countField.GetValue(s_roundInstance); }
        private static void RestoreState(int[] frequencies, int count) { EnsureReflection(); if (frequencies != null && s_frequenciesField != null) s_frequenciesField.SetValue(s_roundInstance, frequencies); if (count >= 0 && s_countField != null) s_countField.SetValue(s_roundInstance, count); }
        private static bool AreEqual(int[] a, int[] b) { if (a == null || b == null || a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }
        private static RecipeList.Entry CloneEntry(RecipeList.Entry entry) { RecipeList.Entry copy = new RecipeList.Entry(); copy.Copy(entry); return copy; }
        private static bool IsAuthority() { Type type = AccessTools.TypeByName("ConnectionStatus"); MethodInfo session = type == null ? null : AccessTools.Method(type, "IsInSession"); MethodInfo host = type == null ? null : AccessTools.Method(type, "IsHost"); try { return session == null || !(bool)session.Invoke(null, null) || (host != null && (bool)host.Invoke(null, null)); } catch { return true; } }

        [HarmonyPatch(typeof(ServerTeamMonitor), "Initialise")]
        private static class MonitorPatch { private static void Postfix(ServerTeamMonitor __instance) { s_controller = __instance == null ? null : __instance.OrdersController; s_initialRemaining = MenuFilterFeature.GetInitialExclusionCount(); } }
        [HarmonyPatch(typeof(ServerFlowControllerBase), "SetRoundBehaviourActivation")]
        private static class RoundPatch { private static void Postfix(bool _enabled) { OnRoundActivationChanged(_enabled); } }
        [HarmonyPatch(typeof(ServerOrderControllerBase), "AddNewOrder", new Type[] { })]
        private static class AddPatch { private static bool Prefix(ServerOrderControllerBase __instance) { return TryConsumeNextOrder(__instance); } }
    }
}
