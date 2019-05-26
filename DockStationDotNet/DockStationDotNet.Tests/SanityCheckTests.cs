using System;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using DockStationDotNet.Tests.Fixtures;
using NFluent;
using Xunit;

namespace DockStationDotNet.Tests {
  public partial class SanityCheckTests : IClassFixture<DockerHostCreatorFixture> {
    private readonly DockerHostCreatorFixture _dockerFixture;

    public SanityCheckTests(DockerHostCreatorFixture dockerFixture) {
      _dockerFixture = dockerFixture ?? throw new ArgumentNullException(nameof(dockerFixture));
    }

    [Fact]
    public async Task GettingAListOfContainersShouldWork() {
      var containers = await _dockerFixture.ConfiguredClient.Containers.ListContainersAsync(new ContainersListParameters() {
        Limit = 10,
      });

      Check.That(containers).IsNotNull();
    }
  }
}
