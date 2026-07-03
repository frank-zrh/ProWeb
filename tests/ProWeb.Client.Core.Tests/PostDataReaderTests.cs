using FluentAssertions;
using ProWeb.Client.Core;
using Xunit;

namespace ProWeb.Client.Core.Tests;

/// <summary>UT-F-R3-001: request body extraction for write methods.</summary>
public class PostDataReaderTests
{
    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("DELETE", true)]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("post", true)]
    [InlineData(null, false)]
    public void MethodCanHaveBody_MatchesWriteMethods(string? method, bool expected)
    {
        PostDataReader.MethodCanHaveBody(method).Should().Be(expected);
    }

    [Fact]
    public void Combine_ConcatenatesSegmentsInOrder()
    {
        var body = PostDataReader.Combine(new[]
        {
            new byte[] { 1, 2 },
            new byte[] { 3 },
            new byte[] { 4, 5, 6 },
        });

        body.Should().Equal(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void Combine_FormUrlEncodedBody_RoundTripsExactBytes()
    {
        var expected = System.Text.Encoding.ASCII.GetBytes("user=alice&pass=secret%21");
        var body = PostDataReader.Combine(new[] { expected });
        body.Should().Equal(expected);
    }

    [Fact]
    public void Combine_NullOrEmptySegments_ReturnsNull()
    {
        PostDataReader.Combine(null).Should().BeNull();
        PostDataReader.Combine(new byte[]?[] { null, Array.Empty<byte>() }).Should().BeNull();
    }
}
