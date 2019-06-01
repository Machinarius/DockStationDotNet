using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DockStationDotNet.Tests.Fixtures;
using NFluent;
using Xunit;

namespace DockStationDotNet.Tests {
  [Collection(DockerHostCreatorFixtureCollection.Name)]
  public class HttpEchoServerContainerTests {
    private readonly DockerHostCreatorFixture _dockerFixture;
    private readonly DockerClient _dockerClient;

    private readonly DockerContainerFactory _testSubject;

    public HttpEchoServerContainerTests(DockerHostCreatorFixture dockerFixture) {
      _dockerFixture = dockerFixture ?? throw new ArgumentNullException(nameof(dockerFixture));
      _testSubject = new DockerContainerFactory(_dockerFixture.ConfiguredClient, _dockerFixture.DockerHost);
      _dockerClient = _dockerFixture.ConfiguredClient;
    }

    [Fact]
    public async Task RequestingAContainerMustImplyContainerCreationOnTheMachine() {
      var containerRef = await _testSubject.CreateContainerAsync("httpEcho", 5678, "hashicorp/http-echo", "-text=\"Hello World\"");
      Check.That(containerRef).IsNotNull();

      var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters { });
      var createdContainer = containers.FirstOrDefault(response => response.Names.Any(name => name == "/httpEcho"));
      Check.That(createdContainer).IsNotNull();
      Check.That(createdContainer.Image).IsEqualTo("hashicorp/http-echo");
    }

    [Fact]
    public async Task RequestingAContainerWithExposedPortsMustResultInAReachableService() {
      var containerRef = await _testSubject.CreateContainerAsync("httpEcho", 5678, "hashicorp/http-echo", "-text=\"Hello World\"");
      Check.That(containerRef).IsNotNull();
      Check.That(containerRef.ExposedPorts).IsNotNull();
      Check.That(containerRef.ExposedPorts.Count()).IsEqualTo(1);

      var httpClient = new HttpClient();
      var testMessage = new HttpRequestMessage(HttpMethod.Get, $"http://{containerRef.DockerHost}:{containerRef.ExposedPorts.First()}");
      var response = await httpClient.SendAsync(testMessage);
      Check.That(response.IsSuccessStatusCode).IsTrue();

      var responseContent = await response.Content.ReadAsStringAsync();
      Check.That(responseContent.Trim()).IsEqualTo("\"Hello World\"");
    }

    [Fact]
    public async Task DisposingOfTheContainerReferenceObjectMustStopAndDestroyTheContainer() {
      var containerRef = await _testSubject.CreateContainerAsync("httpEcho", 5678, "hashicorp/http-echo", "-text=\"Hello World\"");
      containerRef.Dispose();

      var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters { });
      var containerExists = containers.Any(response => response.Names.Any(name => name == "/httpEcho"));
      Check.That(containerExists).IsFalse();
    }
  }
}
