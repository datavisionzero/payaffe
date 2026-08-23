using Payaffe.Domain.Payments;

namespace Payaffe.Domain.Tests.Payments;

public sealed class PaymentContextFieldsTests
{
    [Fact]
    public void Create_normalizes_blank_values_to_null()
    {
        var fields = PaymentContextFields.Create(username: " ", customerNumber: "\t");

        Assert.Null(fields.Username);
        Assert.Null(fields.CustomerNumber);
    }

    [Fact]
    public void Create_trims_values()
    {
        var fields = PaymentContextFields.Create(
            username: " customer@example.test ",
            customerNumber: " C-1000 ",
            cartName: " Starter package ",
            note: " Optional note ");

        Assert.Equal("customer@example.test", fields.Username);
        Assert.Equal("C-1000", fields.CustomerNumber);
        Assert.Equal("Starter package", fields.CartName);
        Assert.Equal("Optional note", fields.Note);
    }

    [Theory]
    [InlineData("username", "payment_context.username.too_long")]
    [InlineData("customerNumber", "payment_context.customer_number.too_long")]
    [InlineData("cartName", "payment_context.cart_name.too_long")]
    [InlineData("note", "payment_context.note.too_long")]
    public void Create_limits_each_field_to_255_characters(string fieldName, string expectedCode)
    {
        var value = new string('x', PaymentContextFields.MaxLength + 1);

        var exception = Assert.Throws<DomainRuleException>(() => fieldName switch
        {
            "username" => PaymentContextFields.Create(username: value),
            "customerNumber" => PaymentContextFields.Create(customerNumber: value),
            "cartName" => PaymentContextFields.Create(cartName: value),
            "note" => PaymentContextFields.Create(note: value),
            _ => throw new InvalidOperationException("Unknown field name."),
        });

        Assert.Equal(expectedCode, exception.Code);
    }
}
