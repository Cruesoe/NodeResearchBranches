using System;
using System.Collections.Generic;
using System.Linq;

namespace NodeResearchBranches.Layout
{
    /// <summary>One bubble to place. Extents are measured from the bubble centre.</summary>
    public sealed class LayoutItem
    {
        public string Key = "";
        public float Cost;
        public bool IsFoundation;
        public bool IsEmergence;
        public bool IsPhantom;
        public int PhantomOrder;
        public int Era;
        public float Width = 40f;
        public float Top = 20f;
        public float Bottom = 20f;
    }

    public sealed class LayoutOptions
    {
        public float ColumnGap = 70f;
        public float SiblingGap = 26f;
        public float RootGap = 80f;
        public float IsolatedGap = 30f;
        public int SortPasses = 3;
        public int MaxFanRows = 6;
        public float FanGap = 16f;
        public float EraGap = 220f;
        public float EraAspect = 1f;
    }

    public sealed class LayoutResult
    {
        public float[] X = Array.Empty<float>();
        public float[] Y = Array.Empty<float>();
        public int[] Column = Array.Empty<int>();
        public int[] PrimaryParent = Array.Empty<int>();
        public int InitialCrossings;
        public int Crossings;
    }

    /// <summary>Left-to-right tidy tree: columns by longest path, branches fanned out by subtree contours.</summary>
    public static class BranchLayout
    {
        private sealed class Graph
        {
            public int N;
            public List<int>[] Parents = Array.Empty<List<int>>();
            public List<int>[] Children = Array.Empty<List<int>>();
            public List<(int from, int to)> Edges = new List<(int, int)>();
        }

        // Subtree outline per column, relative to the subtree root's centre.
        private sealed class Contour
        {
            public readonly float[] Top;
            public readonly float[] Bottom;

            public Contour(int columns)
            {
                Top = new float[columns];
                Bottom = new float[columns];
                for (int i = 0; i < columns; i++) { Top[i] = float.PositiveInfinity; Bottom[i] = float.NegativeInfinity; }
            }

            public bool Has(int c) => Top[c] <= Bottom[c];

            public void Include(int c, float top, float bottom)
            {
                if (top < Top[c]) Top[c] = top;
                if (bottom > Bottom[c]) Bottom[c] = bottom;
            }

            public void Merge(Contour other, float offset)
            {
                for (int c = 0; c < Top.Length; c++)
                    if (other.Has(c)) Include(c, other.Top[c] + offset, other.Bottom[c] + offset);
            }

            public void Shift(float offset)
            {
                for (int c = 0; c < Top.Length; c++)
                    if (Has(c)) { Top[c] += offset; Bottom[c] += offset; }
            }

            public float MinTop()
            {
                float v = float.PositiveInfinity;
                for (int c = 0; c < Top.Length; c++) if (Has(c)) v = Math.Min(v, Top[c]);
                return float.IsInfinity(v) ? 0f : v;
            }

            public float MaxBottom()
            {
                float v = float.NegativeInfinity;
                for (int c = 0; c < Bottom.Length; c++) if (Has(c)) v = Math.Max(v, Bottom[c]);
                return float.IsInfinity(v) ? 0f : v;
            }

            // Smallest offset for 'below' that keeps it clear of this contour by 'gap' in every shared column.
            public float Separation(Contour below, float gap)
            {
                float shift = float.NegativeInfinity;
                for (int c = 0; c < Top.Length; c++)
                    if (Has(c) && below.Has(c)) shift = Math.Max(shift, Bottom[c] + gap - below.Top[c]);
                return shift;
            }
        }

