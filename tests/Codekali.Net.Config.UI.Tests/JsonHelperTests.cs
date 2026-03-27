using System.Text.Json.Nodes;
using Codekali.Net.Config.UI.Services;
using FluentAssertions;
using Xunit;

namespace Codekali.Net.Config.UI.Tests;

public class JsonHelperTests
{
    // ── ParseObject ────────────────────────────────────────────────────────

    [Fact]
    public void ParseObject_ValidJson_ReturnsJsonObject()
    {
        var result = JsonHelper.ParseObject("""{"Key":"Value"}""");
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseObject_InvalidJson_ReturnsNull()
    {
        var result = JsonHelper.ParseObject("not json at all");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseObject_JsonArray_ReturnsNull()
    {
        var result = JsonHelper.ParseObject("""["a","b"]""");
        result.Should().BeNull();
    }

    // ── Validate ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidJson_ReturnsNull()
    {
        var error = JsonHelper.Validate("""{"a":1}""");
        error.Should().BeNull();
    }

    [Fact]
    public void Validate_InvalidJson_ReturnsErrorMessage()
    {
        var error = JsonHelper.Validate("{broken");
        error.Should().NotBeNullOrEmpty();
    }

    // ── GetNode ────────────────────────────────────────────────────────────

    [Fact]
    public void GetNode_TopLevelKey_ReturnsValue()
    {
        var root = JsonHelper.ParseObject("""{"Name":"Alice"}""")!;
        var node = JsonHelper.GetNode(root, "Name");
        node.Should().NotBeNull();
        node!.GetValue<string>().Should().Be("Alice");
    }

    [Fact]
    public void GetNode_NestedKey_ReturnsValue()
    {
        var root = JsonHelper.ParseObject("""{"Logging":{"LogLevel":{"Default":"Information"}}}""")!;
        var node = JsonHelper.GetNode(root, "Logging:LogLevel:Default");
        node!.GetValue<string>().Should().Be("Information");
    }

    [Fact]
    public void GetNode_MissingKey_ReturnsNull()
    {
        var root = JsonHelper.ParseObject("""{"Name":"Alice"}""")!;
        var node = JsonHelper.GetNode(root, "DoesNotExist");
        node.Should().BeNull();
    }

    // ── SetNode ────────────────────────────────────────────────────────────

    [Fact]
    public void SetNode_NewTopLevelKey_SetsValue()
    {
        var root = new JsonObject();
        JsonHelper.SetNode(root, "AppName", JsonValue.Create("MyApp"));
        root["AppName"]!.GetValue<string>().Should().Be("MyApp");
    }

    [Fact]
    public void SetNode_NestedPath_CreatesIntermediateObjects()
    {
        var root = new JsonObject();
        JsonHelper.SetNode(root, "A:B:C", JsonValue.Create(42));
        var node = JsonHelper.GetNode(root, "A:B:C");
        node!.GetValue<int>().Should().Be(42);
    }

    // ── RemoveNode ─────────────────────────────────────────────────────────

    [Fact]
    public void RemoveNode_ExistingKey_ReturnsTrueAndRemoves()
    {
        var root = JsonHelper.ParseObject("""{"Key":"Value","Other":"Keep"}""")!;
        var removed = JsonHelper.RemoveNode(root, "Key");
        removed.Should().BeTrue();
        JsonHelper.GetNode(root, "Key").Should().BeNull();
        JsonHelper.GetNode(root, "Other").Should().NotBeNull();
    }

    [Fact]
    public void RemoveNode_MissingKey_ReturnsFalse()
    {
        var root = JsonHelper.ParseObject("""{"Key":"Value"}""")!;
        var removed = JsonHelper.RemoveNode(root, "Ghost");
        removed.Should().BeFalse();
    }

    [Fact]
    public void RemoveNode_NestedKey_RemovesCorrectly()
    {
        var root = JsonHelper.ParseObject("""{"Parent":{"Child":"data","Sibling":"keep"}}""")!;
        JsonHelper.RemoveNode(root, "Parent:Child");
        JsonHelper.GetNode(root, "Parent:Child").Should().BeNull();
        JsonHelper.GetNode(root, "Parent:Sibling").Should().NotBeNull();
    }

    // ── Flatten ────────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_NestedObject_ProducesDotNotationKeys()
    {
        var root = JsonHelper.ParseObject("""{"Logging":{"LogLevel":{"Default":"Info"}}}""")!;
        var flat = JsonHelper.Flatten(root);
        flat.Should().ContainKey("Logging:LogLevel:Default");
    }

    [Fact]
    public void Flatten_EmptyObject_ReturnsEmptyDictionary()
    {
        var root = new JsonObject();
        var flat = JsonHelper.Flatten(root);
        flat.Should().BeEmpty();
    }

    // ── IsSensitiveKey ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Password")]
    [InlineData("DbPassword")]
    [InlineData("SecretKey")]
    [InlineData("ApiToken")]
    [InlineData("ApiKey")]
    [InlineData("ConnectionString")]
    public void IsSensitiveKey_SensitiveNames_ReturnsTrue(string key)
    {
        JsonHelper.IsSensitiveKey(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("AppName")]
    [InlineData("Version")]
    [InlineData("Environment")]
    public void IsSensitiveKey_NonSensitiveNames_ReturnsFalse(string key)
    {
        JsonHelper.IsSensitiveKey(key).Should().BeFalse();
    }
}
