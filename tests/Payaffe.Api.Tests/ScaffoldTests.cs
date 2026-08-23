namespace Payaffe.Api.Tests;

public sealed class ScaffoldTests
{
    [Fact]
    public void ReferencesApiAssembly()
    {
        Assert.Equal("Payaffe.Api", typeof(Program).Assembly.GetName().Name);
    }
}