        /// <summary>Lays out each era as its own tree, then places the eras left to right in order.</summary>
        public static LayoutResult Compute(IList<LayoutItem> items, IList<(int from, int to)> edges, LayoutOptions? options = null)
        {
            var o = options ?? new LayoutOptions();
            int n = items.Count;
            var real = Enumerable.Range(0, n).Where(i => !items[i].IsPhantom).ToList();
            var eras = real.Select(i => items[i].Era).Distinct().OrderBy(e => e).ToList();
            if (eras.Count <= 1) return ComputeEra(items, edges, o);

            // Era bubbles for earlier eras belong with the first block.
            int EraOf(int i) => items[i].IsPhantom ? eras[0] : items[i].Era;

            var result = new LayoutResult { X = new float[n], Y = new float[n], Column = new int[n], PrimaryParent = new int[n] };
            float cursor = 0f;
            int columnBase = 0;
            foreach (var era in eras)
            {
                var members = Enumerable.Range(0, n).Where(i => EraOf(i) == era).ToList();
                var local = new Dictionary<int, int>();
                for (int k = 0; k < members.Count; k++) local[members[k]] = k;
                var sub = members.Select(i => items[i]).ToList();
                var subEdges = edges.Where(e => local.ContainsKey(e.from) && local.ContainsKey(e.to)).Select(e => (local[e.from], local[e.to])).ToList();
                // Where each project's prerequisites from earlier eras already sit.
                var anchor = new float[sub.Count];
                for (int k = 0; k < sub.Count; k++)
                {
                    var ys = edges.Where(e => e.to == members[k] && !local.ContainsKey(e.from) && EraOf(e.from) < era).Select(e => result.Y[e.from]).ToList();
                    anchor[k] = ys.Count > 0 ? ys.Average() : float.NaN;
                }
                var r = ComputeEra(sub, subEdges, o, anchor);

                // Slide the block vertically to sit level with those prerequisites.
                var offsets = Enumerable.Range(0, sub.Count).Where(k => !float.IsNaN(anchor[k])).Select(k => anchor[k] - r.Y[k]).ToList();
                float dy = offsets.Count > 0 ? offsets.Average() : 0f;

                float left = float.PositiveInfinity, right = float.NegativeInfinity;
                for (int k = 0; k < sub.Count; k++)
                {
                    left = Math.Min(left, r.X[k] - sub[k].Width / 2f);
                    right = Math.Max(right, r.X[k] + sub[k].Width / 2f);
                }
                float shift = cursor - left;
                int minCol = r.Column.Min();
                for (int k = 0; k < sub.Count; k++)
                {
                    int i = members[k];
                    result.X[i] = r.X[k] + shift;
                    result.Y[i] = r.Y[k] + dy;
                    result.Column[i] = r.Column[k] - minCol + columnBase;
                    result.PrimaryParent[i] = r.PrimaryParent[k] >= 0 ? members[r.PrimaryParent[k]] : -1;
                }
                cursor += right - left + o.EraGap;
                columnBase += r.Column.Max() - minCol + 1;
                result.InitialCrossings += r.InitialCrossings;
                result.Crossings += r.Crossings;
            }

            float centre = (cursor - o.EraGap) / 2f;
            float top = Enumerable.Range(0, n).Min(i => result.Y[i] - items[i].Top);
            float bottom = Enumerable.Range(0, n).Max(i => result.Y[i] + items[i].Bottom);
            for (int i = 0; i < n; i++) { result.X[i] -= centre; result.Y[i] -= (top + bottom) / 2f; }
            return result;
        }

