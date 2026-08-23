using Payaffe.Application;

namespace Payaffe.Application.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void ReferencesApplicationAssembly()
    {
        Assert.Equal("Payaffe.Application", typeof(ApplicationAssembly).Assembly.GetName().Name);
    }
}
