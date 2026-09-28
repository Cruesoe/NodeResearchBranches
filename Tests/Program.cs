using System;
using System.Collections.Generic;
using System.Linq;
using NodeResearchBranches.Layout;

namespace NodeResearchBranches.Tests
{
    internal static class Program
    {
        private static int failures;

        private static int Main()
        {
            Run(nameof(ChildrenSitRightOfPrerequisites), ChildrenSitRightOfPrerequisites);
            Run(nameof(NoBubblesOverlap), NoBubblesOverlap);
            Run(nameof(ParentCentredOnChildren), ParentCentredOnChildren);
            Run(nameof(PhantomsLeftEmergenceRight), PhantomsLeftEmergenceRight);
            Run(nameof(Deterministic), Deterministic);
            Run(nameof(CyclesDoNotBreakLayout), CyclesDoNotBreakLayout);
            Run(nameof(IsolatedBelowTree), IsolatedBelowTree);
            Run(nameof(RandomGraphsStayValid), RandomGraphsStayValid);
            Run(nameof(ShrinkingABubbleShrinksItsBranch), ShrinkingABubbleShrinksItsBranch);
            Console.WriteLine(failures == 0 ? "All tests passed." : $"{failures} test(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static LayoutItem Item(string key, float cost = 100f, float size = 40f, bool expanded = false) => new LayoutItem
        {
            Key = key,
            Cost = cost,
            Width = expanded ? 200f : size,
            Top = size / 2f,
            Bottom = size / 2f + (expanded ? 62f : 0f),
        };

        // Fire -> Cooking -> {Pemmican, Brewing}; Fire -> Torches; Smithing -> {Longblades -> Plate, Tools}; Cooking + Smithing -> Pots
        private static (List<LayoutItem>, List<(int, int)>) Sample()
        {
            var names = new[] { "Fire", "Cooking", "Pemmican", "Brewing", "Torches", "Smithing", "Longblades", "Plate", "Tools", "Pots" };
            var items = names.Select((n, i) => Item(n, 100f + i, i % 3 == 0 ? 80f : 40f, i % 2 == 0)).ToList();
            items[0].IsFoundation = true;
            items[5].IsFoundation = true;
            var edges = new List<(int, int)> { (0, 1), (1, 2), (1, 3), (0, 4), (5, 6), (6, 7), (5, 8), (1, 9), (5, 9) };
            return (items, edges);
        }

        private static void AssertValid(IList<LayoutItem> items, IList<(int from, int to)> edges, LayoutResult r)
        {
            var order = TopoRank(items.Count, edges);
            foreach (var (from, to) in edges)
            {
                if (items[to].IsPhantom || order[from] >= order[to]) continue;
                Check(r.X[to] > r.X[from], $"{items[to].Key} is not right of {items[from].Key}");
            }
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    bool overlapX = Math.Abs(r.X[i] - r.X[j]) < (items[i].Width + items[j].Width) / 2f - 0.01f;
                    float iTop = r.Y[i] - items[i].Top, iBottom = r.Y[i] + items[i].Bottom;
                    float jTop = r.Y[j] - items[j].Top, jBottom = r.Y[j] + items[j].Bottom;
                    bool overlapY = iTop < jBottom - 0.01f && jTop < iBottom - 0.01f;
                    Check(!(overlapX && overlapY), $"{items[i].Key} overlaps {items[j].Key}");
                }
        }

        // Order used to tell forward edges from cycle-closing ones in tests: plain Kahn, ties by index.
        private static int[] TopoRank(int n, IList<(int from, int to)> edges)
        {
            var indeg = new int[n];
            foreach (var (_, to) in edges) indeg[to]++;
            var rank = Enumerable.Repeat(int.MaxValue, n).ToArray();
            var queue = new Queue<int>(Enumerable.Range(0, n).Where(i => indeg[i] == 0));
            int next = 0;
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                rank[v] = next++;
                foreach (var (from, to) in edges) if (from == v && --indeg[to] == 0) queue.Enqueue(to);
            }
            return rank;
        }

        private static void ChildrenSitRightOfPrerequisites()
        {
            var (items, edges) = Sample();
            var r = BranchLayout.Compute(items, edges);
            AssertValid(items, edges, r);
            Check(r.Column[9] == 2, "Pots should be in the column after Cooking");
        }

        private static void NoBubblesOverlap()
        {
            var (items, edges) = Sample();
            AssertValid(items, edges, BranchLayout.Compute(items, edges));
        }

