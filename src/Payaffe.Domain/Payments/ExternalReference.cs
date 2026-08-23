namespace Payaffe.Domain.Payments;

public sealed record ExternalReference
{
    public const int MaxLength = 255;

    private ExternalReference(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ExternalReference Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("External Reference is required.", "external_reference.required");
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length > MaxLength)
        {
            throw new DomainRuleException(
                $"External Reference must be at most {MaxLength} characters.",
                "external_reference.too_long");
        }

        return new ExternalReference(trimmedValue);
    }
}
