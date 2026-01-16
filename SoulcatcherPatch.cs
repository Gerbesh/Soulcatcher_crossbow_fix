using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SoulcatcherCrossbowFix
{
    [BepInPlugin("ru.custom.soulcatcher_crossbowfix", "Soulcatcher Crossbow Fix", "1.0.0")]
    [BepInDependency("Soulcatcher", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony _harmony;

        // ---------------- CONFIG ----------------
        private static ConfigEntry<bool> _cfgEnableDamage;
        private static ConfigEntry<bool> _cfgEnableReload;
        private static ConfigEntry<float> _cfgReloadMinTime;

        private static ConfigEntry<bool> _cfgDebug;
        private static ConfigEntry<bool> _cfgDebugDumpWeaponCustomData;
        private static ConfigEntry<bool> _cfgDebugDumpDiscovery;
        private static ConfigEntry<float> _cfgDebugMinIntervalSec;

        private static ConfigEntry<string> _cfgEffectNameHare;
        private static ConfigEntry<string> _cfgEffectNameDverger;

        // rate limit
        private static float _nextLogTime;

        // Effect names
        private const string DefaultEffectHare = "Hare Soul Power";
        private const string DefaultEffectDverger = "Dverger Soul Power";

        private static StatusEffect _hareStatusEffect;
        private static StatusEffect _dvergerStatusEffect;
        private static Sprite _statusIcon;
        private static float _nextStatusRefreshTime;
        private const float StatusRefreshInterval = 0.5f;

        // ---------------- RUNTIME RESOLVE ----------------
        private static bool _resolved;
        private static MethodInfo _miGetEffectPowerGenericDef;
        private static Type _cfgTypeHare;
        private static Type _cfgTypeDverger;

        // Reflection for private field
        private static FieldInfo _fiSEMan_m_character;

        private void Awake()
        {
            // Mapping: Hare -> DAMAGE, Dverger -> RELOAD
            _cfgEnableDamage = Config.Bind("General", "EnableHareDamageBonus", true, "Enable Hare gem damage bonus for crossbows.");
            _cfgEnableReload = Config.Bind("General", "EnableDvergerReloadBonus", true, "Enable Dverger gem reload time reduction for crossbows.");
            _cfgReloadMinTime = Config.Bind("General", "MinReloadTime", 0.3f, "Minimum reload time for crossbows when the Dverger gem effect reduces it.");

            _cfgEffectNameHare = Config.Bind("Effects", "HareEffectName", DefaultEffectHare, "Jewelcrafting effect name for Hare gem.");
            _cfgEffectNameDverger = Config.Bind("Effects", "DvergerEffectName", DefaultEffectDverger, "Jewelcrafting effect name for Dverger gem.");

            _cfgDebug = Config.Bind("Debug", "DebugEnabled", false, "Enable verbose debug logging.");
            _cfgDebugDumpWeaponCustomData = Config.Bind("Debug", "DumpWeaponCustomData", false, "Dump current weapon m_customData (spammy).");
            _cfgDebugDumpDiscovery = Config.Bind("Debug", "DumpDiscovery", true, "Log discovery of GetEffectPower method and config types.");
            _cfgDebugMinIntervalSec = Config.Bind("Debug", "MinLogIntervalSeconds", 0.50f, "Rate limit for repeated debug logs.");

            // Reflection
            _fiSEMan_m_character = AccessTools.Field(typeof(SEMan), "m_character");

            ResolveJewelcraftingBindings();

            _harmony = new Harmony("ru.custom.soulcatcher_crossbowfix");
            _harmony.PatchAll();

            LogInfo("[Init] Loaded v1.1.4 (Client-side, fixed SEMan private field access)");
        }

        // ---------------- PATCH: DAMAGE (HARE) ----------------
        [HarmonyPatch(typeof(SEMan), "ModifyAttack")]
        private static class SEMan_ModifyAttack_Patch
        {
            private static void Postfix(SEMan __instance, Skills.SkillType skill, ref HitData hitData)
            {
                try
                {
                    if (!_cfgEnableDamage.Value) return;

                    Character ch = _fiSEMan_m_character?.GetValue(__instance) as Character;
                    if (ch is not Player player) return;

                    ItemDrop.ItemData weapon = player.GetCurrentWeapon();
                    if (!IsCrossbow(weapon)) return;

                    float valuePct = GetEffectPercent(player, _cfgEffectNameHare?.Value);
                    if (valuePct <= 0f)
                    {
                        DebugLogRate($"[DMG] Effect '{_cfgEffectNameHare?.Value}' value=0 (no gem or not applied).");
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

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static class ObjectDB_Awake_Patch
        {
            private static void Postfix(ObjectDB __instance)
            {
                EnsureCustomStatusEffects(__instance);
            }
        }

        // ---------------- PATCH: RELOAD (DVERGER) ----------------
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetWeaponLoadingTime")]
        private static class ItemData_GetWeaponLoadingTime_Patch
        {
            private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
            {
                try
                {
                    if (!_cfgEnableReload.Value) return;

                    Player player = Player.m_localPlayer;
                    if (player == null) return;

                    if (!ReferenceEquals(__instance, player.GetCurrentWeapon())) return;

                    if (!IsCrossbow(__instance)) return;

                    float valuePct = GetEffectPercent(player, _cfgEffectNameDverger?.Value);
                    if (valuePct <= 0f)
                    {
                        DebugLogRate($"[RLD] Effect '{_cfgEffectNameDverger?.Value}' value=0 (no gem or not applied).");
                        return;
                    }

                    float timeMult = Mathf.Clamp01(1f - (valuePct / 100f));
                    float before = __result;
                    __result *= timeMult;

                    float minReload = Mathf.Max(0f, _cfgReloadMinTime?.Value ?? 0f);
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

        // ---------------- HELPERS ----------------
        private static bool IsCrossbow(ItemDrop.ItemData weapon)
        {
            if (weapon?.m_shared == null) return false;

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

        private void Update()
        {
            if (Time.time < _nextStatusRefreshTime) return;

            _nextStatusRefreshTime = Time.time + StatusRefreshInterval;

            Player player = Player.m_localPlayer;
            if (player == null) return;

            ObjectDB db = ObjectDB.instance;
            if (db != null)
            {
                EnsureCustomStatusEffects(db);
            }

            RefreshStatusEffect(player, _hareStatusEffect, _cfgEffectNameHare?.Value);
            RefreshStatusEffect(player, _dvergerStatusEffect, _cfgEffectNameDverger?.Value);
        }

        private static void RefreshStatusEffect(Player player, StatusEffect effect, string effectName)
        {
            if (player == null || effect == null) return;
            if (string.IsNullOrWhiteSpace(effectName)) return;

            SEMan seMan = player.GetSEMan();
            if (seMan == null) return;

            float valuePct = GetEffectPercent(player, effectName);
            if (valuePct > 0f)
            {
                seMan.AddStatusEffect(effect, resetTime: true);
            }
            else
            {
                seMan.RemoveStatusEffect(effect.NameHash(), quiet: true);
            }
        }

        private static void EnsureCustomStatusEffects(ObjectDB objectDb)
        {
            if (objectDb == null) return;
            if (_hareStatusEffect != null && _dvergerStatusEffect != null) return;

            Sprite icon = FindStatusIcon(objectDb);

            if (_hareStatusEffect == null)
            {
                _hareStatusEffect = CreateCustomStatusEffect("SE_SoulcatcherHare", _cfgEffectNameHare?.Value ?? DefaultEffectHare,
                    "Hare gems now also show their buff icon while increasing crossbow damage.", icon);
            }
            if (_dvergerStatusEffect == null)
            {
                _dvergerStatusEffect = CreateCustomStatusEffect("SE_SoulcatcherDverger", _cfgEffectNameDverger?.Value ?? DefaultEffectDverger,
                    "Dverger gems now also show their buff icon while reducing crossbow reload time.", icon);
            }

            AddStatusEffectToObjectDB(objectDb, _hareStatusEffect);
            AddStatusEffectToObjectDB(objectDb, _dvergerStatusEffect);
        }

        private static StatusEffect CreateCustomStatusEffect(string unityName, string displayName, string tooltip, Sprite icon)
        {
            StatusEffect statusEffect = ScriptableObject.CreateInstance<StatusEffect>();
            statusEffect.name = unityName;
            statusEffect.m_name = displayName ?? unityName;
            statusEffect.m_tooltip = tooltip ?? statusEffect.m_name;
            statusEffect.m_icon = icon;
            statusEffect.m_ttl = 0f;
            statusEffect.m_startEffects = new EffectList();
            statusEffect.m_stopEffects = new EffectList();
            return statusEffect;
        }

        private static void AddStatusEffectToObjectDB(ObjectDB objectDb, StatusEffect effect)
        {
            if (objectDb == null || effect == null) return;
            if (objectDb.GetStatusEffect(effect.NameHash()) != null) return;
            objectDb.m_StatusEffects.Add(effect);
        }

        private static Sprite FindStatusIcon(ObjectDB objectDb)
        {
            if (_statusIcon != null) return _statusIcon;
            if (objectDb == null) return null;

            foreach (var statusEffect in objectDb.m_StatusEffects)
            {
                if (statusEffect == null) continue;
                if (string.Equals(statusEffect.m_name, "Rested", StringComparison.OrdinalIgnoreCase) && statusEffect.m_icon != null)
                {
                    _statusIcon = statusEffect.m_icon;
                    return _statusIcon;
                }
            }

            foreach (var statusEffect in objectDb.m_StatusEffects)
            {
                if (statusEffect == null) continue;
                if (statusEffect.m_icon != null)
                {
                    _statusIcon = statusEffect.m_icon;
                    break;
                }
            }

            return _statusIcon;
        }

        // ---------------- JEWELCRAFTING EFFECT POWER ----------------
        private static float GetEffectPercent(Player player, string effectName)
        {
            if (player == null) return 0f;
            if (string.IsNullOrWhiteSpace(effectName)) return 0f;

            ResolveJewelcraftingBindings();
            if (_miGetEffectPowerGenericDef == null)
            {
                DebugLogRate("[JC] GetEffectPower<T> not resolved.");
                return 0f;
            }

            Type cfgType = null;

            if (string.Equals(effectName, _cfgEffectNameHare?.Value, StringComparison.OrdinalIgnoreCase))
                cfgType = _cfgTypeHare;
            else if (string.Equals(effectName, _cfgEffectNameDverger?.Value, StringComparison.OrdinalIgnoreCase))
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

                if (_cfgDebug?.Value == true && _cfgDebugDumpDiscovery?.Value == true)
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

        private static void ResolveJewelcraftingBindings()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();

                IEnumerable<Assembly> ordered = asms
                    .OrderByDescending(a => (a.GetName().Name ?? "").IndexOf("Jewelcrafting", StringComparison.OrdinalIgnoreCase) >= 0);

                foreach (var asm in ordered)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null) continue;

                        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var m in methods)
                        {
                            if (m.Name != "GetEffectPower") continue;
                            if (!m.IsGenericMethodDefinition) continue;

                            var ps = m.GetParameters();
                            if (ps.Length != 2) continue;
                            if (ps[0].ParameterType != typeof(Player)) continue;
                            if (ps[1].ParameterType != typeof(string)) continue;

                            _miGetEffectPowerGenericDef = m;
                            goto FoundMethod;
                        }
                    }
                }
            FoundMethod: ;

                if (_cfgDebug?.Value == true && _cfgDebugDumpDiscovery?.Value == true)
                {
                    LogInfo(_miGetEffectPowerGenericDef != null
                        ? $"[DISCOVERY] Found GetEffectPower<T>: {_miGetEffectPowerGenericDef.DeclaringType?.FullName}::{_miGetEffectPowerGenericDef.Name}"
                        : "[DISCOVERY] GetEffectPower<T> not found.");
                }
            }
            catch (Exception e)
            {
                if (_cfgDebug?.Value == true) LogError($"[DISCOVERY] GetEffectPower<T> search failed: {e}");
            }

            try
            {
                _cfgTypeHare = FindConfigTypeByFragments(new[] { "Hare", "Soul", "Power" });
                _cfgTypeDverger = FindConfigTypeByFragments(new[] { "Dver", "Soul", "Power" });

                if (_cfgDebug?.Value == true && _cfgDebugDumpDiscovery?.Value == true)
                {
                    LogInfo($"[DISCOVERY] Hare cfgType: {_cfgTypeHare?.FullName ?? "NOT FOUND"}");
                    LogInfo($"[DISCOVERY] Dverger cfgType: {_cfgTypeDverger?.FullName ?? "NOT FOUND"}");
                }
            }
            catch (Exception e)
            {
                if (_cfgDebug?.Value == true) LogError($"[DISCOVERY] Config type search failed: {e}");
            }
        }

        private static Type FindConfigTypeByFragments(string[] fragments)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();

            IEnumerable<Assembly> ordered = asms
                .OrderByDescending(a => (a.GetName().Name ?? "").IndexOf("Soulcatcher", StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var asm in ordered)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }

                foreach (var t in types)
                {
                    if (t?.FullName == null) continue;

                    if (!string.Equals(t.Name, "Config", StringComparison.OrdinalIgnoreCase)) continue;

                    bool ok = true;
                    for (int i = 0; i < fragments.Length; i++)
                    {
                        if (t.FullName.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (!ok) continue;

                    return t;
                }
            }
            return null;
        }

        // ---------------- DEBUG LOGGING ----------------
        private static void LogInfo(string msg)
        {
            try { BepInEx.Logging.Logger.CreateLogSource("SoulcatcherCrossbowFix").LogInfo(msg); }
            catch { Debug.Log(msg); }
        }

        private static void LogError(string msg)
        {
            try { BepInEx.Logging.Logger.CreateLogSource("SoulcatcherCrossbowFix").LogError(msg); }
            catch { Debug.LogError(msg); }
        }

        private static void DebugLogRate(string msg)
        {
            if (_cfgDebug == null || !_cfgDebug.Value) return;

            float min = _cfgDebugMinIntervalSec != null ? Mathf.Max(0.05f, _cfgDebugMinIntervalSec.Value) : 0.5f;
            if (Time.time < _nextLogTime) return;

            _nextLogTime = Time.time + min;
            LogInfo(msg);
        }

        private static void DebugDumpWeaponCustomDataRate(ItemDrop.ItemData weapon)
        {
            if (_cfgDebug == null || !_cfgDebug.Value) return;
            if (_cfgDebugDumpWeaponCustomData == null || !_cfgDebugDumpWeaponCustomData.Value) return;
            if (weapon == null) return;

            float min = _cfgDebugMinIntervalSec != null ? Mathf.Max(0.05f, _cfgDebugMinIntervalSec.Value) : 0.5f;
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
