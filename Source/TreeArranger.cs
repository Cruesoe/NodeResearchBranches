using System.Collections.Generic;
using BetterResearchMenu;
using NodeResearchBranches.Layout;
using RimWorld;
using UnityEngine;

namespace NodeResearchBranches
{
    /// <summary>Feeds Node Research's bubbles through the branch layout and holds each bubble to its place.</summary>
    public static class TreeArranger
    {
        // Node Research's world-unit bubble sizes (MainTabWindow_BetterResearch NodeSizeExpanded/Minimized).
        private const float SizeExpanded = 80f;
        private const float SizeMinimized = 40f;
        private const float LabelWidth = 200f;
        private const float LabelHeight = 62f;
        private const float EraLabelWidth = 150f;
        private const float EraLabelHeight = 24f;
        private const float RingPadding = 10f;

        private static readonly Dictionary<ResearchNode, Vector2> targets = new Dictionary<ResearchNode, Vector2>();
        private static readonly HashSet<ResearchEdge> crossEraEdges = new HashSet<ResearchEdge>();
        private static readonly HashSet<ResearchEdge> extraEdges = new HashSet<ResearchEdge>();
        private static List<ResearchNode>? lastNodes;

        /// <summary>Bumped on every layout, so callers can tell the node and edge lists were rebuilt.</summary>
        public static int Version;
        private static int lastHash;

        public static void Arrange(List<ResearchNode> nodes, List<ResearchEdge> edges, bool instant)
        {
            var visible = new List<ResearchNode>();
            var items = new List<LayoutItem>();
            var index = new Dictionary<ResearchNode, int>();
            foreach (var node in nodes)
            {
                if (node.state == NodeState.Hidden) continue;
                index[node] = visible.Count;
                visible.Add(node);
                items.Add(ToItem(node));
            }

            var links = new List<(int, int)>();
            foreach (var edge in edges)
                if (index.TryGetValue(edge.from, out int from) && index.TryGetValue(edge.to, out int to))
                    links.Add((from, to));

            // A group without its own tech level joins the earliest era it leads into.
            foreach (var (from, to) in links)
            {
                var group = visible[from];
                bool unset = group.isGroupNode && (group.groupNodeDef == null || group.groupNodeDef.techLevel == TechLevel.Undefined);
                if (unset && items[to].Era > 0 && (items[from].Era == 0 || items[to].Era < items[from].Era))
                    items[from].Era = items[to].Era;
            }

            var result = BranchLayout.Compute(items, links, new LayoutOptions { MaxFanRows = BranchesMod.Settings.maxFanRows });

            targets.Clear();
            for (int i = 0; i < visible.Count; i++)
            {
                var node = visible[i];
                var pos = new Vector2(result.X[i], result.Y[i]);
                targets[node] = pos;
                node.pos = pos;
                node.velocity = Vector2.zero;
                if (instant)
                {
                    node.drawPos = pos;
                    node.dampVelocity = Vector2.zero;
                }
            }

            crossEraEdges.Clear();
            extraEdges.Clear();
            foreach (var edge in edges)
            {
                if (!index.TryGetValue(edge.from, out int from) || !index.TryGetValue(edge.to, out int to)) continue;
                if (!edge.from.isPhantom && !edge.to.isPhantom && items[from].Era != items[to].Era) crossEraEdges.Add(edge);
                else if (!edge.isGroupEdge && result.PrimaryParent[to] != from) extraEdges.Add(edge);
            }

            Version++;
            lastNodes = nodes;
            lastHash = SizeHash(nodes);
        }

        /// <summary>Whether a line is left out of the drawing because neither end is selected.</summary>
        public static bool IsHiddenLine(ResearchEdge edge, ResearchNode? selected)
        {
            if (edge.from == selected || edge.to == selected) return false;
            if (BranchesMod.Settings.hideCrossEraLines && crossEraEdges.Contains(edge)) return true;
            return BranchesMod.Settings.hideExtraLines && extraEdges.Contains(edge);
        }

        /// <summary>Re-arranges when bubbles appear, vanish or change size; otherwise pins them in place.</summary>
        public static void Maintain(List<ResearchNode> nodes, List<ResearchEdge> edges)
        {
            if (!ReferenceEquals(nodes, lastNodes) || SizeHash(nodes) != lastHash)
            {
                Arrange(nodes, edges, instant: false);
                return;
            }
            foreach (var kv in targets)
                kv.Key.pos = kv.Value;
        }

        public static bool Settled(List<ResearchNode> nodes)
        {
            foreach (var node in nodes)
                if (node.state != NodeState.Hidden && (node.drawPos - node.pos).sqrMagnitude > 0.25f) return false;
            return true;
        }

        private static LayoutItem ToItem(ResearchNode node)
        {
            var item = new LayoutItem
            {
                IsPhantom = node.isPhantom,
                PhantomOrder = (int)node.phantomEra,
                IsFoundation = node.isFoundation,
                IsEmergence = node.isEmergence,
                Era = (int)(node.isPhantom ? node.phantomEra : node.isGroupNode ? node.groupNodeDef?.techLevel ?? TechLevel.Undefined : node.def.techLevel),
            };

            if (node.isPhantom || node.isGroupNode)
            {
                item.Key = node.isPhantom ? "~era_" + (int)node.phantomEra : "~group_" + node.groupNodeDef?.defName;
                item.Width = EraLabelWidth;
                item.Top = SizeMinimized / 2f;
                item.Bottom = SizeMinimized / 2f + EraLabelHeight;
                return item;
            }

            item.Key = node.def.defName;
            item.Cost = node.def.baseCost;
            float size = BubbleSize(node);
            if (node.state == NodeState.Expanded)
            {
                item.Width = Mathf.Max(size + RingPadding * 2f, LabelWidth);
                item.Top = size / 2f + RingPadding;
                item.Bottom = size / 2f + RingPadding + LabelHeight;
            }
            else
            {
                item.Width = size;
                item.Top = size / 2f;
                item.Bottom = size / 2f;
            }
            return item;
        }

        // Mirrors ResearchNode.GetNodeSize, using the target scale so a growing bubble lays out once, not every frame.
        // Dots are sized as minimized bubbles, since that is how they draw under the cursor.
        private static float BubbleSize(ResearchNode node)
        {
            float scale = node.TargetDynamicScale;
            float size;
            if (node.state == NodeState.Expanded)
                size = node.UsesLargeNodeStyle ? SizeExpanded * 2.6f : SizeExpanded * scale;
            else if (node.UsesLargeNodeStyle)
                size = SizeMinimized * Mathf.Max(2.275f, 2.4375f * node.CollapsedShrink);
            else
                size = SizeMinimized * Mathf.Sqrt(scale) * node.CollapsedShrink;

            if (node.isEmergence && node.state != NodeState.Expanded) size *= 1.5f;
            return size * node.customScale;
        }

        private static int SizeHash(List<ResearchNode> nodes)
        {
            unchecked
            {
                int hash = nodes.Count * 31 + BranchesMod.Settings.maxFanRows;
                foreach (var node in nodes)
                {
                    hash = hash * 31 + (int)node.state;
                    if (node.state == NodeState.Hidden) continue;
                    hash = hash * 31 + Mathf.RoundToInt(BubbleSize(node) * 4f);
                }
                return hash;
            }
        }
    }
}
