using System;
using System.Collections.Generic;

namespace DockStationDotNet {
  public class ContainerReference : IDisposable {
    public string DockerHost { get; }
    public IEnumerable<int> ExposedPorts { get; }
    public string ContainerId { get; }

    private readonly DockerContainerFactory _factory;

    internal ContainerReference(string dockerHost, IEnumerable<int> exposedPorts, string containerId, DockerContainerFactory factory) {
      DockerHost = dockerHost ?? throw new ArgumentNullException(nameof(dockerHost));
      ExposedPorts = exposedPorts ?? throw new ArgumentNullException(nameof(exposedPorts));
      ContainerId = containerId ?? throw new ArgumentNullException(nameof(containerId));
      _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Dispose() {
      _factory.DisposeContainerAsync(this).Wait();
    }
  }
}
