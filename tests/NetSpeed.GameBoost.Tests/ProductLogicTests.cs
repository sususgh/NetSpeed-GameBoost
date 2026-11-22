using System.IO;
using NetSpeed.GameBoost.Desktop.Networking;
using NetSpeed.GameBoost.Desktop.Profiles;
using Xunit;

namespace NetSpeed.GameBoost.Tests;

public sealed class ProductLogicTests
{
    [Fact]
    public void WireGuardConfig_ValidStructure_IsAccepted()
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".conf");
        try
        {
            File.WriteAllText(path, """
                [Interface]
                PrivateKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                Address=10.0.0.2/32

                [Peer]
                PublicKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                AllowedIPs=0.0.0.0/0, ::/0
                Endpoint=198.51.100.1:51820
                """);

            var result = new WireGuardTunnelManager().ValidateConfig(path);

            Assert.True(result.IsValid, string.Join("; ", result.Errors));
            Assert.Empty(result.Errors);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WireGuardConfig_MissingEndpoint_IsRejected()
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".conf");
        try
        {
            File.WriteAllText(path, """
                [Interface]
                PrivateKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=

                [Peer]
                PublicKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                """);

            var result = new WireGuardTunnelManager().ValidateConfig(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("Endpoint", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WireGuardConfig_InvalidBase64Key_IsRejected()
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".conf");
        try
        {
            File.WriteAllText(path, """
                [Interface]
                PrivateKey=not-a-valid-key
                Address=10.0.0.2/32

                [Peer]
                PublicKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                AllowedIPs=0.0.0.0/0
                Endpoint=198.51.100.1:51820
                """);

            var result = new WireGuardTunnelManager().ValidateConfig(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("PrivateKey", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WireGuardConfig_InvalidEndpoint_IsRejected()
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), ".conf");
        try
        {
            File.WriteAllText(path, """
                [Interface]
                PrivateKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                Address=10.0.0.2/32

                [Peer]
                PublicKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
                AllowedIPs=0.0.0.0/0
                Endpoint=not-a-host:70000
                """);

            var result = new WireGuardTunnelManager().ValidateConfig(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, error => error.Contains("Endpoint", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RelayCatalog_SaveAndLoad_RoundTripsNode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netspeed-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "nodes.json");
        try
        {
            var expected = new RelayNode("relay-1", "Test relay", "relay.example.invalid", 443, "TCP", "test");
            var catalog = new RelayNodeCatalog();

            catalog.Save(path, [expected]);
            var actual = Assert.Single(catalog.Load(path));

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RelayCatalog_NullTransport_IsRejected()
    {
        var catalog = new RelayNodeCatalog();
        var node = new RelayNode("relay-1", "Test relay", "relay.example.invalid", 443, null!, "test");

        Assert.Throws<InvalidDataException>(() => catalog.Save(
            Path.Combine(Path.GetTempPath(), $"netspeed-{Guid.NewGuid():N}.json"), [node]));
    }

    [Fact]
    public void RouteScore_UsesMeasuredInputsAndIsBounded()
    {
        var score = GameRouteAnalyzer.CalculateScore(50, 10, 0);

        Assert.Equal(88.5, score);
        Assert.Equal(0, GameRouteAnalyzer.CalculateScore(double.NaN, 0, 0));
        Assert.InRange(GameRouteAnalyzer.CalculateScore(1, 1, 0), 0, 100);
    }

    [Theory]
    [InlineData("game.exe", true)]
    [InlineData("game", true)]
    [InlineData("../game.exe", false)]
    [InlineData("", false)]
    public void GameProfile_ProcessNameValidation_IsSafe(string name, bool expected)
    {
        Assert.Equal(expected, GameProfileService.IsValidProcessName(name));
    }
}
