using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Codekali.Net.Config.UI.Tests;

public class AppSettingsServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static (AppSettingsService svc, Mock<IConfigFileRepository> repo, Mock<IBackupService> backup)
        BuildSut(string? fileContent = null, ConfigUIOptions? options = null)
    {
        var repo = new Mock<IConfigFileRepository>();
        var backup = new Mock<IBackupService>();
        var opts = options ?? new ConfigUIOptions();

        const string defaultJson = """
            {
              "AppName": "Test",
              "Nested": {
                "Child": "value"
              },
              "Flag": true
            }
            """;

        repo.Setup(r => r.FileExists(It.IsAny<string>())).Returns(true);
        repo.Setup(r => r.ResolvePath(It.IsAny<string>())).Returns<string>(fn => $"/config/{fn}");
        repo.Setup(r => r.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileContent ?? defaultJson);
        repo.Setup(r => r.DiscoverFiles()).Returns(new[] { "/config/appsettings.json" });
        repo.Setup(r => r.GetLastWriteTime(It.IsAny<string>())).Returns(DateTimeOffset.UtcNow);

        backup.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(OperationResult<string>.Success("/backup/file.bak"));

        var svc = new AppSettingsService(
            repo.Object, backup.Object, opts, NullLogger<AppSettingsService>.Instance);

        return (svc, repo, backup);
    }

    // ── GetAllFilesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAllFilesAsync_WhenFilesExist_ReturnsSuccess()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.GetAllFilesAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetAllFilesAsync_ExtractsBaseEnvironmentCorrectly()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.GetAllFilesAsync();
        result.Value![0].Environment.Should().Be("Base");
    }

    // ── GetEntriesAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetEntriesAsync_ValidJson_ReturnsEntries()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.GetEntriesAsync("appsettings.json");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetEntriesAsync_InvalidJson_ReturnsFailure()
    {
        var (svc, _, _) = BuildSut("{broken json");
        var result = await svc.GetEntriesAsync("appsettings.json");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetEntriesAsync_SensitiveKeysMaskedWhenOptionEnabled()
    {
        var opts = new ConfigUIOptions { MaskSensitiveValues = true };
        var json = """{"Password":"super-secret","AppName":"App"}""";
        var (svc, _, _) = BuildSut(json, opts);

        var result = await svc.GetEntriesAsync("appsettings.json");
        result.IsSuccess.Should().BeTrue();

        var pwdEntry = result.Value!.FirstOrDefault(e => e.Key == "Password");
        pwdEntry.Should().NotBeNull();
        pwdEntry!.IsMasked.Should().BeTrue();
        pwdEntry.RawValue.Should().BeNull();
    }

    // ── AddEntryAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddEntryAsync_NewKey_CallsWriteAndReturnsSuccess()
    {
        var (svc, repo, _) = BuildSut();
        var result = await svc.AddEntryAsync("appsettings.json", "NewKey", "\"hello\"");
        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddEntryAsync_DuplicateKey_ReturnsFailureWithoutWriting()
    {
        var (svc, repo, _) = BuildSut();
        var result = await svc.AddEntryAsync("appsettings.json", "AppName", "\"NewValue\"");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── UpdateEntryAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEntryAsync_ExistingKey_UpdatesValueAndWrites()
    {
        var (svc, repo, _) = BuildSut();
        var result = await svc.UpdateEntryAsync("appsettings.json", "AppName", "\"UpdatedName\"");
        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(),
            It.Is<string>(s => s.Contains("UpdatedName")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEntryAsync_NonExistentKey_ReturnsFailure()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.UpdateEntryAsync("appsettings.json", "GhostKey", "\"value\"");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
    }

    // ── DeleteEntryAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeleteEntryAsync_ExistingKey_DeletesAndWrites()
    {
        var (svc, repo, _) = BuildSut();
        var result = await svc.DeleteEntryAsync("appsettings.json", "AppName");
        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(),
            It.Is<string>(s => !s.Contains("\"AppName\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteEntryAsync_NonExistentKey_ReturnsFailure()
    {
        var (svc, _, _) = BuildSut();
        var result = await svc.DeleteEntryAsync("appsettings.json", "GhostKey");
        result.IsSuccess.Should().BeFalse();
    }

    // ── SaveRawJsonAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveRawJsonAsync_ValidJson_WritesAndReturnsSuccess()
    {
        var (svc, repo, _) = BuildSut();
        var newJson = """{"Fresh":"value"}""";
        var result = await svc.SaveRawJsonAsync("appsettings.json", newJson);
        result.IsSuccess.Should().BeTrue();
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(), newJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveRawJsonAsync_InvalidJson_ReturnsFailureWithoutWriting()
    {
        var (svc, repo, _) = BuildSut();
        var result = await svc.SaveRawJsonAsync("appsettings.json", "{not valid");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid JSON");
        repo.Verify(r => r.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Backup is explicit-only (not automatic on write) ──────────────────

    [Fact]
    public async Task AddEntryAsync_DoesNotAutoCreateBackup()
    {
        // Backup is user-triggered, not automatic on write.
        // This test guards against the behaviour being reintroduced.
        var (svc, _, backup) = BuildSut();

        await svc.AddEntryAsync("appsettings.json", "AnotherKey", "1");

        backup.Verify(
            b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateEntryAsync_DoesNotAutoCreateBackup()
    {
        var (svc, _, backup) = BuildSut();

        await svc.UpdateEntryAsync("appsettings.json", "AppName", "\"Changed\"");

        backup.Verify(
            b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteEntryAsync_DoesNotAutoCreateBackup()
    {
        var (svc, _, backup) = BuildSut();

        await svc.DeleteEntryAsync("appsettings.json", "AppName");

        backup.Verify(
            b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveRawJsonAsync_DoesNotAutoCreateBackup()
    {
        var (svc, _, backup) = BuildSut();

        await svc.SaveRawJsonAsync("appsettings.json", """{"Fresh":"value"}""");

        backup.Verify(
            b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
