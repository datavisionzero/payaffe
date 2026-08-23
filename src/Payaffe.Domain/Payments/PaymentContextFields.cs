namespace Payaffe.Domain.Payments;

public sealed record PaymentContextFields
{
    public const int MaxLength = 255;

    private PaymentContextFields(
        string? username,
        string? customerNumber,
        string? cartName,
        string? note)
    {
        Username = username;
        CustomerNumber = customerNumber;
        CartName = cartName;
        Note = note;
    }

    public string? Username { get; }

    public string? CustomerNumber { get; }

    public string? CartName { get; }

    public string? Note { get; }

    public static PaymentContextFields Empty { get; } = new(null, null, null, null);

    public static PaymentContextFields Create(
        string? username = null,
        string? customerNumber = null,
        string? cartName = null,
        string? note = null)
    {
        return new PaymentContextFields(
            Normalize(username, "username"),
            Normalize(customerNumber, "customer_number"),
            Normalize(cartName, "cart_name"),
            Normalize(note, "note"));
    }

    private static string? Normalize(string? value, string fieldName)
    {
        if (value is null)
        {
            return null;
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length == 0)
        {
            return null;
        }

        if (trimmedValue.Length > MaxLength)
        {
            throw new DomainRuleException(
                $"Payment Context Field {fieldName} must be at most {MaxLength} characters.",
                $"payment_context.{fieldName}.too_long");
        }

        return trimmedValue;
    }
}
