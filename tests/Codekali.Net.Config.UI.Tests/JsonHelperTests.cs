using Codekali.Net.Config.UI.Services;
using FluentAssertions;
using Xunit;

namespace Codekali.Net.Config.UI.Tests;

public class JsonHelperTests
{
    private const string Json = """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information"
            }
          },
          "CorsSettings": {
            "AllowedOrigins": [
              "https://example.com",
              "https://test.com"
            ]
          },
          "Port": 5432,
          "Enabled": true,
          "NullKey": null,
          "$schema": "https://json.schemastore.org/appsettings.json"
        }
        """;

    [Fact]
    public void ParseObject_ReturnsJsonObject_ForValidJson()
    {
        var result = JsonPatchHelper.ParseObject(Json);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseObject_ReturnsNull_ForInvalidJson()
    {
        JsonPatchHelper.ParseObject("{ bad json }").Should().BeNull();
    }

    [Fact]
    public void ParseObject_HandlesComments()
    {
        var commented = "{ // comment\n \"Key\": \"Value\" }";
        var result = JsonPatchHelper.ParseObject(commented);
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetNode_ReturnsNestedValue()
    {
        var root = JsonPatchHelper.ParseObject(Json)!;
        var node = JsonPatchHelper.GetNode(root, "Logging:LogLevel:Default");
        node.Should().NotBeNull();
        node!.ToJsonString().Should().Be("\"Information\"");
    }

    [Fact]
    public void GetNode_ReturnsNull_ForMissingPath()
    {
        var root = JsonPatchHelper.ParseObject(Json)!;
        JsonPatchHelper.GetNode(root, "Logging:Missing:Key").Should().BeNull();
    }

    [Fact]
    public void GetNode_HandlesNumericSegment_ForArrayAccess()
    {
        var root = JsonPatchHelper.ParseObject(Json)!;
        var node = JsonPatchHelper.GetNode(root, "CorsSettings:AllowedOrigins:0");
        node.Should().NotBeNull();
        node!.ToJsonString().Should().Be("\"https://example.com\"");
    }

    [Fact]
    public void ToEntryTree_ProducesChildren_ForArrays()
    {
        var root = JsonPatchHelper.ParseObject(Json)!;
        var tree = JsonPatchHelper.ToEntryTree(root, "test.json", false);

        var cors = tree.Single(e => e.Key == "CorsSettings");
        var origins = cors.Children!.Single(e => e.Key == "AllowedOrigins");
        origins.ValueType.Should().Be(Codekali.Net.Config.UI.Models.ConfigValueType.Array);
        origins.Children.Should().HaveCount(2);
        origins.Children![0].ValueType.Should().Be(Codekali.Net.Config.UI.Models.ConfigValueType.ArrayItem);
    }

    [Fact]
    public void IsSensitiveKey_Masks_Password()
    {
        JsonPatchHelper.IsSensitiveKey("DatabasePassword").Should().BeTrue();
        JsonPatchHelper.IsSensitiveKey("ApiSecret").Should().BeTrue();
        JsonPatchHelper.IsSensitiveKey("JwtToken").Should().BeTrue();
    }

    [Fact]
    public void IsSensitiveKey_DoesNotMask_SchemaKey()
    {
        JsonPatchHelper.IsSensitiveKey("$schema").Should().BeFalse();
    }

    [Fact]
    public void IsSensitiveKey_DoesNotMask_AllowedOrigins()
    {
        JsonPatchHelper.IsSensitiveKey("AllowedOrigins").Should().BeFalse();
    }

    [Fact]
    public void IsSensitiveKey_DoesNotMask_AllowedMethods()
    {
        JsonPatchHelper.IsSensitiveKey("AllowedMethods").Should().BeFalse();
    }

    [Fact]
    public void Flatten_ProducesIndexedKeys_ForArrays()
    {
        var root = JsonPatchHelper.ParseObject(Json)!;
        var flat = JsonPatchHelper.Flatten(root);
        flat.Should().ContainKey("CorsSettings:AllowedOrigins:0");
        flat.Should().ContainKey("CorsSettings:AllowedOrigins:1");
    }
}