        private static void ParentCentredOnChildren()
        {
            var (items, edges) = Sample();
            var r = BranchLayout.Compute(items, edges);
            for (int p = 0; p < items.Count; p++)
            {
                var kids = Enumerable.Range(0, items.Count).Where(c => r.PrimaryParent[c] == p).ToList();
                if (kids.Count == 0) continue;
                float mid = (kids.Min(c => r.Y[c]) + kids.Max(c => r.Y[c])) / 2f;
                Check(Math.Abs(r.Y[p] - mid) < 0.01f, $"{items[p].Key} is not centred on its branches");
            }
        }

        private static void PhantomsLeftEmergenceRight()
        {
            var items = new List<LayoutItem>
            {
                new LayoutItem { Key = "~era_3", IsPhantom = true, PhantomOrder = 3, Width = 150f, Top = 20f, Bottom = 44f },
                Item("A"), Item("B"), Item("C"),
                new LayoutItem { Key = "Emergence", IsEmergence = true, Width = 208f, Top = 114f, Bottom = 176f },
            };
            var edges = new List<(int, int)> { (0, 1), (1, 2), (2, 3), (1, 4), (3, 4) };
            var r = BranchLayout.Compute(items, edges);
            Check(r.Column[0] == -1, "era bubble should be in column -1");
            Check(r.Column[1] == 0, "A should start at column 0");
            Check(r.Column[4] == r.Column.Max() && r.Column[4] > r.Column[3], "emergence should be in its own last column");
            AssertValid(items, edges, r);
        }

        private static void Deterministic()
        {
            var (items, edges) = Sample();
            var a = BranchLayout.Compute(items, edges);
            var b = BranchLayout.Compute(items, edges);
            Check(a.X.SequenceEqual(b.X) && a.Y.SequenceEqual(b.Y), "layout differs between runs");
        }

        private static void CyclesDoNotBreakLayout()
        {
            var items = new List<LayoutItem> { Item("A"), Item("B"), Item("C"), Item("D") };
            var edges = new List<(int, int)> { (0, 1), (1, 2), (2, 1), (2, 3) };
            var r = BranchLayout.Compute(items, edges);
            Check(r.X.All(v => !float.IsNaN(v)) && r.Y.All(v => !float.IsNaN(v)), "NaN position");
            AssertValid(items, edges.Where(e => e != (2, 1)).ToList(), r);
        }

        private static void IsolatedBelowTree()
        {
            var (items, edges) = Sample();
            items.Add(Item("Loner1"));
            items.Add(Item("Loner2"));
            var r = BranchLayout.Compute(items, edges);
            float treeBottom = Enumerable.Range(0, 10).Max(i => r.Y[i] + items[i].Bottom);
            Check(r.Y[10] - items[10].Top > treeBottom && r.Y[11] - items[11].Top > treeBottom, "isolated projects should sit below the tree");
            AssertValid(items, edges, r);
        }

        private static void RandomGraphsStayValid()
        {
            var rng = new Random(1234);
            for (int trial = 0; trial < 60; trial++)
            {
                int n = rng.Next(5, 120);
                var items = Enumerable.Range(0, n).Select(i => Item("P" + i, rng.Next(50, 3000), rng.Next(3) switch { 0 => 20f, 1 => 40f, _ => 80f }, rng.Next(3) == 0)).ToList();
                for (int i = 0; i < n; i++) items[i].IsFoundation = rng.Next(10) == 0;
                var edges = new List<(int, int)>();
                for (int c = 1; c < n; c++)
                {
                    int parents = rng.Next(0, 4);
                    for (int k = 0; k < parents; k++) edges.Add((rng.Next(0, c), c));
                }
                var r = BranchLayout.Compute(items, edges);
                AssertValid(items, edges.Distinct().ToList(), r);
                Check(r.Crossings <= r.InitialCrossings, "sibling sorting made crossings worse");
            }
        }

        private static void ShrinkingABubbleShrinksItsBranch()
        {
            var (items, edges) = Sample();
            float Height(LayoutResult r) => Enumerable.Range(0, items.Count).Max(i => r.Y[i] + items[i].Bottom) - Enumerable.Range(0, items.Count).Min(i => r.Y[i] - items[i].Top);
            float before = Height(BranchLayout.Compute(items, edges));
            foreach (var it in items) { it.Width = 20f; it.Top = 10f; it.Bottom = 10f; }
            float after = Height(BranchLayout.Compute(items, edges));
            Check(after < before, "collapsed bubbles should take less room");
        }
    }
}
