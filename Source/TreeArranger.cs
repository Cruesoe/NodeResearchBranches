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

            var result = BranchLayout.Compute(items, links, new LayoutOptions { MaxFanRows = BranchesMod.Settings.maxFanRows, EraAspect = BranchesMod.Settings.eraAspect });

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

            if (node.isPhantom) item.Key = "~era_" + (int)node.phantomEra;
            else if (node.isGroupNode) item.Key = "~group_" + node.groupNodeDef?.defName;
            else
            {
                item.Key = node.def.defName;
                item.Cost = node.def.baseCost;
            }

            var size = CardRenderer.Size(node);
            item.Width = size.x;
            item.Top = size.y / 2f;
            item.Bottom = size.y / 2f;
            return item;
        }

        private static int SizeHash(List<ResearchNode> nodes)
        {
            unchecked
            {
                int hash = (nodes.Count * 31 + BranchesMod.Settings.maxFanRows) * 31 + Mathf.RoundToInt(BranchesMod.Settings.eraAspect * 10f);
                foreach (var node in nodes)
                {
                    hash = hash * 31 + (int)node.state;
                    if (node.state == NodeState.Hidden) continue;
                    var size = CardRenderer.Size(node);
                    hash = hash * 31 + Mathf.RoundToInt(size.x * 4f) * 7919 + Mathf.RoundToInt(size.y * 4f);
                }
                return hash;
            }
        }
    }
}
