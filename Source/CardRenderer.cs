using System;
using System.Collections.Generic;
using BetterResearchMenu;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NodeResearchBranches
{
    /// <summary>Draws research projects as cards with the name inside, in place of Node Research's bubbles.</summary>
    [StaticConstructorOnStartup]
    public static class CardRenderer
    {
        // Card sizes in world units, before zoom.
        public const float FullWidth = 200f;
        public const float FullHeight = 56f;
        public const float CompactWidth = 150f;
        public const float CompactHeight = 26f;
        public const float EmergenceScale = 1.15f;

        private static readonly Color Background = new Color(0.13f, 0.15f, 0.18f);
        private static readonly Color BackgroundFinished = new Color(0.15f, 0.24f, 0.15f);
        private static readonly Color BackgroundActive = new Color(0.27f, 0.21f, 0.10f);
        private static readonly Color BackgroundEra = new Color(0.14f, 0.17f, 0.26f);
        private static readonly Color Border = new Color(0.33f, 0.36f, 0.40f);
        private static readonly Color BorderAvailable = new Color(0.72f, 0.75f, 0.80f);
        private static readonly Color BorderFinished = new Color(0.38f, 0.62f, 0.32f);
        private static readonly Color BorderActive = new Color(0.95f, 0.65f, 0.20f);
        private static readonly Color Foundation = new Color(0.86f, 0.70f, 0.30f);
        private static readonly Color Progress = new ColorInt(125, 183, 96).ToColor;
        private static readonly Color TextNormal = new Color(0.92f, 0.92f, 0.92f);
        private static readonly Color TextLocked = new Color(0.58f, 0.60f, 0.63f);
        private static readonly Color TextCost = new Color(0.68f, 0.70f, 0.73f);

        private static readonly Texture2D LockTex = ContentFinder<Texture2D>.Get("UI/Lock", false);
        private static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<ResearchProjectDef, MainTabWindow_BetterResearch.CachedProjInfo?> iconInfo = new Dictionary<ResearchProjectDef, MainTabWindow_BetterResearch.CachedProjInfo?>();
        private static Func<ResearchProjectDef, MainTabWindow_BetterResearch.CachedProjInfo>? getProjInfo;

        /// <summary>The node whose hit test Node Research is about to run; set when it reads the node's shape.</summary>
        public static ResearchNode? Current;

        /// <summary>The window's zoom, captured at the start of each GUI event.</summary>
        public static float Zoom = 1f;

        public static bool IsCompact(ResearchNode node) => node.isPhantom || node.isGroupNode || node.state != NodeState.Expanded;

        public static Vector2 Size(ResearchNode node)
        {
            var size = IsCompact(node) ? new Vector2(CompactWidth, CompactHeight) : new Vector2(FullWidth, FullHeight);
            if (node.isEmergence) size *= EmergenceScale;
            return size * node.customScale;
        }

        public static bool HitTest(ResearchNode node, Vector2 screenPos, Vector2 mousePos)
        {
            var half = Size(node) * Zoom / 2f;
            return Mathf.Abs(mousePos.x - screenPos.x) < half.x && Mathf.Abs(mousePos.y - screenPos.y) < half.y;
        }

        public static void DrawAll(MainTabWindow_BetterResearch window)
        {
            float zoom = BranchesStartup.ZoomField(window);
            var graph = BranchesStartup.GraphRect(window);
            float bottomBar = (float)BranchesStartup.BottomBarHeight.Invoke(window, null);
            var pivot = new Vector2(graph.width / 2f, (graph.height + bottomBar - 40f) / 2f);
            var camera = BranchesStartup.CameraOffset();
            var selected = BranchesStartup.SelectedNode(window);
            var view = new Rect(0f, 0f, graph.width, graph.height);

            var oldColor = GUI.color;
            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            try
            {
                foreach (var node in BranchesStartup.Nodes(window))
                {
                    if (node.state == NodeState.Hidden || !node.matchesSearchCache) continue;
                    var centre = (node.drawPos + camera) * zoom + pivot;
                    var size = Size(node) * zoom;
                    var rect = new Rect(centre.x - size.x / 2f, centre.y - size.y / 2f, size.x, size.y);
                    if (!rect.Overlaps(view)) continue;
                    Draw(node, rect, zoom, node == selected);
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private static void Draw(ResearchNode node, Rect rect, float zoom, bool selected)
        {
            if (node.isPhantom)
            {
                DrawFrame(rect, BackgroundEra, Border, zoom, selected, false);
                MainTabWindow_BetterResearch.TechLevelIcons.TryGetValue(node.phantomEra, out var eraIcon);
                DrawLine(rect, zoom, eraIcon, null, "← " + node.phantomEra.ToStringHuman().CapitalizeFirst(), TextNormal);
                return;
            }
            if (node.isGroupNode)
            {
                DrawFrame(rect, Background, Border, zoom, selected, false);
                DrawLine(rect, zoom, node.groupNodeDef?.GetTexture(), null, node.groupNodeDef?.LabelCap ?? "", TextNormal);
                return;
            }

            var def = node.def;
            bool finished = node.isFinishedCache;
            bool active = !finished && Find.ResearchManager.IsCurrentProject(def);
            bool locked = node.isLockedCache;
            var background = finished ? BackgroundFinished : active ? BackgroundActive : Background;
            var border = finished ? BorderFinished : active ? BorderActive : locked ? Border : BorderAvailable;
            DrawFrame(rect, background, border, zoom, selected, node.isFoundation);

            if (IsCompact(node))
            {
                bool mystery = !(State.openedNodes?.Contains(def.defName) ?? false) && !finished;
                if (mystery) DrawLine(rect, zoom, null, null, "?", TextLocked, centred: true);
                else DrawLine(rect, zoom, null, def, (finished ? "✓ " : "") + def.LabelCap, locked ? TextLocked : TextNormal);
            }
            else
            {
                DrawFull(node, rect, zoom, locked);
            }

            if (!finished && def.ProgressPercent > 0f)
            {
                float h = Mathf.Max(2f, 3f * zoom);
                var bar = new Rect(rect.x + 1f, rect.yMax - h - 1f, (rect.width - 2f) * def.ProgressPercent, h);
                Widgets.DrawBoxSolid(bar, Progress);
            }

            int queued = ResearchQueue.DefNames.IndexOf(def.defName);
            if (queued >= 0) DrawBadge(rect, zoom, (queued + 1).ToString());
            if (locked && !IsCompact(node) && LockTex != null)
            {
                float s = 14f * zoom;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                GUI.DrawTexture(new Rect(rect.xMax - s - 4f * zoom, rect.y + 4f * zoom, s, s), LockTex);
                GUI.color = Color.white;
            }
        }

        private static void DrawFrame(Rect rect, Color background, Color border, float zoom, bool selected, bool foundation)
        {
            if (selected) Widgets.DrawBoxSolid(rect.ExpandedBy(Mathf.Max(2f, 3f * zoom)), Color.white);
            Widgets.DrawBoxSolid(rect, foundation ? Foundation : border);
            float edge = Mathf.Max(1f, (foundation ? 2.5f : 1.5f) * zoom);
            Widgets.DrawBoxSolid(rect.ContractedBy(edge), background);
            if (foundation) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, Mathf.Max(3f, 5f * zoom), rect.height), Foundation);
        }

        private static void DrawFull(ResearchNode node, Rect rect, float zoom, bool locked)
        {
            float pad = 6f * zoom;
            float iconSize = rect.height - pad * 2f;
            var iconRect = new Rect(rect.x + pad + (node.isFoundation ? 3f * zoom : 0f), rect.y + pad, iconSize, iconSize);
            DrawIcon(iconRect, node.def, locked);

            var textRect = new Rect(iconRect.xMax + pad, rect.y + pad * 0.5f, rect.xMax - iconRect.xMax - pad * 2f - (locked ? 14f * zoom : 0f), rect.height - pad);
            var nameRect = new Rect(textRect.x, textRect.y, textRect.width, textRect.height * 0.64f);
            var costRect = new Rect(textRect.x, nameRect.yMax, textRect.width, textRect.height - nameRect.height);
            Label(nameRect, node.def.LabelCap, 13f * zoom, locked ? TextLocked : TextNormal, TextAnchor.LowerLeft);

            string cost = "BRM_Points".Translate(node.def.Cost);
            if (node.isFoundation) cost += " · " + "BRM_Foundation".Translate();
            else if (node.isEmergence) cost += " · " + "BRM_Emergence".Translate();
            Label(costRect, cost, 10.5f * zoom, TextCost, TextAnchor.UpperLeft);
        }

        // One-line card: optional icon, then a label.
        private static void DrawLine(Rect rect, float zoom, Texture? tex, ResearchProjectDef? def, string text, Color color, bool centred = false)
        {
            float pad = 4f * zoom;
            float x = rect.x + pad * 1.5f;
            float iconSize = rect.height - pad * 2f;
            if (!centred && (tex != null || def != null) && iconSize >= 8f)
            {
                var iconRect = new Rect(x, rect.y + pad, iconSize, iconSize);
                if (tex != null) Widgets.DrawTextureFitted(iconRect, tex, 1f);
                else DrawIcon(iconRect, def!, color == TextLocked);
                x = iconRect.xMax + pad;
            }
            var labelRect = new Rect(x, rect.y, rect.xMax - x - pad, rect.height);
            Label(labelRect, text, 11f * zoom, color, centred ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
        }

        private static void DrawBadge(Rect rect, float zoom, string text)
        {
            float s = Mathf.Max(12f, 16f * zoom);
            var badge = new Rect(rect.xMax - s * 0.7f, rect.y - s * 0.3f, s, s);
            Widgets.DrawBoxSolid(badge, BorderActive);
            Label(badge, text, 10f * zoom, Color.black, TextAnchor.MiddleCenter);
        }

        private static void DrawIcon(Rect rect, ResearchProjectDef def, bool dim)
        {
            var info = Info(def);
            if (info == null) return;
            GUI.color = dim ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            try
            {
                if (info.hasEmergence && MainTabWindow_BetterResearch.TechLevelIcons.TryGetValue(info.emergenceLevel, out var eraIcon))
                    Widgets.DrawTextureFitted(rect, eraIcon, 1f);
                else if (info.hasCustomIcon && info.customTex != null)
                    Widgets.DrawTextureFitted(rect, info.customTex, 1f);
                else if (info.iconDef != null)
                    Widgets.DefIcon(rect, info.iconDef, alpha: dim ? 0.55f : 1f);
            }
            finally
            {
                GUI.color = Color.white;
            }
        }

        private static MainTabWindow_BetterResearch.CachedProjInfo? Info(ResearchProjectDef def)
        {
            if (iconInfo.TryGetValue(def, out var info)) return info;
            try
            {
                getProjInfo ??= AccessTools.MethodDelegate<Func<ResearchProjectDef, MainTabWindow_BetterResearch.CachedProjInfo>>(
                    AccessTools.Method(typeof(MainTabWindow_BetterResearch), "GetProjInfo"));
                info = getProjInfo(def);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Node Research: Branches] Could not read the icon for " + def.defName + ": " + ex.Message, def.defName.GetHashCode() ^ 0x4E5242);
                info = null;
            }
            iconInfo[def] = info;
            return info;
        }

        // Labels scale smoothly with zoom by sizing the font directly; below a readable size they are skipped.
        private static void Label(Rect rect, string text, float fontSize, Color color, TextAnchor anchor)
        {
            int size = Mathf.RoundToInt(fontSize);
            if (size < 6 || rect.width < 8f) return;
            if (!styles.TryGetValue(size, out var style))
            {
                style = new GUIStyle(Text.fontStyles[(int)GameFont.Small]) { fontSize = size, wordWrap = true, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
                styles[size] = style;
            }
            style.alignment = anchor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }
    }
}
