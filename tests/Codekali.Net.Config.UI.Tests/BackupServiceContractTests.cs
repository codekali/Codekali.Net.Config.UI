using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using FluentAssertions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Codekali.Net.Config.UI.Tests
{
    // ── IBackupService direct tests ───────────────────────────────────────

    public class BackupServiceContractTests
    {
        private static Mock<IBackupService> BuildBackupMock()
        {
            var mock = new Mock<IBackupService>();

            mock.Setup(b => b.CreateBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<string>.Success("/backup/appsettings.json.20240101T120000.bak"));

            mock.Setup(b => b.ListBackupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IReadOnlyList<string>>.Success(
                    new[] { "/backup/appsettings.json.20240101T120000.bak" }));

            mock.Setup(b => b.RestoreBackupAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult.Success());

            return mock;
        }

        [Fact]
        public async Task CreateBackupAsync_WhenCalledExplicitly_ReturnsBackupPath()
        {
            var backup = BuildBackupMock();

            var result = await backup.Object.CreateBackupAsync("appsettings.json");

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().EndWith(".bak");
        }

        [Fact]
        public async Task ListBackupsAsync_ReturnsOrderedBackupList()
        {
            var backup = BuildBackupMock();

            var result = await backup.Object.ListBackupsAsync("appsettings.json");

            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeEmpty();
        }

        [Fact]
        public async Task RestoreBackupAsync_ValidPath_ReturnsSuccess()
        {
            var backup = BuildBackupMock();

            var result = await backup.Object.RestoreBackupAsync(
                "/backup/appsettings.json.20240101T120000.bak");

            result.IsSuccess.Should().BeTrue();
        }
    }
}
