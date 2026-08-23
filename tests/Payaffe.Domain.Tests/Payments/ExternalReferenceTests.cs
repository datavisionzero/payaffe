using Payaffe.Domain.Payments;

namespace Payaffe.Domain.Tests.Payments;

public sealed class ExternalReferenceTests
{
    [Fact]
    public void Create_requires_value()
    {
        var exception = Assert.Throws<DomainRuleException>(() => ExternalReference.Create(" "));

        Assert.Equal("external_reference.required", exception.Code);
    }

    [Fact]
    public void Create_limits_value_length()
    {
        var value = new string('x', ExternalReference.MaxLength + 1);

        var exception = Assert.Throws<DomainRuleException>(() => ExternalReference.Create(value));

        Assert.Equal("external_reference.too_long", exception.Code);
    }

    [Fact]
    public void Create_trims_value()
    {
        var reference = ExternalReference.Create(" order-123 ");

        Assert.Equal("order-123", reference.Value);
    }
}
