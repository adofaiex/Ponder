using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Ponder
{
    public static class PatchManager
    {
        private static Harmony _harmony => Main.Harmony!;

        // Status
        private static readonly Dictionary<Type, bool> _activePatches = new();
        // Optimization: Cache exact patch bindings for each patch class to speed up and isolate unpatching
        private static readonly Dictionary<Type, List<(MethodBase Original, MethodInfo PatchMethod)>> _patchedBindings = new();

        // Instance-based patches (BasePatchMethod subclasses)
        private static readonly List<BasePatchMethod> _methodPatches = new();

        // Patch Declaration
        private class PatchDef
        {
            public Type Type;
            public Func<bool> Condition;
            public Type? Parent;
            public string Name;

            public PatchDef(Type type, Func<bool> condition, Type? parent = null)
            {
                Type = type;
                Condition = condition;
                Parent = parent;
                Name = type.Name;
            }
        }

        private static readonly List<PatchDef> _definitions = new();

        static PatchManager()
        {
            RegisterPatches();
        }

        /// <summary>
        /// 注册一个实例化补丁（BasePatchMethod 子类）
        /// </summary>
        public static void RegisterMethodPatch(BasePatchMethod patch)
        {
            lock (_methodPatches)
            {
                if (!_methodPatches.Contains(patch))
                    _methodPatches.Add(patch);
            }
        }

        /// <summary>
        /// 取消注册实例化补丁
        /// </summary>
        public static void UnregisterMethodPatch(BasePatchMethod patch)
        {
            lock (_methodPatches)
            {
                _methodPatches.Remove(patch);
            }
        }

        /// <summary>
        /// 注册一个包含 HarmonyPatch 嵌套类型的补丁类中的所有嵌套补丁
        /// </summary>
        private static void RegisterNestedPatches(Type parentType, Func<bool> condition, HashSet<Type>? exclude = null)
        {
            foreach (var type in parentType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    if (exclude != null && exclude.Contains(type))
                        continue;
                    _definitions.Add(new PatchDef(type, condition));
                }
            }
        }

        private static void RegisterPatches()
        {
            _definitions.Clear();

            // --- Ponder 总开关 ---
            var masterCond = () => Main.Settings.enablePonder;

            // 演示日志补丁：总开关 + 演示日志子开关
            _definitions.Add(new PatchDef(typeof(PonderPatches.ControllerStartPatch),
                () => masterCond() && Main.Settings.enableDemoLogs));

            // 未来的编辑器补丁放这里，例如：
            // RegisterNestedPatches(typeof(PonderEditorPatches), masterCond);
        }

        /// <summary>
        /// 更新所有patch（仅用于初始化或全量更新）
        /// </summary>
        public static void UpdateAllPatches()
        {
            if (_harmony == null) return;

            foreach (var def in _definitions)
            {
                UpdateSinglePatch(def);
            }

            // 同步实例化补丁的 IL 模式
            BasePatchMethod.SyncILModeFromSettings();
        }

        /// <summary>
        /// 按类型更新单个patch - 用于增量更新
        /// </summary>
        public static void UpdatePatchByType(Type patchType)
        {
            if (_harmony == null) return;

            var def = _definitions.Find(d => d.Type == patchType);
            if (def != null)
            {
                UpdateSinglePatch(def);
            }
        }

        /// <summary>
        /// 更新满足条件的patch - 用于批量增量更新
        /// </summary>
        public static void UpdatePatchesByCondition(Func<Type, bool> predicate)
        {
            if (_harmony == null) return;

            foreach (var def in _definitions)
            {
                if (predicate(def.Type))
                {
                    UpdateSinglePatch(def);
                }
            }
        }

        /// <summary>
        /// 更新单个patch定义
        /// </summary>
        private static void UpdateSinglePatch(PatchDef def)
        {
            bool shouldBeActive = CalculateEffectiveStatus(def);
            bool trackedActive = _activePatches.TryGetValue(def.Type, out bool currentActive) && currentActive;

            if (trackedActive != shouldBeActive)
            {
                Main.Handler.Log($"PatchManager: {def.Name} status {trackedActive} -> {shouldBeActive}");
                if (shouldBeActive) ApplyPatch(def.Type);
                else RemovePatch(def.Type);

                _activePatches[def.Type] = shouldBeActive;
            }
        }

        private static bool CalculateEffectiveStatus(PatchDef def)
        {
            // Condition
            if (!def.Condition()) return false;

            // Check Parent
            if (def.Parent != null)
            {
                _activePatches.TryGetValue(def.Parent, out bool parentActive);
                if (!parentActive) return false;
            }

            return true;
        }

        private static void ApplyPatch(Type type)
        {
            try
            {
                Main.Handler.Log($"PatchManager: applying {type.Name}");
                var processor = _harmony.CreateClassProcessor(type);
                var originals = processor.Patch();

                if (originals != null && originals.Count > 0)
                {
                    var bindings = new List<(MethodBase Original, MethodInfo PatchMethod)>();
                    foreach (var original in originals)
                    {
                        var info = Harmony.GetPatchInfo(original);
                        if (info == null) continue;

                        foreach (var p in info.Prefixes)
                        {
                            if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                                bindings.Add((original, p.PatchMethod));
                        }
                        foreach (var p in info.Postfixes)
                        {
                            if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                                bindings.Add((original, p.PatchMethod));
                        }
                        foreach (var p in info.Transpilers)
                        {
                            if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                                bindings.Add((original, p.PatchMethod));
                        }
                        foreach (var p in info.Finalizers)
                        {
                            if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == type)
                                bindings.Add((original, p.PatchMethod));
                        }
                    }

                    _patchedBindings[type] = bindings;
                    _activePatches[type] = true;
                    Main.Handler.Log($"PatchManager: applied {type.Name} ({bindings.Count} bindings)");
                }
                else
                {
                    Main.Handler.Warning($"PatchManager: {type.Name} patched no methods");
                }
            }
            catch (Exception e)
            {
                Main.Handler.Error($"PatchManager: failed to apply {type.Name}: {e}");
            }
        }

        private static void RemovePatch(Type type)
        {
            try
            {
                Main.Handler.Log($"PatchManager: removing {type.Name}");
                if (_patchedBindings.TryGetValue(type, out var bindings) && bindings.Count > 0)
                {
                    Main.Handler.Log($"PatchManager: using cached bindings ({bindings.Count})");
                    foreach (var (original, patchMethod) in bindings)
                    {
                        _harmony.Unpatch(original, patchMethod);
                    }
                    _patchedBindings.Remove(type);
                }
                else
                {
                    // Fallback to slow method if cache is missing or empty
                    Main.Handler.Log($"PatchManager: using slow fallback for {type.Name}");
                    UnpatchMethod(type);
                    _patchedBindings.Remove(type);
                }

                _activePatches[type] = false;
                Main.Handler.Log($"PatchManager: removed {type.Name}");
            }
            catch (Exception e)
            {
                Main.Handler.Error($"PatchManager: failed to remove {type.Name}: {e}");
            }
        }

        private static void UnpatchMethod(Type patchClass)
        {
            // Slow fallback: search all patched methods in the game
            var allPatchedMethods = _harmony.GetPatchedMethods();
            foreach (var original in allPatchedMethods)
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) continue;

                foreach (var p in info.Prefixes)
                {
                    if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == patchClass)
                        _harmony.Unpatch(original, p.PatchMethod);
                }
                foreach (var p in info.Postfixes)
                {
                    if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == patchClass)
                        _harmony.Unpatch(original, p.PatchMethod);
                }
                foreach (var p in info.Transpilers)
                {
                    if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == patchClass)
                        _harmony.Unpatch(original, p.PatchMethod);
                }
                foreach (var p in info.Finalizers)
                {
                    if (p.owner == _harmony.Id && p.PatchMethod.DeclaringType == patchClass)
                        _harmony.Unpatch(original, p.PatchMethod);
                }
            }
        }

        public static void UnpatchAll()
        {
            _harmony?.UnpatchSelf();
            _activePatches.Clear();
            _patchedBindings.Clear();

            // 停止所有实例化补丁
            lock (_methodPatches)
            {
                foreach (var mp in _methodPatches)
                {
                    if (mp.IsPatched)
                        mp.StopPatch();
                }
            }

            Main.Handler.Log("PatchManager: all patches unpatched");
        }
    }
}
