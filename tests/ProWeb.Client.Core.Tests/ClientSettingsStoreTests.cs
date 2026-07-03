using FluentAssertions;
using ProWeb.Client.Core;
using Xunit;

namespace ProWeb.Client.Core.Tests;

public class ClientSettingsStoreTests
{
    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proweb-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ClientSettingsStore(path);
            var settings = new ClientSettings
            {
                ServerBaseUrl = "https://proxy.internal:9443",
                DeviceId = "dev-42",
                HomePage = "https://start.example",
            };
            store.Save(settings);

            var loaded = store.Load();
            loaded.ServerBaseUrl.Should().Be("https://proxy.internal:9443");
            loaded.DeviceId.Should().Be("dev-42");
            loaded.HomePage.Should().Be("https://start.example");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proweb-missing-{Guid.NewGuid():N}.json");
        var loaded = new ClientSettingsStore(path).Load();
        loaded.ServerBaseUrl.Should().StartWith("https://");
        loaded.IsServerUrlValid.Should().BeTrue();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proweb-corrupt-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ this is not valid json ");
            var loaded = new ClientSettingsStore(path).Load();
            loaded.IsServerUrlValid.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("https://ok:8443", true)]
    [InlineData("http://plain", true)]
    [InlineData("ftp://nope", false)]
    [InlineData("not a url", false)]
    public void IsServerUrlValid_ValidatesScheme(string url, bool expected)
    {
        new ClientSettings { ServerBaseUrl = url }.IsServerUrlValid.Should().Be(expected);
    }
}
