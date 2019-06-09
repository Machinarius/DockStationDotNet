using DockStationDotNet.Bootstrapping;
using DockStationDotNet.Bootstrapping.Helpers;
using Moq;
using NFluent;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace DockStationDotNet.Tests.Bootstrapping {
  public class DefaultMachineCommandProviderTests {
    private readonly Mock<ITestHostEnvironment> _envMock;
    private readonly Mock<IWebDownloadClient> _webClientMock;

    private readonly DefaultMachineCommandProvider _testSubject;

    public DefaultMachineCommandProviderTests() {
      _envMock = new Mock<ITestHostEnvironment>(MockBehavior.Strict);
      _webClientMock = new Mock<IWebDownloadClient>(MockBehavior.Strict);
      _testSubject = new DefaultMachineCommandProvider(_envMock.Object, _webClientMock.Object);
    }

    [Fact]
    public async Task TheDockerMachineCommandInThePathMustBeUsedIfOneExists() {
      _envMock.SetupGet(mock => mock.DockerMachineIsInPath).Returns(true);
      var commandPathResult = await _testSubject.GetCommandPathAsync();

      Check.That(commandPathResult).IsEqualTo("docker-machine");
    }

    [Theory]
    [MemberData(nameof(GetMachineDownloadTestData))]
    public async Task TheDockerMachineToolMustBeDownloadedIfNotInThePath(OSPlatform os, Architecture arch, string urlSuffix) {
      _envMock.SetupGet(mock => mock.DockerMachineIsInPath).Returns(false);
      _envMock.SetupGet(mock => mock.HostOS).Returns(os);
      _envMock.SetupGet(mock => mock.HostArchitechture).Returns(arch);
      _envMock.Setup(mock => mock.FileExists(It.IsAny<string>())).Returns(false);

      var expectedDownloadUrl = "https://github.com/docker/machine/releases/download/v0.16.1/docker-machine-" + urlSuffix;
      var expectedFilename = "docker-machine" + (os == OSPlatform.Windows ? ".exe" : "");
      _webClientMock
        .Setup(mock => mock.DownloadUrlToFileAsync(expectedDownloadUrl, expectedFilename))
        .Returns(Task.FromResult(1))
        .Verifiable();

      var commandPathResult = await _testSubject.GetCommandPathAsync();
      Check.That(commandPathResult.EndsWith(expectedFilename)).IsTrue();

      _webClientMock.VerifyAll();
    }

    [Fact]
    public void TryingToDownloadDockerMachineOnAnUnsupportedPlatformMustThrowAnException() {
      _envMock.SetupGet(mock => mock.DockerMachineIsInPath).Returns(false);
      _envMock.SetupGet(mock => mock.HostOS).Returns(OSPlatform.Windows);
      _envMock.SetupGet(mock => mock.HostArchitechture).Returns(Architecture.Arm64);
      _envMock.Setup(mock => mock.FileExists(It.IsAny<string>())).Returns(false);

      _webClientMock
        .Setup(mock => mock.DownloadUrlToFileAsync(It.IsAny<string>(), It.IsAny<string>()))
        .Throws<HttpRequestException>(); // Simulates a network/invalid response/404 error

      Check
        .ThatAsyncCode(() => _testSubject.GetCommandPathAsync())
        .Throws<DockerMachineUnavailableException>();
    }

    [Fact]
    public async Task ThePathToTheLocalDockerMachineExecutableMustBeReturnedAsAbsolute() {
      _envMock.SetupGet(mock => mock.DockerMachineIsInPath).Returns(false);
      _envMock.SetupGet(mock => mock.HostOS).Returns(OSPlatform.Windows);
      _envMock.SetupGet(mock => mock.HostArchitechture).Returns(Architecture.Arm64);
      _envMock.Setup(mock => mock.FileExists(It.IsAny<string>())).Returns(false);

      _webClientMock
        .Setup(mock => mock.DownloadUrlToFileAsync(It.IsAny<string>(), It.IsAny<string>()))
        .Returns(Task.FromResult(1));

      var commandPathResult = await _testSubject.GetCommandPathAsync();
      Check.That(commandPathResult).IsNotNull().And.IsNotEmpty();
      Check.That(Path.IsPathFullyQualified(commandPathResult)).IsTrue();
    }

    [Fact]
    public async Task TheLocalDockerMachineFileMustBeReusedWheneverPossible() {
      _envMock.SetupGet(mock => mock.DockerMachineIsInPath).Returns(false);
      _envMock.SetupGet(mock => mock.HostOS).Returns(OSPlatform.Windows);
      _envMock.SetupGet(mock => mock.HostArchitechture).Returns(Architecture.Arm64);
      _envMock.Setup(mock => mock.FileExists("docker-machine.exe")).Returns(true);

      var commandPathResult = await _testSubject.GetCommandPathAsync();
      Check.That(commandPathResult).IsNotNull().And.IsNotEmpty();
      Check.That(Path.IsPathFullyQualified(commandPathResult)).IsTrue();
    }

    #region Test Data
    public static IEnumerable<object[]> GetMachineDownloadTestData =>
      new List<object[]> {
        new object[] { OSPlatform.OSX, Architecture.X64, "Darwin-x86_64" },
        new object[] { OSPlatform.Linux, Architecture.Arm64, "Linux-aarch64" },
        new object[] { OSPlatform.Linux, Architecture.Arm, "Linux-armhf" },
        new object[] { OSPlatform.Linux, Architecture.X64, "Linux-x86_64" },
        new object[] { OSPlatform.Windows, Architecture.X64, "Windows-x86_64.exe" },
        new object[] { OSPlatform.Windows, Architecture.X86, "Windows-i386.exe" }
      };
    #endregion
  }
}
