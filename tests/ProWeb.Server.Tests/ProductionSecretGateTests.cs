using FluentAssertions;
using ProWeb.Server.Config;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>Centralized default-secret detection for the production boot gate (UT-C-R1-004).</summary>
public class ProductionSecretGateTests
{
    [Theory]
    [InlineData("dev-signing-key-change-me-please-32b!!")]
    [InlineData("dev-signing-key-change-me-please-32bytes!!")]
    [InlineData("5rC6r0m7t2Y0m0oQ0m9nZ0aX0bC0dE0fG0hI0jK0lM=")]
    public void KnownShippedDefaults_AreDetected(string value)
    {
        ProductionSecretGate.IsDefaultSecret(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("please-change-me-in-prod")]
    public void EmptyOrPlaceholder_IsDetected(string? value)
    {
        ProductionSecretGate.IsDefaultSecret(value).Should().BeTrue();
    }

    [Fact]
    public void StrongOperatorSecret_IsAllowed()
    {
        ProductionSecretGate.IsDefaultSecret("Zx9!kLp2Qw7@vBn4Rt6Yu8Es1Ad3Fg5Hj0").Should().BeFalse();
    }

    [Fact]
    public void DriftedConstants_BothCovered()
    {
        // The historical bug: source constant and appsettings.json value drifted apart.
        // Both must be in the known-default set so the gate cannot be silently bypassed.
        ProductionSecretGate.KnownDefaults.Should().Contain("dev-signing-key-change-me-please-32b!!");
        ProductionSecretGate.KnownDefaults.Should().Contain("dev-signing-key-change-me-please-32bytes!!");
    }
}
