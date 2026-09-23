using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payaffe.Sdk;

[JsonConverter(typeof(PaymentStatusJsonConverter))]
public readonly record struct PaymentStatus
{
    public PaymentStatus(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static PaymentStatus PendingCurrencySelection { get; } = new("pending_currency_selection");

    public static PaymentStatus WaitingForPayment { get; } = new("waiting_for_payment");

    public static PaymentStatus Observed { get; } = new("observed");

    public static PaymentStatus Completed { get; } = new("completed");

    public static PaymentStatus Expired { get; } = new("expired");

    public static PaymentStatus Settled { get; } = new("settled");

    public bool IsTerminal => this == Completed || this == Settled || this == Expired;

    public override string ToString() => Value;
}

[JsonConverter(typeof(SupportedCurrencyJsonConverter))]
public readonly record struct SupportedCurrency
{
    public SupportedCurrency(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static SupportedCurrency Btc { get; } = new("BTC");

    public static SupportedCurrency Ltc { get; } = new("LTC");

    public static SupportedCurrency Eth { get; } = new("ETH");

    public override string ToString() => Value;
}

[JsonConverter(typeof(PaymentOptionStatusJsonConverter))]
public readonly record struct PaymentOptionStatus
{
    public PaymentOptionStatus(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static PaymentOptionStatus Available { get; } = new("available");

    public static PaymentOptionStatus Unavailable { get; } = new("unavailable");

    public override string ToString() => Value;
}

[JsonConverter(typeof(PayaffeErrorCodeJsonConverter))]
public readonly record struct PayaffeErrorCode
{
    public PayaffeErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public static PayaffeErrorCode UnexpectedError { get; } = new("unexpected_error");

    /// <summary>
    /// The installation is not in Test Mode, so it has no route to simulate a
    /// payment. Raised by the SDK, not sent by the server: a live installation
    /// does not know the route exists.
    /// </summary>
    public static PayaffeErrorCode TestModeUnavailable { get; } = new("test_mode.unavailable");

    public override string ToString() => Value;
}

internal abstract class StringValueJsonConverter<T> : JsonConverter<T>
{
    protected abstract T Create(string value);

    protected abstract string GetValue(T value);

    public sealed override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException($"Expected a non-empty {typeof(T).Name} string.");
        }

        return Create(value);
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options) => writer.WriteStringValue(GetValue(value));
}

internal sealed class PaymentStatusJsonConverter : StringValueJsonConverter<PaymentStatus>
{
    protected override PaymentStatus Create(string value) => new(value);

    protected override string GetValue(PaymentStatus value) => value.Value;
}

internal sealed class SupportedCurrencyJsonConverter : StringValueJsonConverter<SupportedCurrency>
{
    protected override SupportedCurrency Create(string value) => new(value);

    protected override string GetValue(SupportedCurrency value) => value.Value;
}

internal sealed class PaymentOptionStatusJsonConverter : StringValueJsonConverter<PaymentOptionStatus>
{
    protected override PaymentOptionStatus Create(string value) => new(value);

    protected override string GetValue(PaymentOptionStatus value) => value.Value;
}

internal sealed class PayaffeErrorCodeJsonConverter : StringValueJsonConverter<PayaffeErrorCode>
{
    protected override PayaffeErrorCode Create(string value) => new(value);

    protected override string GetValue(PayaffeErrorCode value) => value.Value;
}
