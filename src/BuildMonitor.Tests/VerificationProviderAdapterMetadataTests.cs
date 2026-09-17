using BuildMonitor.Core.Rules;

namespace BuildMonitor.Tests;

public sealed class VerificationProviderAdapterMetadataTests
{
    [Fact]
    public void Canonical_constants_match_contract_version_one()
    {
        Assert.Equal("1", VerificationProviderAdapterMetadataParser.ContractVersion);
        Assert.Equal("1.0.0", VerificationProviderAdapterMetadataParser.AdapterVersion);
        Assert.Equal("Unthred/BuildMonitor", VerificationProviderAdapterMetadataParser.AdapterSource);
        Assert.Equal(
            "docs/ops/agent-skills/buildmonitor-control-plane",
            VerificationProviderAdapterMetadataParser.AdapterSourcePath);
    }

    [Fact]
    public void Parse_rejects_drifted_version_or_source()
    {
        var drifted = """
            ---
            name: buildmonitor-control-plane
            verificationProvider: true
            verificationProviderContractVersion: "1"
            adapterVersion: "0.9.0"
            adapterSource: Unthred/BuildMonitor
            adapterSourcePath: docs/ops/agent-skills/buildmonitor-control-plane
            ---
            """;
        var metadata = VerificationProviderAdapterMetadataParser.Parse(drifted);
        Assert.False(VerificationProviderAdapterMetadataParser.MatchesCanonical(metadata));
    }

    [Fact]
    public void Exclusive_ownership_forbids_direct_dotnet_once_claimed()
    {
        Assert.False(VerificationProviderSession.AllowsDirectDotNet(claimed: true));
        Assert.True(VerificationProviderSession.AllowsDirectDotNet(claimed: false));
        Assert.Equal(VerificationOperation.ShipCheck, VerificationProviderSession.FinalVerificationOperation);
    }
}
