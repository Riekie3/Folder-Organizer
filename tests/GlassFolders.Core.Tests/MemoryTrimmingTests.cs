namespace GlassFolders.Core.Tests;

public sealed class MemoryTrimmingTests
{
    [Fact]
    public void Aggressive_GC_Collect_with_blocking_and_compacting_succeeds_without_exception()
    {
        // In .NET Core / .NET 10, GCCollectionMode.Aggressive requires blocking: true.
        // Passing blocking: false throws ArgumentException: "AggressiveGC requires setting the blocking parameter to true."
        var exception = Record.Exception(() =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        });

        Assert.Null(exception);
    }
}
