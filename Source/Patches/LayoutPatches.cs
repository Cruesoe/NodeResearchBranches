using System.Collections.Generic;
using BetterResearchMenu;
using RimWorld;
using UnityEngine;
using Verse;

namespace NodeResearchBranches.Patches
{
    public static class LayoutPatches
    {
        // Radial seeding and physics relaxation are replaced by the branch layout.
        public static bool SkipPrefix() => false;

        // No physics; the temperature stays warm only while bubbles are still gliding, so Node Research keeps easing them.
        public static bool PhysicsTickPrefix(MainTabWindow_BetterResearch __instance)
        {
            BranchesStartup.PhysicsTemperature(__instance) = TreeArranger.Settled(BranchesStartup.Nodes(__instance)) ? 0f : 1f;
            return false;
        }

        public static void InitPhysicsPostfix(MainTabWindow_BetterResearch __instance, bool instant)
        {
            var nodes = BranchesStartup.Nodes(__instance);
            TreeArranger.Arrange(nodes, BranchesStartup.Edges(__instance), instant);
            if (instant) RecentreCameraIfLost(__instance, nodes);
        }

        public static void WindowUpdatePostfix(MainTabWindow_BetterResearch __instance)
        {
            TreeArranger.Maintain(BranchesStartup.Nodes(__instance), BranchesStartup.Edges(__instance));
        }

        // Bubbles can still be clicked, but not dragged out of the tree.
        public static void DoWindowContentsPrefix(MainTabWindow_BetterResearch __instance)
        {
            if (Event.current.type != EventType.MouseDrag) return;
            foreach (var node in BranchesStartup.Nodes(__instance))
                node.isDragging = false;
        }

        // Node Research checked its saved camera against the old radial positions; check it against the tree instead.
        private static void RecentreCameraIfLost(MainTabWindow_BetterResearch window, List<ResearchNode> nodes)
        {
            bool any = false;
            var bounds = new Rect();
            foreach (var node in nodes)
            {
                if (node.state == NodeState.Hidden) continue;
                var point = new Rect(node.pos.x, node.pos.y, 0f, 0f);
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.y), Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.y)) : point;
                any = true;
            }
            if (!any) return;

            ref var camera = ref BranchesStartup.CameraOffset();
            if (bounds.ExpandedBy(300f).Contains(-camera)) return;

            camera = (Vector2)BranchesStartup.ComputeCentroidOffset.Invoke(window, null);
            var tab = (ResearchTabDef)BranchesStartup.CurTabGetter.Invoke(window, null);
            if (tab != null)
                BranchesStartup.CachedCameraOffsets()[$"{tab.defName}_{BranchesStartup.CurrentEra()}_{MainTabWindow_BetterResearch.GodModeReveal}"] = camera;
        }
    }
}
