using Codekali.Net.Config.UI.Extensions;
using FluentAssertions;
using Xunit;

namespace Codekali.Net.Config.UI.Tests;

public class JsonCommentPreservingWriterTests
{
    private const string SimpleJson = """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information"
            }
          },
          "AllowedHosts": "*"
        }
        """;

    private const string CommentedJson = """
        {
          // App logging settings
          "Logging": {
            /* controls minimum level */
            "LogLevel": {
              "Default": "Information"
            }
          },
          "AllowedHosts": "*"
        }
        """;

    private const string ArrayJson = """
        {
          "AllowedOrigins": [
            "https://example.com",
            "https://test.com"
          ]
        }
        """;

    [Fact]
    public void SetValue_UpdatesScalarValue()
    {
        var result = JsonCommentPreservingWriter.SetValue(SimpleJson, "AllowedHosts", "\"localhost\"");
        result.Should().Contain("\"localhost\"");
        result.Should().NotContain("\"*\"");
    }

    [Fact]
    public void SetValue_UpdatesNestedValue()
    {
        var result = JsonCommentPreservingWriter.SetValue(SimpleJson, "Logging:LogLevel:Default", "\"Warning\"");
        result.Should().Contain("\"Warning\"");
        result.Should().NotContain("\"Information\"");
    }

    [Fact]
    public void SetValue_PreservesComments_OnUpdate()
    {
        var result = JsonCommentPreservingWriter.SetValue(CommentedJson, "AllowedHosts", "\"localhost\"");
        result.Should().Contain("// App logging settings");
        result.Should().Contain("/* controls minimum level */");
        result.Should().Contain("\"localhost\"");
    }

    [Fact]
    public void SetValue_AddsNewKey_AtRoot()
    {
        var result = JsonCommentPreservingWriter.SetValue(SimpleJson, "NewKey", "\"NewValue\"");
        result.Should().Contain("\"NewKey\"");
        result.Should().Contain("\"NewValue\"");
    }

    [Fact]
    public void SetValue_AddsNewNestedKey_InExistingObject()
    {
        var result = JsonCommentPreservingWriter.SetValue(SimpleJson, "Logging:LogLevel:Microsoft", "\"Warning\"");
        result.Should().Contain("\"Microsoft\"");
        result.Should().Contain("\"Warning\"");
        // Original key must still be present
        result.Should().Contain("\"Default\"");
        result.Should().Contain("\"Information\"");
    }

    [Fact]
    public void SetValue_AddsNewKey_PreservesComments()
    {
        var result = JsonCommentPreservingWriter.SetValue(CommentedJson, "NewSetting", "true");
        result.Should().Contain("// App logging settings");
        result.Should().Contain("/* controls minimum level */");
        result.Should().Contain("\"NewSetting\": true");
    }

    [Fact]
    public void RemoveKey_RemovesExistingKey()
    {
        var result = JsonCommentPreservingWriter.RemoveKey(SimpleJson, "AllowedHosts");
        result.Should().NotContain("\"AllowedHosts\"");
        result.Should().Contain("\"Logging\"");
    }

    [Fact]
    public void RemoveKey_PreservesComments()
    {
        var result = JsonCommentPreservingWriter.RemoveKey(CommentedJson, "AllowedHosts");
        result.Should().Contain("// App logging settings");
        result.Should().Contain("/* controls minimum level */");
        result.Should().NotContain("\"AllowedHosts\"");
    }

    [Fact]
    public void RemoveKey_IsNoOp_WhenKeyNotFound()
    {
        var result = JsonCommentPreservingWriter.RemoveKey(SimpleJson, "NonExistent");
        result.Should().Be(SimpleJson);
    }

    [Fact]
    public void Validate_ReturnsNull_ForValidJson()
    {
        JsonCommentPreservingWriter.Validate(SimpleJson).Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsNull_ForCommentedJson()
    {
        JsonCommentPreservingWriter.Validate(CommentedJson).Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsError_ForInvalidJson()
    {
        JsonCommentPreservingWriter.Validate("{ invalid }").Should().NotBeNull();
    }

    [Fact]
    public void AppendToArray_AppendsItem_ToExistingArray()
    {
        var (result, error) = JsonCommentPreservingWriter.AppendToArray(
            ArrayJson, "AllowedOrigins", "\"https://new.com\"");

        error.Should().BeNull();
        result.Should().Contain("\"https://new.com\"");
        result.Should().Contain("\"https://example.com\"");
        result.Should().Contain("\"https://test.com\"");
    }

    [Fact]
    public void AppendToArray_CreatesArray_WhenKeyNotPresent()
    {
        var (result, error) = JsonCommentPreservingWriter.AppendToArray(
            SimpleJson, "NewArray", "\"item1\"");

        error.Should().BeNull();
        result.Should().Contain("\"NewArray\"");
        result.Should().Contain("\"item1\"");
    }

    [Fact]
    public void AppendToArray_ReturnsError_WhenKeyIsNotArray()
    {
        var (_, error) = JsonCommentPreservingWriter.AppendToArray(
            SimpleJson, "AllowedHosts", "\"item\"");

        error.Should().NotBeNull();
    }

    [Fact]
    public void RemoveFromArray_RemovesItemAtIndex()
    {
        var (result, error) = JsonCommentPreservingWriter.RemoveFromArray(
            ArrayJson, "AllowedOrigins", 0);

        error.Should().BeNull();
        result.Should().NotContain("\"https://example.com\"");
        result.Should().Contain("\"https://test.com\"");
    }

    [Fact]
    public void RemoveFromArray_ReturnsError_ForOutOfRangeIndex()
    {
        var (_, error) = JsonCommentPreservingWriter.RemoveFromArray(
            ArrayJson, "AllowedOrigins", 99);

        error.Should().NotBeNull();
    }

    [Fact]
    public void SetValue_UpdatesArrayItem_ByIndex()
    {
        var result = JsonCommentPreservingWriter.SetValue(
            ArrayJson, "AllowedOrigins:0", "\"https://updated.com\"");

        result.Should().Contain("\"https://updated.com\"");
        result.Should().NotContain("\"https://example.com\"");
        result.Should().Contain("\"https://test.com\"");
    }
}