        private static LayoutResult ComputeEra(IList<LayoutItem> items, IList<(int from, int to)> edges, LayoutOptions o, float[]? anchor = null)
        {
            int n = items.Count;
            var result = new LayoutResult { X = new float[n], Y = new float[n], Column = new int[n], PrimaryParent = new int[n] };
            if (n == 0) return result;

            var g = BuildGraph(items, edges);
            var order = TopologicalOrder(g, items);
            var rank = new int[n];
            for (int i = 0; i < n; i++) rank[order[i]] = i;

            // Edges that point backwards in the order close a cycle and are ignored for placement.
            bool Forward(int p, int c) => rank[p] < rank[c];

            var col = AssignColumns(g, items, order, Forward);
            var primary = ChoosePrimaryParents(g, items, order, col, Forward);
            var isolated = new bool[n];
            for (int i = 0; i < n; i++)
                isolated[i] = !items[i].IsPhantom && !items[i].IsEmergence && g.Parents[i].Count == 0 && g.Children[i].Count == 0
                    && (anchor == null || float.IsNaN(anchor[i]));

            var kidList = new List<List<int>>();
            for (int i = 0; i < n; i++) kidList.Add(new List<int>());
            for (int i = 0; i < n; i++) if (primary[i] >= 0) kidList[primary[i]].Add(i);

            var roots = new List<int>();
            for (int i = 0; i < n; i++)
                if (primary[i] < 0 && !isolated[i] && !items[i].IsEmergence) roots.Add(i);

            var size = SubtreeSizes(kidList, roots, n);
            foreach (var k in kidList) k.Sort((a, b) => CompareDefault(items, a, b));
            var rootAnchor = SubtreeAnchors(kidList, roots, anchor);
            roots.Sort((a, b) =>
            {
                if (items[a].IsPhantom != items[b].IsPhantom) return items[a].IsPhantom ? -1 : 1;
                if (items[a].IsPhantom) return items[a].PhantomOrder.CompareTo(items[b].PhantomOrder);
                // Branches fed from earlier eras follow those eras' order; the rest follow, largest first.
                float aa = rootAnchor[a], ab = rootAnchor[b];
                if (float.IsNaN(aa) != float.IsNaN(ab)) return float.IsNaN(aa) ? 1 : -1;
                if (!float.IsNaN(aa) && aa != ab) return aa.CompareTo(ab);
                int s = size[b].CompareTo(size[a]);
                return s != 0 ? s : CompareDefault(items, a, b);
            });

            // Long fans of end-of-branch projects wrap into grid blocks, which lay out as single items.
            var all = new List<LayoutItem>(items);
            var colList = new List<int>(col);
            var blocks = new List<FanBlock>();
            for (int v = 0; v < n; v++)
                kidList[v] = WrapFans(kidList[v], kidList, items, all, colList, blocks, o);
            roots = WrapFans(roots, kidList, items, all, colList, blocks, o);

            int total = all.Count;
            var kids = kidList.ToArray();
            var colAll = colList.ToArray();
            var isMember = new bool[total];
            foreach (var b in blocks) foreach (var m in b.Members) isMember[m] = true;

            int minCol = 0, maxCol = 0;
            for (int i = 0; i < n; i++) { minCol = Math.Min(minCol, col[i]); maxCol = Math.Max(maxCol, col[i]); }
            int colOffset = -minCol;
            int columns = maxCol - minCol + 1;

            var xOfCol = ColumnCentres(all, colAll, isMember, colOffset, columns, o);
            var x = new float[total];
            for (int i = 0; i < total; i++) x[i] = xOfCol[colAll[i] + colOffset];

            float[] Place(List<int>[] k)
            {
                var py = PlaceTree(all, k, roots, colAll, colOffset, columns, o);
                foreach (var b in blocks) b.Expand(x, py);
                return py;
            }

            var y = Place(kids);
            var drawn = g.Edges.Where(e => !isolated[e.from] && !isolated[e.to] && !items[e.from].IsEmergence && !items[e.to].IsEmergence).ToList();
            int best = CountCrossings(drawn, x, y);
            result.InitialCrossings = best;

            // Reorder siblings by where all their prerequisites sit, keeping a pass only if it removes crossings.
            for (int pass = 0; pass < o.SortPasses && best > 0; pass++)
            {
                var trialKids = kids.Select(k => new List<int>(k)).ToArray();
                var bary = new float[total];
                for (int i = 0; i < total; i++)
                {
                    var ps = i < n ? g.Parents[i].Where(p => Forward(p, i)).ToList() : new List<int>();
                    bary[i] = ps.Count > 0 ? ps.Average(p => y[p]) : y[i];
                }
                foreach (var k in trialKids)
                    k.Sort((a, b) => { int c = bary[a].CompareTo(bary[b]); return c != 0 ? c : y[a].CompareTo(y[b]); });

                var trialY = Place(trialKids);
                int crossings = CountCrossings(drawn, x, trialY);
                if (crossings >= best) break;
                best = crossings;
                kids = trialKids;
                y = trialY;
            }
            result.Crossings = best;
            Array.Resize(ref x, n);
            Array.Resize(ref y, n);

            var rootBlockOf = new Dictionary<int, int>();
            foreach (var b in blocks)
                if (roots.Contains(b.Index)) foreach (var m in b.Members) rootBlockOf[m] = b.Index;
            Shelve(items, g, primary, roots, rootBlockOf, isolated, x, y, col, o);

            float treeTop = float.PositiveInfinity, treeBottom = float.NegativeInfinity, treeLeft = float.PositiveInfinity, treeRight = float.NegativeInfinity;
            int treeMaxCol = 0;
            for (int i = 0; i < n; i++)
            {
                if (isolated[i] || items[i].IsEmergence) continue;
                treeTop = Math.Min(treeTop, y[i] - items[i].Top);
                treeBottom = Math.Max(treeBottom, y[i] + items[i].Bottom);
                treeLeft = Math.Min(treeLeft, x[i] - items[i].Width / 2f);
                treeRight = Math.Max(treeRight, x[i] + items[i].Width / 2f);
                treeMaxCol = Math.Max(treeMaxCol, col[i]);
            }
            bool hasTree = !float.IsInfinity(treeTop);
            if (!hasTree) { treeTop = 0f; treeBottom = 0f; treeLeft = 0f; treeRight = 0f; }

            for (int i = 0; i < n; i++)
            {
                if (!items[i].IsEmergence) continue;
                y[i] = (treeTop + treeBottom) / 2f;
                if (hasTree)
                {
                    x[i] = treeRight + o.ColumnGap + items[i].Width / 2f;
                    col[i] = treeMaxCol + 1;
                }
            }

            PlaceIsolated(items, isolated, x, y, treeLeft, treeRight, hasTree ? treeBottom + o.RootGap : 0f, o);

            // Centre the whole layout on the origin.
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, x[i] - items[i].Width / 2f);
                maxX = Math.Max(maxX, x[i] + items[i].Width / 2f);
                minY = Math.Min(minY, y[i] - items[i].Top);
                maxY = Math.Max(maxY, y[i] + items[i].Bottom);
            }
            float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
            for (int i = 0; i < n; i++) { x[i] -= cx; y[i] -= cy; }

