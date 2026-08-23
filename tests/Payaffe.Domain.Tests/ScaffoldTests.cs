using Payaffe.Domain;

namespace Payaffe.Domain.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void ReferencesDomainAssembly()
    {
        Assert.Equal("Payaffe.Domain", typeof(DomainAssembly).Assembly.GetName().Name);
    }
}
