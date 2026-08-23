namespace Payaffe.Domain.Payments;

public sealed class DomainRuleException : ArgumentException
{
    public DomainRuleException(string message, string code)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