            result.X = x;
            result.Y = y;
            result.Column = col;
            result.PrimaryParent = primary;
            return result;
        }

        private static Graph BuildGraph(IList<LayoutItem> items, IList<(int from, int to)> edges)
        {
            int n = items.Count;
            var g = new Graph { N = n, Parents = new List<int>[n], Children = new List<int>[n] };
            for (int i = 0; i < n; i++) { g.Parents[i] = new List<int>(); g.Children[i] = new List<int>(); }
            var seen = new HashSet<(int, int)>();
            foreach (var (from, to) in edges)
            {
                if (from < 0 || to < 0 || from >= n || to >= n || from == to) continue;
                if (items[to].IsPhantom) continue;
                if (!seen.Add((from, to))) continue;
                g.Parents[to].Add(from);
                g.Children[from].Add(to);
                g.Edges.Add((from, to));
            }
            return g;
        }

        // Kahn's order; when a cycle blocks progress, the lowest-ranked remaining node is released.
        private static int[] TopologicalOrder(Graph g, IList<LayoutItem> items)
        {
            int n = g.N;
            var indeg = new int[n];
            for (int i = 0; i < n; i++) indeg[i] = g.Parents[i].Count;
            var done = new bool[n];
            var ready = new SortedSet<int>(Comparer<int>.Create((a, b) =>
            {
                int c = CompareDefault(items, a, b);
                return c != 0 ? c : a.CompareTo(b);
            }));
            for (int i = 0; i < n; i++) if (indeg[i] == 0) ready.Add(i);

            var order = new List<int>(n);
            while (order.Count < n)
            {
                if (ready.Count == 0)
                {
                    int pick = -1;
                    for (int i = 0; i < n; i++)
                        if (!done[i] && (pick < 0 || indeg[i] < indeg[pick] || (indeg[i] == indeg[pick] && CompareDefault(items, i, pick) < 0))) pick = i;
                    ready.Add(pick);
                }
                int next = ready.Min;
                ready.Remove(next);
                if (done[next]) continue;
                done[next] = true;
                order.Add(next);
                foreach (var c in g.Children[next])
                    if (!done[c] && --indeg[c] == 0) ready.Add(c);
            }
            return order.ToArray();
        }

        private static int[] AssignColumns(Graph g, IList<LayoutItem> items, int[] order, Func<int, int, bool> forward)
        {
            int n = g.N;
            var col = new int[n];
            int maxCol = 0;
            foreach (var i in order)
            {
                if (items[i].IsPhantom) { col[i] = -1; continue; }
                int c = 0;
                foreach (var p in g.Parents[i])
                    if (forward(p, i) && !items[p].IsEmergence) c = Math.Max(c, col[p] + 1);
                col[i] = c;
                if (!items[i].IsEmergence) maxCol = Math.Max(maxCol, c);
            }
            for (int i = 0; i < n; i++)
                if (items[i].IsEmergence) col[i] = Math.Max(col[i], maxCol + 1);
            return col;
        }

        private static int[] ChoosePrimaryParents(Graph g, IList<LayoutItem> items, int[] order, int[] col, Func<int, int, bool> forward)
        {
            int n = g.N;
            var primary = new int[n];
            var inFoundationBranch = new bool[n];
            for (int i = 0; i < n; i++) primary[i] = -1;
            foreach (var i in order)
            {
                if (!items[i].IsPhantom && !items[i].IsEmergence)
                {
                    int best = -1;
                    foreach (var p in g.Parents[i])
                    {
                        if (!forward(p, i) || items[p].IsEmergence || col[p] != col[i] - 1) continue;
                        if (best < 0 || ComparePrimary(items, inFoundationBranch, p, best) < 0) best = p;
                    }
                    primary[i] = best;
                }
                inFoundationBranch[i] = items[i].IsFoundation || (primary[i] >= 0 && inFoundationBranch[primary[i]]);
            }
            return primary;
        }

        private static int ComparePrimary(IList<LayoutItem> items, bool[] inFoundationBranch, int a, int b)
        {
            if (inFoundationBranch[a] != inFoundationBranch[b]) return inFoundationBranch[a] ? -1 : 1;
            if (items[a].IsPhantom != items[b].IsPhantom) return items[a].IsPhantom ? 1 : -1;
            return CompareDefault(items, a, b);
        }

        private static int CompareDefault(IList<LayoutItem> items, int a, int b)
        {
            int c = items[a].Cost.CompareTo(items[b].Cost);
            return c != 0 ? c : string.CompareOrdinal(items[a].Key, items[b].Key);
        }

        // A grid of end-of-branch projects that lays out as one item; odd sub-columns are staggered so lines pass between bubbles.
        private sealed class FanBlock
        {
            public int Index;
            public List<int> Members = new List<int>();
            public int Rows, Cols;
            public float CellWidth, CellTop, CellBottom, Stagger, RowGap, ColGap, Width, Height;

            public void Expand(float[] x, float[] y)
            {
                float left = x[Index] - Width / 2f;
                float top = y[Index] - Height / 2f;
                for (int k = 0; k < Members.Count; k++)
                {
                    int r = k % Rows, c = k / Rows;
                    x[Members[k]] = left + c * (CellWidth + ColGap) + CellWidth / 2f;
                    y[Members[k]] = top + (c % 2 == 1 ? Stagger : 0f) + r * (CellTop + CellBottom + RowGap) + CellTop;
                }
            }
        }

        private static List<int> WrapFans(List<int> siblings, List<List<int>> kids, IList<LayoutItem> items, List<LayoutItem> all, List<int> col, List<FanBlock> blocks, LayoutOptions o)
        {
            if (o.MaxFanRows <= 0) return siblings;
            var result = new List<int>(siblings);
            foreach (var group in siblings
                .Where(s => s < items.Count && kids[s].Count == 0 && !items[s].IsPhantom && !items[s].IsEmergence)
                .GroupBy(s => col[s]))
            {
                var members = group.ToList();
                if (members.Count <= o.MaxFanRows) continue;

                var block = new FanBlock { Index = all.Count, Members = members, RowGap = o.SiblingGap / 2f, ColGap = o.FanGap };
                block.Cols = (members.Count + o.MaxFanRows - 1) / o.MaxFanRows;
                block.Rows = (members.Count + block.Cols - 1) / block.Cols;
                block.CellWidth = members.Max(m => items[m].Width);
                block.CellTop = members.Max(m => items[m].Top);
                block.CellBottom = members.Max(m => items[m].Bottom);
                float pitch = block.CellTop + block.CellBottom + block.RowGap;
                block.Stagger = block.Cols > 1 ? pitch / 2f : 0f;
                block.Width = block.Cols * block.CellWidth + (block.Cols - 1) * block.ColGap;
                block.Height = block.Rows * pitch - block.RowGap + block.Stagger;

                all.Add(new LayoutItem { Key = items[members[0]].Key, Cost = items[members[0]].Cost, Width = block.Width, Top = block.Height / 2f, Bottom = block.Height / 2f });
                col.Add(group.Key);
                kids.Add(new List<int>());
                blocks.Add(block);

                int at = result.IndexOf(members[0]);
                result.RemoveAll(members.Contains);
                result.Insert(Math.Min(at, result.Count), block.Index);
            }
            return result;
        }

        // Average anchor across each root's branch; NaN when nothing in it has one.
        private static Dictionary<int, float> SubtreeAnchors(IList<List<int>> kids, List<int> roots, float[]? anchor)
        {
            var result = new Dictionary<int, float>();
            foreach (var r in roots)
            {
                float sum = 0f;
                int count = 0;
                var stack = new Stack<int>();
                stack.Push(r);
                while (stack.Count > 0)
                {
                    int v = stack.Pop();
                    if (anchor != null && !float.IsNaN(anchor[v])) { sum += anchor[v]; count++; }
                    foreach (var k in kids[v]) stack.Push(k);
                }
                result[r] = count > 0 ? sum / count : float.NaN;
            }
            return result;
        }

        private static int[] SubtreeSizes(IList<List<int>> kids, List<int> roots, int n)
        {
            var size = new int[n];
            int Visit(int v)
            {
                int s = 1;
                foreach (var k in kids[v]) s += Visit(k);
                return size[v] = s;
            }
            foreach (var r in roots) Visit(r);
            return size;
        }

        private static float[] PlaceTree(IList<LayoutItem> items, List<int>[] kids, List<int> roots, int[] col, int colOffset, int columns, LayoutOptions o)
        {
            int n = items.Count;
            var rel = new float[n];

            Contour Build(int v)
            {
                var contour = new Contour(columns);
                var children = kids[v];
                if (children.Count > 0)
                {
                    var offsets = new float[children.Count];
                    for (int i = 0; i < children.Count; i++)
                    {
                        var sub = Build(children[i]);
                        if (i == 0) offsets[i] = 0f;
                        else
                        {
                            float sep = contour.Separation(sub, o.SiblingGap);
                            offsets[i] = float.IsNegativeInfinity(sep) ? offsets[i - 1] : sep;
                        }
                        contour.Merge(sub, offsets[i]);
                    }
                    float mid = (offsets[0] + offsets[children.Count - 1]) / 2f;
                    for (int i = 0; i < children.Count; i++) rel[children[i]] = offsets[i] - mid;
                    contour.Shift(-mid);
                }
                contour.Include(col[v] + colOffset, -items[v].Top, items[v].Bottom);
                return contour;
            }

            var y = new float[n];
            var forest = new Contour(columns);
            bool first = true;
            float lastOffset = 0f;
            foreach (var r in roots)
            {
                var sub = Build(r);
                float offset;
                if (first) offset = 0f;
                else
                {
                    float sep = forest.Separation(sub, o.RootGap);
                    offset = float.IsNegativeInfinity(sep) ? forest.MaxBottom() + o.RootGap - sub.MinTop() : sep;
                }
                forest.Merge(sub, offset);
                lastOffset = offset;
                first = false;
                y[r] = offset;
                Assign(r);
            }

            void Assign(int v)
            {
                foreach (var k in kids[v])
                {
                    y[k] = y[v] + rel[k];
                    Assign(k);
                }
            }

            return y;
        }

        private static float[] ColumnCentres(IList<LayoutItem> items, int[] col, bool[] skip, int colOffset, int columns, LayoutOptions o)
        {
            var width = new float[columns];
            for (int i = 0; i < items.Count; i++)
                if (!skip[i]) width[col[i] + colOffset] = Math.Max(width[col[i] + colOffset], items[i].Width);

            var centre = new float[columns];
            float cursor = 0f;
            bool any = false;
            for (int c = 0; c < columns; c++)
            {
                if (width[c] <= 0f) { centre[c] = cursor; continue; }
                if (any) cursor += o.ColumnGap;
                centre[c] = cursor + width[c] / 2f;
                cursor += width[c];
                any = true;
            }
            return centre;
        }

        // Unconnected projects fill rows beneath the tree, as wide as the tree itself.
        private sealed class Unit
        {
            public int Id;
            public List<int> Members = new List<int>();
            public HashSet<int> Successors = new HashSet<int>();
            public HashSet<int> Reach = new HashSet<int>();
            public float Left = float.PositiveInfinity, Right = float.NegativeInfinity, Top = float.PositiveInfinity, Bottom = float.NegativeInfinity;
            public int MinCol = int.MaxValue, MaxCol = int.MinValue;
            public bool IsPhantom;
            public float Height => Bottom - Top;
        }

        // Separate trees stacked into one tall pile are wrapped into side-by-side stacks, aiming for the block shape in EraAspect.
        // A tree only moves to a later stack if nothing already placed depends on it, so prerequisites stay to the left.
        private static void Shelve(IList<LayoutItem> items, Graph g, int[] primary, List<int> roots, Dictionary<int, int> rootBlockOf, bool[] isolated, float[] x, float[] y, int[] col, LayoutOptions o)
        {
            if (o.EraAspect <= 0f) return;
            int n = items.Count;
            int UnitOf(int i)
            {
                while (primary[i] >= 0) i = primary[i];
                return rootBlockOf.TryGetValue(i, out int b) ? b : i;
            }

            var units = new Dictionary<int, Unit>();
            var unitOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                unitOf[i] = -1;
                if (isolated[i] || items[i].IsEmergence) continue;
                int id = UnitOf(i);
                if (!units.TryGetValue(id, out var u)) units[id] = u = new Unit { Id = id };
                unitOf[i] = id;
                u.Members.Add(i);
                u.IsPhantom |= items[i].IsPhantom;
                u.Left = Math.Min(u.Left, x[i] - items[i].Width / 2f);
                u.Right = Math.Max(u.Right, x[i] + items[i].Width / 2f);
                u.Top = Math.Min(u.Top, y[i] - items[i].Top);
                u.Bottom = Math.Max(u.Bottom, y[i] + items[i].Bottom);
                u.MinCol = Math.Min(u.MinCol, col[i]);
                u.MaxCol = Math.Max(u.MaxCol, col[i]);
            }
            foreach (var (from, to) in g.Edges)
                if (unitOf[from] >= 0 && unitOf[to] >= 0 && unitOf[from] != unitOf[to])
                    units[unitOf[from]].Successors.Add(unitOf[to]);

            var order = roots.Where(units.ContainsKey).Select(r => units[r]).ToList();
            if (order.Count < 2) return;

            // Follow chains through other trees, so a tree never lands right of something it feeds indirectly.
            foreach (var u in units.Values)
            {
                var reach = new HashSet<int>();
                var stack = new Stack<int>(u.Successors);
                while (stack.Count > 0)
                {
                    int s = stack.Pop();
                    if (s == u.Id || !reach.Add(s)) continue;
                    foreach (var t in units[s].Successors) stack.Push(t);
                }
                u.Reach = reach;
            }

            List<List<Unit>> Pack(float limit)
            {
                var shelves = new List<List<Unit>> { new List<Unit>() };
                var heights = new List<float> { 0f };
                var shelfOf = new Dictionary<int, int>();
                foreach (var u in order)
                {
                    // Anything this tree feeds that is already placed caps how far right it may go.
                    int cap = u.IsPhantom ? 0 : shelves.Count - 1;
                    foreach (var s in u.Reach)
                        if (shelfOf.TryGetValue(s, out int si)) cap = Math.Min(cap, si);

                    int target = cap;
                    if (target == shelves.Count - 1 && !u.IsPhantom && u.Reach.All(s => !shelfOf.ContainsKey(s))
                        && shelves[target].Count > 0 && heights[target] + o.RootGap + u.Height > limit)
                    {
                        shelves.Add(new List<Unit>());
                        heights.Add(0f);
                        target = shelves.Count - 1;
                    }
                    heights[target] += (shelves[target].Count > 0 ? o.RootGap : 0f) + u.Height;
                    shelves[target].Add(u);
                    shelfOf[u.Id] = target;
                }
                return shelves;
            }

            (float width, float height) Measure(List<List<Unit>> shelves)
            {
                float width = 0f, height = 0f;
                foreach (var s in shelves)
                {
                    width += s.Max(u => u.Right) - s.Min(u => u.Left);
                    height = Math.Max(height, s.Sum(u => u.Height) + (s.Count - 1) * o.RootGap);
                }
                return (width + (shelves.Count - 1) * o.ColumnGap * 1.5f, height);
            }

            // Try a range of stack heights and keep the one closest to the target shape.
            float stacked = order.Sum(u => u.Height) + (order.Count - 1) * o.RootGap;
            float tallest = order.Max(u => u.Height);
            List<List<Unit>>? best = null;
            float bestScore = float.PositiveInfinity;
            for (int k = 1; k <= Math.Min(order.Count, 12); k++)
            {
                var shelves = Pack(Math.Max(tallest, stacked / k));
                var (w, h) = Measure(shelves);
                float score = Math.Max(h, w * o.EraAspect);
                if (score < bestScore - 0.5f) { bestScore = score; best = shelves; }
            }
            if (best == null || best.Count < 2) return;

            float cursor = best[0].Min(u => u.Left);
            int colCursor = best[0].Min(u => u.MinCol);
            foreach (var shelf in best)
            {
                float dx = cursor - shelf.Min(u => u.Left);
                int dc = colCursor - shelf.Min(u => u.MinCol);
                float top = 0f;
                foreach (var u in shelf)
                {
                    float dy = top - u.Top;
                    foreach (var m in u.Members) { x[m] += dx; y[m] += dy; col[m] += dc; }
                    top += u.Height + o.RootGap;
                }
                cursor += shelf.Max(u => u.Right) - shelf.Min(u => u.Left) + o.ColumnGap * 1.5f;
                colCursor += shelf.Max(u => u.MaxCol) - shelf.Min(u => u.MinCol) + 1;
            }
        }

        private static void PlaceIsolated(IList<LayoutItem> items, bool[] isolated, float[] x, float[] y, float treeLeft, float treeRight, float rowTop, LayoutOptions o)
        {
            var list = Enumerable.Range(0, items.Count).Where(i => isolated[i]).OrderBy(i => items[i].Cost).ThenBy(i => items[i].Key, StringComparer.Ordinal).ToList();
            if (list.Count == 0) return;

            float left = treeLeft;
            float right = Math.Max(treeRight, left + 4 * (items[list[0]].Width + o.IsolatedGap));

            int start = 0;
            while (start < list.Count)
            {
                float cursor = left;
                int end = start;
                float top = 0f, bottom = 0f;
                while (end < list.Count)
                {
                    var it = items[list[end]];
                    if (end > start && cursor + it.Width > right) break;
                    x[list[end]] = cursor + it.Width / 2f;
                    cursor += it.Width + o.IsolatedGap;
                    top = Math.Max(top, it.Top);
                    bottom = Math.Max(bottom, it.Bottom);
                    end++;
                }
                for (int i = start; i < end; i++) y[list[i]] = rowTop + top;
                rowTop += top + bottom + o.IsolatedGap;
                start = end;
            }
        }

        public static int CountCrossings(IList<(int from, int to)> edges, float[] x, float[] y)
        {
            int count = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                var (a, b) = edges[i];
                for (int j = i + 1; j < edges.Count; j++)
                {
                    var (c, d) = edges[j];
                    if (a == c || a == d || b == c || b == d) continue;
                    if (Intersects(x[a], y[a], x[b], y[b], x[c], y[c], x[d], y[d])) count++;
                }
            }
            return count;
        }

        private static bool Intersects(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
        {
            float d1 = Cross(x3, y3, x4, y4, x1, y1);
            float d2 = Cross(x3, y3, x4, y4, x2, y2);
            float d3 = Cross(x1, y1, x2, y2, x3, y3);
            float d4 = Cross(x1, y1, x2, y2, x4, y4);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static float Cross(float ax, float ay, float bx, float by, float px, float py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }
}
