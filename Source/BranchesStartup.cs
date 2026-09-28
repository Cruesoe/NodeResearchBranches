using System.Collections.Generic;
using System.Reflection;
using BetterResearchMenu;
using HarmonyLib;
using NodeResearchBranches.Patches;
using RimWorld;
using UnityEngine;
using Verse;

namespace NodeResearchBranches
{
    [StaticConstructorOnStartup]
    public static class BranchesStartup
    {
        public const string HarmonyId = "cruesoe.noderesearch.branches";
        private static readonly System.Type Window = typeof(MainTabWindow_BetterResearch);

        public static AccessTools.FieldRef<MainTabWindow_BetterResearch, List<ResearchNode>> Nodes = null!;
        public static AccessTools.FieldRef<MainTabWindow_BetterResearch, List<ResearchEdge>> Edges = null!;
        public static AccessTools.FieldRef<MainTabWindow_BetterResearch, float> PhysicsTemperature = null!;
        public static AccessTools.FieldRef<MainTabWindow_BetterResearch, ResearchNode> SelectedNode = null!;
        public static AccessTools.FieldRef<Vector2> CameraOffset = null!;
        public static AccessTools.FieldRef<Dictionary<string, Vector2>> CachedCameraOffsets = null!;
        public static AccessTools.FieldRef<TechLevel> CurrentEra = null!;
        public static MethodInfo ComputeCentroidOffset = null!;
        public static MethodInfo CurTabGetter = null!;

        static BranchesStartup()
        {
            var missing = new List<string>();
            MethodInfo Method(string name)
            {
                var m = AccessTools.DeclaredMethod(Window, name);
                if (m == null) missing.Add(name + "()");
                return m!;
            }
            FieldInfo Field(string name)
            {
                var f = AccessTools.DeclaredField(Window, name);
                if (f == null) missing.Add(name);
                return f!;
            }

            var seed = Method("SeedHierarchicalPositions");
            var relax = Method("InitPhysicsLayout");
            var tick = Method("PhysicsTick");
            var init = Method("InitPhysics");
            var update = Method("WindowUpdate");
            var contents = Method("DoWindowContents");
            var controls = Method("DrawGraphControls");
            ComputeCentroidOffset = Method("ComputeCentroidOffset");
            CurTabGetter = AccessTools.PropertyGetter(typeof(MainTabWindow_Research), "CurTab");
            if (CurTabGetter == null) missing.Add("MainTabWindow_Research.CurTab");

            var nodes = Field("nodes");
            var edges = Field("edges");
            var temperature = Field("physicsTemperature");
            var camera = Field("cameraOffset");
            var cameraCache = Field("cachedCameraOffsets");
            var era = Field("currentEra");
            var selected = Field("selectedNode");

            if (missing.Count > 0)
            {
                Log.Error("[Node Research: Branches] This Node Research version is not supported, so the tree is left as Node Research draws it. Missing: " + string.Join(", ", missing));
                return;
            }

            Nodes = AccessTools.FieldRefAccess<MainTabWindow_BetterResearch, List<ResearchNode>>(nodes);
            Edges = AccessTools.FieldRefAccess<MainTabWindow_BetterResearch, List<ResearchEdge>>(edges);
            PhysicsTemperature = AccessTools.FieldRefAccess<MainTabWindow_BetterResearch, float>(temperature);
            SelectedNode = AccessTools.FieldRefAccess<MainTabWindow_BetterResearch, ResearchNode>(selected);
            CameraOffset = AccessTools.StaticFieldRefAccess<Vector2>(camera);
            CachedCameraOffsets = AccessTools.StaticFieldRefAccess<Dictionary<string, Vector2>>(cameraCache);
            CurrentEra = AccessTools.StaticFieldRefAccess<TechLevel>(era);

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(seed, prefix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.SkipPrefix)));
            harmony.Patch(relax, prefix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.SkipPrefix)));
            harmony.Patch(tick, prefix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.PhysicsTickPrefix)));
            harmony.Patch(init, postfix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.InitPhysicsPostfix)));
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.WindowUpdatePostfix)));
            harmony.Patch(contents,
                prefix: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.DoWindowContentsPrefix)),
                finalizer: new HarmonyMethod(typeof(LayoutPatches), nameof(LayoutPatches.DoWindowContentsFinalizer)));
            harmony.Patch(controls,
                prefix: new HarmonyMethod(typeof(ControlPatches), nameof(ControlPatches.Prefix)),
                postfix: new HarmonyMethod(typeof(ControlPatches), nameof(ControlPatches.Postfix)));
        }
    }
}
