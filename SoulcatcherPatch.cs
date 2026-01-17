using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SoulcatcherCrossbowFix
{
    [BepInPlugin("ru.custom.soulcatcher_crossbowfix", "Soulcatcher Crossbow Fix", "1.0.1")]
    [BepInDependency("Soulcatcher", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private static ManualLogSource Log;
        private Harmony _harmony;

        // ---------------- CONFIG ----------------
        private static ConfigEntry<bool> CfgEnableDamage;
        private static ConfigEntry<bool> CfgEnableReload;
        private static ConfigEntry<float> CfgReloadMinTime;

        private static ConfigEntry<bool> CfgDebug;
        private static ConfigEntry<bool> CfgDebugDumpWeaponCustomData;
        private static ConfigEntry<bool> CfgDebugDumpDiscovery;
        private static ConfigEntry<float> CfgDebugMinIntervalSec;

        private static ConfigEntry<string> CfgEffectNameHare;
        private static ConfigEntry<string> CfgEffectNameDverger;

        // Rate limit
        private static float _nextLogTime;

        // Effect names
        private const string DefaultEffectHare = "Hare Soul Power";
        private const string DefaultEffectDverger = "Dverger Soul Power";

        // ---------------- RUNTIME RESOLVE ----------------
        private static MethodInfo _miGetEffectPowerGenericDef;
        private static Type _cfgTypeHare;
        private static Type _cfgTypeDverger;

        private static float _nextResolveAttemptTime;

        // Reflection for private field
        private static FieldInfo _fiSEMan_m_character;

        private void Awake()
        {
            Log = Logger;

            CfgEnableDamage = Config.Bind("General", "EnableHareDamageBonus", true, "Enable Hare gem damage bonus for crossbows.");
            CfgEnableReload = Config.Bind("General", "EnableDvergerReloadBonus", true, "Enable Dverger gem reload time reduction for crossbows.");
            CfgReloadMinTime = Config.Bind("General", "MinReloadTime", 0.3f, "Minimum reload time for crossbows when the Dverger gem effect reduces it.");

            CfgEffectNameHare = Config.Bind("Effects", "HareEffectName", DefaultEffectHare, "Jewelcrafting effect name for Hare gem.");
            CfgEffectNameDverger = Config.Bind("Effects", "DvergerEffectName", DefaultEffectDverger, "Jewelcrafting effect name for Dverger gem.");

            CfgDebug = Config.Bind("Debug", "DebugEnabled", false, "Enable verbose debug logging.");
            CfgDebugDumpWeaponCustomData = Config.Bind("Debug", "DumpWeaponCustomData", false, "Dump current weapon m_customData (spammy).");
            CfgDebugDumpDiscovery = Config.Bind("Debug", "DumpDiscovery", true, "Log discovery of GetEffectPower method and config types.");
            CfgDebugMinIntervalSec = Config.Bind("Debug", "MinLogIntervalSeconds", 0.50f, "Rate limit for repeated debug logs.");

            _fiSEMan_m_character = AccessTools.Field(typeof(SEMan), "m_character");

            ResolveJewelcraftingBindings(force: true);

            _harmony = new Harmony("ru.custom.soulcatcher_crossbowfix");
            _harmony.PatchAll();

            LogInfo("[Init] Loaded v1.0.1");
        }

        // ---------------- PATCH: DAMAGE (HARE) ----------------
        [HarmonyPatch(typeof(SEMan), "ModifyAttack")]
        private static class SEMan_ModifyAttack_Patch
        {
            private static void Postfix(SEMan __instance, Skills.SkillType skill, ref HitData hitData)
            {
                try
                {
                    if (CfgEnableDamage == null || !CfgEnableDamage.Value) return;

                    Character ch = _fiSEMan_m_character != null ? _fiSEMan_m_character.GetValue(__instance) as Character : null;
                    if (!(ch is Player player)) return;

                    ItemDrop.ItemData weapon = player.GetCurrentWeapon();
                    if (!IsCrossbow(weapon)) return;

                    float valuePct = GetEffectPercent(player, CfgEffectNameHare != null ? CfgEffectNameHare.Value : DefaultEffectHare);
                    if (valuePct <= 0f)
                    {
                        DebugLogRate("[DMG] Hare value=0 (no gem or not applied).");
                        return;
                    }

                    float mult = 1f + (valuePct / 100f);
                    MultiplyDamage(ref hitData.m_damage, mult);

                    DebugLogRate($"[DMG] Applied Hare. value={valuePct:0.###}% mult={mult:0.###} weapon={weapon?.m_dropPrefab?.name}");
                    DebugDumpWeaponCustomDataRate(weapon);
                }
                catch (Exception e)
                {
                    LogError($"[DMG] ModifyAttack failed: {e}");
                }
            }
        }

        // ---------------- PATCH: RELOAD (DVERGER) ----------------
        // Kept as-is by request (tested working).
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetWeaponLoadingTime")]
        private static class ItemData_GetWeaponLoadingTime_Patch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                try
                {
                    if (CfgEnableReload == null || !CfgEnableReload.Value) return;

                    Player player = Player.m_localPlayer;
                    if (player == null) return;

                    if (!ReferenceEquals(__instance, player.GetCurrentWeapon())) return;
                    if (!IsCrossbow(__instance)) return;

                    float valuePct = GetEffectPercent(player, CfgEffectNameDverger != null ? CfgEffectNameDverger.Value : DefaultEffectDverger);
                    if (valuePct <= 0f)
                    {
                        DebugLogRate("[RLD] Dverger value=0 (no gem or not applied).");
                        return;
                    }

                    float timeMult = Mathf.Clamp01(1f - (valuePct / 100f));
                    float before = __result;
                    __result *= timeMult;

                    float minReload = Mathf.Max(0f, CfgReloadMinTime != null ? CfgReloadMinTime.Value : 0f);
                    if (minReload > 0f)
                    {
                        __result = Mathf.Max(__result, minReload);
                    }

                    DebugLogRate($"[RLD] Applied Dverger. value={valuePct:0.###}% timeMult={timeMult:0.###} loading {before:0.###}->{__result:0.###} weapon={__instance?.m_dropPrefab?.name}");
                    DebugDumpWeaponCustomDataRate(__instance);
                }
                catch (Exception e)
                {
                    LogError($"[RLD] GetWeaponLoadingTime failed: {e}");
                }
            }
        }

        // ---------------- PATCH: ACTIVE CELLS TOOLTIP NUMBERS ----------------
        // Injects current % values into the "Active cells" tooltip lines for crossbow reload/damage.
        // English-only. No custom StatusEffects created.
        [HarmonyPatch(typeof(StatusEffect), "GetTooltipString")]
        private static class StatusEffect_GetTooltipString_Patch
        {
            private static void Postfix(StatusEffect __instance, ref string __result)
            {
                try
                {
                    if (string.IsNullOrEmpty(__result)) return;

                    // Fast filter: we only touch tooltips that mention crossbows.
                    if (__result.IndexOf("crossbow", StringComparison.OrdinalIgnoreCase) < 0) return;

                    Player player = Player.m_localPlayer;
                    if (player == null) return;

                    string hareName = CfgEffectNameHare != null ? CfgEffectNameHare.Value : DefaultEffectHare;
                    string dverName = CfgEffectNameDverger != null ? CfgEffectNameDverger.Value : DefaultEffectDverger;

                    float harePct = GetEffectPercent(player, hareName);
                    float dverPct = GetEffectPercent(player, dverName);

                    if (harePct <= 0f && dverPct <= 0f) return;

                    string patched = InjectNumbers(__result, harePct, dverPct);
                    if (!string.Equals(patched, __result, StringComparison.Ordinal))
                        __result = patched;
                }
                catch (Exception e)
                {
                    LogError($"[UI] Active cells tooltip patch failed: {e}");
                }
            }

            private static string InjectNumbers(string tooltip, float harePct, float dverPct)
            {
                string[] lines = tooltip.Split('\n');
                bool changed = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string t = line.Trim();

                    // Crossbow reload line
                    if (dverPct > 0f && ContainsAll(t, "crossbow", "reload"))
                    {
                        lines[i] = ReplaceKeepIndent(line, $"Crossbow reload time reduced by {dverPct:0.##}%.");
                        changed = true;
                        continue;
                    }

                    // Crossbow damage line (avoid matching the reload line)
                    if (harePct > 0f && ContainsAll(t, "crossbow", "damage") && t.IndexOf("reload", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        lines[i] = ReplaceKeepIndent(line, $"Crossbow damage increased by {harePct:0.##}%.");
                        changed = true;
                        continue;
                    }
                }

                return changed ? string.Join("\n", lines) : tooltip;
            }

            private static bool ContainsAll(string s, params string[] parts)
            {
                if (string.IsNullOrEmpty(s) || parts == null || parts.Length == 0) return false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (s.IndexOf(parts[i], StringComparison.OrdinalIgnoreCase) < 0) return false;
                }
                return true;
            }

            private static string ReplaceKeepIndent(string originalLine, string newText)
            {
                if (string.IsNullOrEmpty(originalLine)) return newText;
                int indent = 0;
                while (indent < originalLine.Length && char.IsWhiteSpace(originalLine[indent])) indent++;
                return indent > 0 ? originalLine.Substring(0, indent) + newText : newText;
            }
        }

        // ---------------- HELPERS ----------------
        private static bool IsCrossbow(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared == null) return false;
            if (weapon.m_shared.m_skillType == Skills.SkillType.Crossbows) return true;

            string prefab = weapon.m_dropPrefab ? weapon.m_dropPrefab.name : null;
            if (string.IsNullOrEmpty(prefab)) return false;

            return prefab.StartsWith("Crossbow", StringComparison.OrdinalIgnoreCase);
        }

        private static void MultiplyDamage(ref HitData.DamageTypes dmg, float mult)
        {
            dmg.m_damage *= mult;
            dmg.m_pierce *= mult;
            dmg.m_blunt *= mult;
            dmg.m_slash *= mult;
            dmg.m_fire *= mult;
            dmg.m_frost *= mult;
            dmg.m_lightning *= mult;
            dmg.m_poison *= mult;
            dmg.m_spirit *= mult;
            dmg.m_chop *= mult;
            dmg.m_pickaxe *= mult;
        }

        // ---------------- JEWELCRAFTING EFFECT POWER (REFLECTION) ----------------
        private static float GetEffectPercent(Player player, string effectName)
        {
            if (player == null) return 0f;
            if (string.IsNullOrWhiteSpace(effectName)) return 0f;

            ResolveJewelcraftingBindings(force: false);

            if (_miGetEffectPowerGenericDef == null)
            {
                DebugLogRate("[JC] GetEffectPower<T> not resolved.");
                return 0f;
            }

            Type cfgType = null;

            if (CfgEffectNameHare != null && string.Equals(effectName, CfgEffectNameHare.Value, StringComparison.OrdinalIgnoreCase))
                cfgType = _cfgTypeHare;
            else if (CfgEffectNameDverger != null && string.Equals(effectName, CfgEffectNameDverger.Value, StringComparison.OrdinalIgnoreCase))
                cfgType = _cfgTypeDverger;

            if (cfgType == null)
            {
                if (effectName.IndexOf("Hare", StringComparison.OrdinalIgnoreCase) >= 0) cfgType = _cfgTypeHare;
                if (effectName.IndexOf("Dver", StringComparison.OrdinalIgnoreCase) >= 0) cfgType = _cfgTypeDverger;
            }

            if (cfgType == null)
            {
                DebugLogRate($"[JC] Config type not resolved for effect '{effectName}'.");
                return 0f;
            }

            try
            {
                var gm = _miGetEffectPowerGenericDef.MakeGenericMethod(cfgType);
                object cfgObj = gm.Invoke(null, new object[] { player, effectName });
                if (cfgObj == null) return 0f;

                float v = ExtractFirstFloat(cfgType, cfgObj);

                if (CfgDebug != null && CfgDebug.Value && CfgDebugDumpDiscovery != null && CfgDebugDumpDiscovery.Value)
                    LogInfo($"[JC] Effect '{effectName}' cfgType='{cfgType.FullName}' value={v:0.###}");

                return v;
            }
            catch (Exception e)
            {
                DebugLogRate($"[JC] GetEffectPower invoke failed: {e.GetType().Name}: {e.Message}");
                return 0f;
            }
        }

        private static float ExtractFirstFloat(Type cfgType, object cfgObj)
        {
            try
            {
                var fValue = cfgType.GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fValue != null && fValue.FieldType == typeof(float))
                    return (float)fValue.GetValue(cfgObj);

                var fPower = cfgType.GetField("Power", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fPower != null && fPower.FieldType == typeof(float))
                    return (float)fPower.GetValue(cfgObj);

                var fields = cfgType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var ff = fields.FirstOrDefault(x => x.FieldType == typeof(float));
                if (ff != null) return (float)ff.GetValue(cfgObj);

                var props = cfgType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var pf = props.FirstOrDefault(x => x.PropertyType == typeof(float) && x.GetIndexParameters().Length == 0);
                if (pf != null) return (float)pf.GetValue(cfgObj, null);
            }
            catch { }
            return 0f;
        }

        private static void ResolveJewelcraftingBindings(bool force)
        {
            if (!force)
            {
                if (Time.time < _nextResolveAttemptTime) return;
                _nextResolveAttemptTime = Time.time + 1.0f;
            }

            bool haveMethod = _miGetEffectPowerGenericDef != null;
            bool haveTypes = _cfgTypeHare != null && _cfgTypeDverger != null;
            if (haveMethod && haveTypes) return;

            try
            {
                if (_miGetEffectPowerGenericDef == null)
                {
                    var asms = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in asms)
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }

                        for (int ti = 0; ti < types.Length; ti++)
                        {
                            var t = types[ti];
                            if (t == null) continue;

                            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            for (int mi = 0; mi < methods.Length; mi++)
                            {
                                var m = methods[mi];
                                if (m == null) continue;
                                if (m.Name != "GetEffectPower") continue;
                                if (!m.IsGenericMethodDefinition) continue;

                                var ps = m.GetParameters();
                                if (ps.Length != 2) continue;
                                if (ps[0].ParameterType != typeof(Player)) continue;
                                if (ps[1].ParameterType != typeof(string)) continue;

                                _miGetEffectPowerGenericDef = m;
                                break;
                            }

                            if (_miGetEffectPowerGenericDef != null) break;
                        }

                        if (_miGetEffectPowerGenericDef != null) break;
                    }

                    if (CfgDebug != null && CfgDebug.Value && CfgDebugDumpDiscovery != null && CfgDebugDumpDiscovery.Value)
                    {
                        LogInfo(_miGetEffectPowerGenericDef != null
                            ? $"[DISCOVERY] Found GetEffectPower<T>: {_miGetEffectPowerGenericDef.DeclaringType?.FullName}::{_miGetEffectPowerGenericDef.Name}"
                            : "[DISCOVERY] GetEffectPower<T> not found (yet).");
                    }
                }
            }
            catch (Exception e)
            {
                if (CfgDebug != null && CfgDebug.Value) LogError($"[DISCOVERY] GetEffectPower<T> search failed: {e}");
            }

            try
            {
                if (_cfgTypeHare == null)
                    _cfgTypeHare = FindTypeByFragments(new[] { "Hare", "Soul", "Power" });

                if (_cfgTypeDverger == null)
                    _cfgTypeDverger = FindTypeByFragments(new[] { "Dver", "Soul", "Power" });

                if (CfgDebug != null && CfgDebug.Value && CfgDebugDumpDiscovery != null && CfgDebugDumpDiscovery.Value)
                {
                    LogInfo($"[DISCOVERY] Hare cfgType: {_cfgTypeHare?.FullName ?? "NOT FOUND"}");
                    LogInfo($"[DISCOVERY] Dverger cfgType: {_cfgTypeDverger?.FullName ?? "NOT FOUND"}");
                }
            }
            catch (Exception e)
            {
                if (CfgDebug != null && CfgDebug.Value) LogError($"[DISCOVERY] Config type search failed: {e}");
            }
        }

        private static Type FindTypeByFragments(string[] fragments)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();

            var ordered = asms.OrderByDescending(a =>
            {
                string n = a.GetName().Name ?? "";
                int score = 0;
                if (n.IndexOf("Soulcatcher", StringComparison.OrdinalIgnoreCase) >= 0) score += 2;
                if (n.IndexOf("Jewelcrafting", StringComparison.OrdinalIgnoreCase) >= 0) score += 1;
                return score;
            });

            foreach (var asm in ordered)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    string fn = t.FullName;
                    if (string.IsNullOrEmpty(fn)) continue;

                    bool ok = true;
                    for (int i = 0; i < fragments.Length; i++)
                    {
                        if (fn.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) continue;

                    bool hasFloat =
                        t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(f => f.FieldType == typeof(float)) ||
                        t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(p => p.PropertyType == typeof(float) && p.GetIndexParameters().Length == 0);

                    if (!hasFloat) continue;

                    return t;
                }
            }

            return null;
        }

        // ---------------- LOGGING ----------------
        private static void LogInfo(string msg)
        {
            try { Log?.LogInfo(msg); }
            catch { Debug.Log(msg); }
        }

        private static void LogError(string msg)
        {
            try { Log?.LogError(msg); }
            catch { Debug.LogError(msg); }
        }

        private static void DebugLogRate(string msg)
        {
            if (CfgDebug == null || !CfgDebug.Value) return;

            float min = CfgDebugMinIntervalSec != null ? Mathf.Max(0.05f, CfgDebugMinIntervalSec.Value) : 0.5f;
            if (Time.time < _nextLogTime) return;

            _nextLogTime = Time.time + min;
            LogInfo(msg);
        }

        private static void DebugDumpWeaponCustomDataRate(ItemDrop.ItemData weapon)
        {
            if (CfgDebug == null || !CfgDebug.Value) return;
            if (CfgDebugDumpWeaponCustomData == null || !CfgDebugDumpWeaponCustomData.Value) return;
            if (weapon == null) return;

            float min = CfgDebugMinIntervalSec != null ? Mathf.Max(0.05f, CfgDebugMinIntervalSec.Value) : 0.5f;
            if (Time.time < _nextLogTime) return;

            if (weapon.m_customData == null || weapon.m_customData.Count == 0)
            {
                LogInfo("[DBG] weapon.m_customData is empty");
                return;
            }

            foreach (var kv in weapon.m_customData)
            {
                string v = kv.Value ?? "";
                if (v.Length > 160) v = v.Substring(0, 160) + "...";
                LogInfo($"[DBG] customData: '{kv.Key}' = '{v}'");
            }
        }
    }
}
