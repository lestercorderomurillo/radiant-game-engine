using System;
using System.Linq;
using System.Threading.Tasks;
using com.radiant.engine.core;

internal static class ShapeCollectionRegression
{
    public static void Run()
    {
        var batch = new ShapeCollection(1);
        for (int i = 0; i < 1000; i++) batch.Add(new Shape { Type = i });
        Require(batch.Count == 1000 && batch.Items[999].Type == 999, "Main buffer growth");
        batch.Clear();
        batch.InitializeParallel(3, 1);
        // Independent workers may grow only their own slots.
        Parallel.For(0, 3, worker =>
        {
            for (int i = 0; i < 9; i++) batch.DrawParallel(worker, new Shape { Type = worker * 10 + i });
        });
        batch.EnsureParallelCapacity(2);
        Require(batch.GetParallelBuffer(0).Length >= 9 && batch.GetParallelZBuffer(0).Length >= 9, "Growth must preserve shapes and Z storage");
        batch.CollectParallel();
        Require(batch.Count == 27 && batch.Items[26].Type == 28, "Worker collection/order");
        batch.CollectParallel();
        Require(batch.Count == 0, "Collected counts not reset");
        // A three-cycle permutation detects the old inverse-permutation sorting error.
        float[][] depths = { new float[] { 3, 1, 2 }, new float[] { -2, 4, 0 }, new float[] { 8, 6, 5, 7 } };
        for (int worker = 0; worker < depths.Length; worker++)
        {
            var shapes = batch.GetParallelBuffer(worker);
            var z = batch.GetParallelZBuffer(worker);
            for (int i = 0; i < depths[worker].Length; i++) { shapes[i] = new Shape { Type = depths[worker][i] }; z[i] = depths[worker][i]; }
            batch.SetParallelCount(worker, depths[worker].Length);
            batch.SortBufferByZ(worker);
        }
        batch.CollectParallelSorted();
        Require(batch.Items.ToArray().Select(shape => shape.Type).SequenceEqual(new float[] { -2, 0, 1, 2, 3, 4, 5, 6, 7, 8 }), "Z order/data association in worker sort and heap merge");
        batch.CollectParallelSorted();
        Require(batch.Count == 0, "Sorted counts not reset");
        Reject(() => batch.InitializeParallel(0));
        Reject(() => batch.SetParallelCount(0, -1));
        Reject(() => batch.SetParallelCount(0, int.MaxValue));
        Console.WriteLine("PASS: CPU shape growth, independent worker buffers, Z association, sorted merge and count validation.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); throw new Exception("Invalid count accepted"); } catch (ArgumentOutOfRangeException) { } }
}
