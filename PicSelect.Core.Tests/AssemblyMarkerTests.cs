using PicSelect.Core;

namespace PicSelect.Core.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void CoreAssemblyCanBeReferencedFromTests()
    {
        Assert.Equal("PicSelect.Core.AssemblyMarker", typeof(AssemblyMarker).FullName);
    }
}
