using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BetterResearchMenu;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NodeResearchBranches.Patches
{
    /// <summary>Swaps Node Research's bubbles for cards: its node drawing is blanked out and hit tests use the card's rectangle.</summary>
    public static class CardPatches
    {
        // Blanks the drawing calls in DoWindowContents' node loop, which runs between the last line drawn and DrawGraphControls.
        public static IEnumerable<CodeInstruction> RemoveBubbleDrawing(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var window = typeof(MainTabWindow_BetterResearch);
            var drawLine = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawLine), new[] { typeof(Vector2), typeof(Vector2), typeof(Color), typeof(float) });
            var controls = AccessTools.Method(window, "DrawGraphControls");
            var replacements = new Dictionary<MethodInfo, MethodInfo>
            {
                [AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture), new[] { typeof(Rect), typeof(Texture) })] = AccessTools.Method(typeof(CardPatches), nameof(NoTexture)),
                [AccessTools.Method(typeof(Widgets), nameof(Widgets.Label), new[] { typeof(Rect), typeof(string) })] = AccessTools.Method(typeof(CardPatches), nameof(NoLabel)),
                [AccessTools.Method(window, "DrawBubble")] = AccessTools.Method(typeof(CardPatches), nameof(NoBubble)),
                [AccessTools.Method(window, "DrawScaledLabel")] = AccessTools.Method(typeof(CardPatches), nameof(NoScaledLabel)),
                [AccessTools.Method(window, "DrawScaledResearchLabels")] = AccessTools.Method(typeof(CardPatches), nameof(NoResearchLabels)),
            };

            int start = list.FindLastIndex(ci => ci.Calls(drawLine));
            int end = list.FindIndex(ci => ci.Calls(controls));
            int replaced = 0;
            if (start >= 0 && end > start)
            {
                for (int i = start + 1; i < end; i++)
                {
                    if (list[i].operand is MethodInfo method && replacements.TryGetValue(method, out var stub))
                    {
                        list[i].opcode = OpCodes.Call;
                        list[i].operand = stub;
                        replaced++;
                    }
                }
            }
            if (replaced == 0)
                Log.Error("[Node Research: Branches] Could not find Node Research's bubble drawing, so bubbles will show under the cards.");
            return list;
        }

        private static void NoTexture(Rect rect, Texture texture) { }

        private static void NoLabel(Rect rect, string label) { }

        private static void NoBubble(MainTabWindow_BetterResearch window, Rect rect, ResearchProjectDef proj, float iconPadding, List<ResearchProjectDef> activeProjs, bool drawSilhouette, Color? bubbleTint, bool square) { }

        private static void NoScaledLabel(MainTabWindow_BetterResearch window, Rect rect, string text, float zoom, float baseFontSize) { }

        private static void NoResearchLabels(MainTabWindow_BetterResearch window, ResearchNode node, Vector2 screenPos, float nodeSize, float zoom) { }

        // Node Research reads a node's shape just before hit-testing it, which tells us which node is being tested.
        public static void ShapePostfix(ResearchNode __instance) => CardRenderer.Current = __instance;

        public static bool HitTestPrefix(Vector2 screenPos, Vector2 mousePos, ref bool __result)
        {
            var node = CardRenderer.Current;
            if (node == null) return true;
            __result = CardRenderer.HitTest(node, screenPos, mousePos);
            return false;
        }
    }
}
