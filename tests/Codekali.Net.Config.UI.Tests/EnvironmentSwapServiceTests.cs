using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Codekali.Net.Config.UI.Tests;

public class EnvironmentSwapServiceTests
{
    private const string SourceFile = "appsettings.Development.json";
    private const string TargetFile = "appsettings.Production.json";

    private const string SourceJson = """
        {
          "Logging": { "LogLevel": { "Default": "Debug" } },
          "FeatureA": "dev-value",
          "SharedKey": "dev-shared"
        }
        """;

    private const string TargetJson = """
        {
          "Logging": { "LogLevel": { "Default": "Warning" } },
          "SharedKey": "prod-shared"
        }
        """;

    private static (EnvironmentSwapService svc, Mock<IConfigFileRepository> repo, Mock<IBackupService> backup)
        BuildSut(string? sourceJson = null, string? targetJson = null)
    {
        var repo = new Mock<IConfigFileRepository>();
        var backup = new Mock<IBackupService>();

        repo.Setup(r => r.FileExists(It.IsAny<string>())).Returns(true);
        repo.Setup(r => r.ResolvePath(SourceFile)).Returns($"/config/{SourceFile}");
        repo.Setup(r => r.ResolvePath(TargetFile)).Returns($"/config/{TargetFile}");
        repo.Setup(r => r.ReadAllTextAsync($"/config/{SourceFile}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceJson ?? SourceJson);
        repo.Setup(r => r.ReadAllTextAsync($"/config/{TargetFile}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetJson ?? TargetJson);

        backup.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(OperationResult<string>.Success("/backup/file.bak"));

        var svc = new EnvironmentSwapService(
            repo.Object, backup.Object, NullLogger<EnvironmentSwapService>.Instance);

        return (svc, repo, backup);
    }

    // ── ExecuteSwapAsync ── Copy ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_Copy_WritesTargetAndLeavesSourceUnchanged()
    {
        var (svc, repo, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = ["FeatureA"],
            Operation = SwapOperation.Copy,
            OverwriteExisting = true
        };

        var result = await svc.ExecuteSwapAsync(request);

        result.IsSuccess.Should().BeTrue();
        // Target written once; source NOT written (copy keeps source)
        repo.Verify(r => r.WriteAllTextAsync($"/config/{TargetFile}", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.WriteAllTextAsync($"/config/{SourceFile}", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ExecuteSwapAsync ── Move ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_Move_WritesBothFiles()
    {
        var (svc, repo, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = ["FeatureA"],
            Operation = SwapOperation.Move,
            OverwriteExisting = true
        };

        var result = await svc.ExecuteSwapAsync(request);

        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.WriteAllTextAsync($"/config/{TargetFile}", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.WriteAllTextAsync($"/config/{SourceFile}", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Collision detection ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_KeyExistsInTarget_OverwriteFalse_ReturnsFailure()
    {
        var (svc, repo, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = ["SharedKey"],       // exists in both
            Operation = SwapOperation.Copy,
            OverwriteExisting = false
        };

        var result = await svc.ExecuteSwapAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("SharedKey");
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteSwap_KeyExistsInTarget_OverwriteTrue_Succeeds()
    {
        var (svc, _, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = ["SharedKey"],
            Operation = SwapOperation.Copy,
            OverwriteExisting = true
        };

        var result = await svc.ExecuteSwapAsync(request);
        result.IsSuccess.Should().BeTrue();
    }

    // ── Same source and target ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_SameSourceAndTarget_ReturnsFailure()
    {
        var (svc, _, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = SourceFile,
            Keys = ["FeatureA"],
            Operation = SwapOperation.Copy
        };
        var result = await svc.ExecuteSwapAsync(request);
        result.IsSuccess.Should().BeFalse();
    }

    // ── Empty keys list ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_EmptyKeysList_ReturnsFailure()
    {
        var (svc, _, _) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = [],
            Operation = SwapOperation.Copy
        };
        var result = await svc.ExecuteSwapAsync(request);
        result.IsSuccess.Should().BeFalse();
    }

    // ── CompareFilesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CompareFiles_DetectsOnlyInSource()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.CompareFilesAsync(SourceFile, TargetFile);
        result.IsSuccess.Should().BeTrue();
        result.Value!.OnlyInSource.Should().Contain("FeatureA");
    }

    [Fact]
    public async Task CompareFiles_DetectsValueDifferences()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.CompareFilesAsync(SourceFile, TargetFile);
        result.IsSuccess.Should().BeTrue();
        result.Value!.ValueDifferences
            .Should().Contain(d => d.Key.Contains("Default"));
    }

    [Fact]
    public async Task CompareFiles_IdenticalFiles_ReturnsEmptyDiffSections()
    {
        var json = """{"Key":"Value"}""";
        var (svc, _, _) = BuildSut(json, json);
        var result = await svc.CompareFilesAsync(SourceFile, TargetFile);
        result.IsSuccess.Should().BeTrue();
        result.Value!.OnlyInSource.Should().BeEmpty();
        result.Value.OnlyInTarget.Should().BeEmpty();
        result.Value.ValueDifferences.Should().BeEmpty();
        result.Value.Identical.Should().Contain("Key");
    }

    // ── FindConflictsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FindConflicts_ReturnsKeysAlreadyInTarget()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.FindConflictsAsync(TargetFile, ["SharedKey", "NewKey"]);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("SharedKey");
        result.Value.Should().NotContain("NewKey");
    }

    // ── Backups triggered ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSwap_AlwaysBackupsBothFiles()
    {
        var (svc, _, backup) = BuildSut();
        var request = new SwapRequest
        {
            SourceFile = SourceFile,
            TargetFile = TargetFile,
            Keys = ["FeatureA"],
            Operation = SwapOperation.Copy,
            OverwriteExisting = true
        };
        await svc.ExecuteSwapAsync(request);

        backup.Verify(b => b.CreateBackupAsync(SourceFile, It.IsAny<CancellationToken>()), Times.Once);
        backup.Verify(b => b.CreateBackupAsync(TargetFile, It.IsAny<CancellationToken>()), Times.Once);
    }
}
