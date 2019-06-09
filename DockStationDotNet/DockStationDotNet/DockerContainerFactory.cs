using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using Newtonsoft.Json;

namespace DockStationDotNet {
  public class DockerContainerFactory {
    private readonly DockerClient _dockerClient;
    private readonly string _dockerHost;

    public DockerContainerFactory(DockerClient dockerClient, string dockerHost) {
      if (dockerHost == null) {
        throw new ArgumentNullException(nameof(dockerHost));
      }

      _dockerClient = dockerClient ?? throw new ArgumentNullException(nameof(dockerClient));
      _dockerHost = dockerHost;
    }

    public Task<ContainerReference> CreateContainerAsync(string containerName, int containerPort, string imageName, params string[] arguments) {
      return CreateContainerAsync(containerName, new[] { containerPort }, imageName, arguments);
    }

    public async Task<ContainerReference> CreateContainerAsync(string containerName, IEnumerable<int> containerPorts, string imageName, params string[] arguments) {
      if (containerPorts == null || !containerPorts.Any()) {
        throw new ArgumentNullException(nameof(containerPorts));
      }

      var currentImages = await _dockerClient.Images.ListImagesAsync(new ImagesListParameters { All = true });
      var imageExists = currentImages.Any(image => image.RepoTags.Any(tag => tag == imageName));
      if (!imageExists) {
        await _dockerClient.Images.CreateImageAsync(new ImagesCreateParameters {
          FromImage = imageName,
          Tag = "latest"
        }, null, new DummyProgressListener());
      }

      var containersList = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters {
        All = true
      });
      var existingContainer = containersList.FirstOrDefault(x => x.Names.Any(y => y.Contains(containerName)));
      if (existingContainer != null) {
        await _dockerClient.Containers.StopContainerAsync(existingContainer.ID, new ContainerStopParameters());
        try {
          await _dockerClient.Containers.RemoveContainerAsync(existingContainer.ID, new ContainerRemoveParameters());
        } catch {
          // Assume AutoRemove is on
        }
      }

      var portBindings = containerPorts.Select(x => KeyValuePair.Create(
          x.ToString(), new List<PortBinding> {
            new PortBinding {
              HostPort = x.ToString(),
              HostIP = "0.0.0.0"
            }
          })).ToDictionary(x => x.Key, x => (IList<PortBinding>)x.Value);

      var containerCreationResponse = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters {
        Name = containerName,
        Image = imageName,
        Cmd = arguments?.ToList(),
        HostConfig = new HostConfig {
          PublishAllPorts = true,
          PortBindings = portBindings,
          AutoRemove = true
        }
      });

      var startResponse = await _dockerClient.Containers.StartContainerAsync(containerCreationResponse.ID, new ContainerStartParameters());
      if (!startResponse) {
        throw new DockerContainerCreationException("The requested container could not be started.\n" +
          "Check the container documentation for missing arguments or environment variables");
      }

      var createdContainer = await _dockerClient.Containers.InspectContainerAsync(containerCreationResponse.ID);
      var exposedPorts = containerPorts
        .Select(x => createdContainer.NetworkSettings.Ports[x + "/tcp"])
        .Select(x => x.First())
        .Select(x => int.Parse(x.HostPort));

      return new ContainerReference(_dockerHost, exposedPorts, createdContainer.ID, this);
    }

    internal async Task DisposeContainerAsync(ContainerReference containerRef) {
      await _dockerClient.Containers.StopContainerAsync(containerRef.ContainerId, new ContainerStopParameters());
      try {
        await _dockerClient.Containers.RemoveContainerAsync(containerRef.ContainerId, new ContainerRemoveParameters());
      } catch {
        // Assume AutoRemove is on
      }
    }

    private class DummyProgressListener : IProgress<JSONMessage> {
      public void Report(JSONMessage value) {
        // ignored
      }
    }
  }
}
