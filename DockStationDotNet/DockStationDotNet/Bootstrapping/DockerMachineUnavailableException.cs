using System;

namespace DockStationDotNet.Bootstrapping {
  public class DockerMachineUnavailableException : Exception {
    public DockerMachineUnavailableException(string message) : base(message) { }

    public DockerMachineUnavailableException(string message, Exception innerException) : base(message, innerException) { }
  }
}
