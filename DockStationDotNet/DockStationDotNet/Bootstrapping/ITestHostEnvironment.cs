using System.Runtime.InteropServices;

namespace DockStationDotNet.Bootstrapping {
  public interface ITestHostEnvironment {
    bool DockerMachineIsInPath { get; }
    OSPlatform HostOS { get; }
    Architecture HostArchitechture { get; }
    bool FileExists(string pathToFile);
  }
}
