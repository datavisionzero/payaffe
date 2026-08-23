using System.ComponentModel.DataAnnotations;

namespace Payaffe.Mcp;

public sealed class AdminMcpOptions : IValidatableObject
{
    /// <summary>
    /// The Admin Account the local MCP host acts as. Audit Log entries are
    /// attributed to this account with `mcp` as the source service, and native
    /// ETH Address Pool imports reference it as the importing account.
    /// </summary>
    public Guid AdminAccountId { get; set; }

    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 60;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] is satisfied by default(Guid), so an unset account would
        // otherwise start the host and only fail at the Address Pool import
        // foreign key.
        if (AdminAccountId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Mcp:Admin:AdminAccountId must be an existing Admin Account.",
                [nameof(AdminAccountId)]);
        }

        if (Window < TimeSpan.FromSeconds(1))
        {
            yield return new ValidationResult(
                "Mcp:Admin:Window must be at least one second.",
                [nameof(Window)]);
        }
    }
}
