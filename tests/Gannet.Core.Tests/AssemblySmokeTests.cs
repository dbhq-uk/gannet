namespace Gannet.Core.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Core_assembly_is_named_Gannet_Core()
    {
        var assembly = typeof(Gannet.Core.GannetInfo).Assembly;
        Assert.Equal("Gannet.Core", assembly.GetName().Name);
    }
}
