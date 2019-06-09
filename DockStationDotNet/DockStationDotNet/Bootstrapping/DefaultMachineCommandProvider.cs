using DockStationDotNet.Bootstrapping.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DockStationDotNet.Bootstrapping {
  public class DefaultMachineCommandProvider {
    private const string DockerMachineDownloadBase = "https://github.com/docker/machine/releases/download/v0.16.1/docker-machine-";

    private readonly ITestHostEnvironment _testHostEnv;
    private readonly IWebDownloadClient _webClient;

    public DefaultMachineCommandProvider(ITestHostEnvironment testHostEnv, IWebDownloadClient webClient) {
      _testHostEnv = testHostEnv ?? throw new ArgumentNullException(nameof(testHostEnv));
      _webClient = webClient ?? throw new ArgumentNullException(nameof(webClient));
    }

    public async Task<string> GetCommandPathAsync() {
      if (_testHostEnv.DockerMachineIsInPath) {
        return "docker-machine";
      }

      var windowsSuffix = _testHostEnv.HostOS == OSPlatform.Windows ? ".exe" : "";
      var machineFile = "docker-machine" + windowsSuffix;
      if (_testHostEnv.FileExists(machineFile)) {
        return Path.GetFullPath(machineFile);
      }

      var osName = GetOSName();
      var archString = GetArchString();
      var commandUrl = DockerMachineDownloadBase + osName + "-" + archString + windowsSuffix;

      try { 
        await _webClient.DownloadUrlToFileAsync(commandUrl, machineFile);
      } catch (HttpRequestException ex) {
        throw new DockerMachineUnavailableException("Error downloading the Docker-Machine command", ex);
      }

      return Path.GetFullPath(machineFile);
    }

    private string GetOSName() {
      string GetNameOrNull(OSPlatform platform, string name) {
        return _testHostEnv.HostOS == platform ? name : null;
      }

      return
        GetNameOrNull(OSPlatform.Windows, "Windows") ??
        GetNameOrNull(OSPlatform.Linux, "Linux") ??
        GetNameOrNull(OSPlatform.OSX, "Darwin") ??
        throw new DockerMachineUnavailableException("The current operating system is not supported by Docker-Machine");
    }

    private string GetArchString() {
      string GetNameOrNull(Architecture arch, string name) {
        return _testHostEnv.HostArchitechture == arch ? name : null;
      }

      return
        GetNameOrNull(Architecture.X64, "x86_64") ??
        GetNameOrNull(Architecture.X86, "i386") ??
        GetNameOrNull(Architecture.Arm, "armhf") ??
        GetNameOrNull(Architecture.Arm64, "aarch64") ??
        throw new DockerMachineUnavailableException("The current OS Architecture is not supported by Docker-Machine");
    }
  }
}
