using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace Payaffe.Mcp.Tests;

public sealed class McpContractTests
{
    private static readonly string[] RiskyTools =
        ["payment.settle", "webhook_delivery.resend", "address_pool.import_native_eth"];

    private static readonly string[] ExpectedTools =
    [
        "address_pool.import_native_eth",
        "address_pool.summarize",
        "audit_log.search",
        "configuration.summarize",
        "payment.inspect",
        "payment.search",
        "payment.settle",
        "project.list",
        "webhook_delivery.resend",
        "webhook_delivery.search",
    ];

    [Fact]
    public async Task Admin_tool_contract_matches_the_accepted_snapshot()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);
        var generated = BuildSnapshot(tools);
        var snapshotPath = ResolveRepositoryPath("docs", "contracts", "mcp", "admin.snapshot.json");

        if (ShouldUpdateSnapshot())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, generated);
        }

        Assert.True(
            File.Exists(snapshotPath),
            $"Accepted Admin MCP contract snapshot is missing at {snapshotPath}.");
        var accepted = await File.ReadAllTextAsync(snapshotPath);
        Assert.Equal(accepted.ReplaceLineEndings("\n"), generated.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Admin_surface_exposes_exactly_the_accepted_MVP_tools()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(
            ExpectedTools,
            tools.Select(tool => tool.ProtocolTool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Risky_tools_advertise_mutating_annotations_and_require_confirmation()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        foreach (var name in RiskyTools)
        {
            var tool = Assert.Single(tools, candidate => candidate.ProtocolTool.Name == name).ProtocolTool;
            Assert.False(tool.Annotations?.ReadOnlyHint);
            Assert.True(
                tool.InputSchema.GetProperty("properties").TryGetProperty("confirmed", out _),
                $"Risky tool {name} must take a confirmation input.");
        }
    }

    [Fact]
    public async Task Read_tools_advertise_read_only_annotations()
    {
        await using var client = await CreateClientAsync();
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None);

        foreach (var tool in tools.Where(candidate => !RiskyTools.Contains(candidate.ProtocolTool.Name)))
        {
            Assert.True(
                tool.ProtocolTool.Annotations?.ReadOnlyHint,
                $"Tool {tool.ProtocolTool.Name} is not a risky tool and must be annotated read-only.");
        }
    }

    private static async Task<McpClient> CreateClientAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "payaffe-admin-contract-test",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "Payaffe.Mcp.dll")],
            WorkingDirectory = ResolveRepositoryPath(),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                // Listing tools never opens a connection; the host only has to start.
                ["ConnectionStrings__Payaffe"] =
                    "Host=127.0.0.1;Port=1;Database=payaffe;Username=contract;Password=contract;Timeout=1",
                ["Mcp__Admin__AdminAccountId"] = "00000000-0000-0000-0000-0000000000ff",
            },
        });

        return await McpClient.CreateAsync(transport, cancellationToken: CancellationToken.None);
    }

    private static string BuildSnapshot(IEnumerable<McpClientTool> tools)
    {
        var contractTools = tools
            .OrderBy(tool => tool.ProtocolTool.Name, StringComparer.Ordinal)
            .Select(tool =>
            {
                var protocol = tool.ProtocolTool;
                return new ContractTool(
                    protocol.Name,
                    protocol.Title,
                    protocol.Description,
                    new ContractAnnotations(
                        protocol.Annotations?.ReadOnlyHint,
                        protocol.Annotations?.DestructiveHint,
                        protocol.Annotations?.IdempotentHint,
                        protocol.Annotations?.OpenWorldHint),
                    protocol.InputSchema.Clone(),
                    protocol.OutputSchema?.Clone());
            })
            .ToArray();

        var node = JsonSerializer.SerializeToNode(
            new ContractDocument("payaffe-admin", 1, contractTools),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            })!;

        return Canonicalize(node).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>
    /// Sorts object keys so the snapshot stays stable across serializer and SDK
    /// ordering changes. Value-only arrays are sorted too; arrays holding
    /// objects keep their order because it can carry meaning.
    /// </summary>
    private static JsonNode Canonicalize(JsonNode node)
    {
        if (node is JsonObject source)
        {
            var sorted = new JsonObject();
            foreach (var property in source.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                sorted[property.Key] = property.Value is null ? null : Canonicalize(property.Value);
            }

            return sorted;
        }

        if (node is JsonArray array)
        {
            var values = array.Select(value => value is null ? null : Canonicalize(value)).ToList();
            if (values.All(value => value is null or JsonValue))
            {
                values = values
                    .OrderBy(value => value?.ToJsonString() ?? "null", StringComparer.Ordinal)
                    .ToList();
            }

            var sorted = new JsonArray();
            foreach (var value in values)
            {
                sorted.Add(value);
            }

            return sorted;
        }

        return node.DeepClone();
    }

    private static bool ShouldUpdateSnapshot() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PAYAFFE_UPDATE_CONTRACT_SNAPSHOTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Payaffe.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Repository root was not found.");
        }

        return segments.Aggregate(directory.FullName, Path.Combine);
    }

    private sealed record ContractDocument(
        string Server,
        int ContractVersion,
        IReadOnlyCollection<ContractTool> Tools);

    private sealed record ContractTool(
        string Name,
        string? Title,
        string? Description,
        ContractAnnotations Annotations,
        JsonElement InputSchema,
        JsonElement? OutputSchema);

    private sealed record ContractAnnotations(
        bool? ReadOnly,
        bool? Destructive,
        bool? Idempotent,
        bool? OpenWorld);
}
