using Payaffe.Infrastructure;

namespace Payaffe.Integration.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void ReferencesInfrastructureAssembly()
    {
        Assert.Equal("Payaffe.Infrastructure", typeof(InfrastructureAssembly).Assembly.GetName().Name);
    }
}
