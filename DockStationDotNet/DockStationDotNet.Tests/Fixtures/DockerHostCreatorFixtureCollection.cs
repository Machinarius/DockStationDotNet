using Xunit;

namespace DockStationDotNet.Tests.Fixtures {
  [CollectionDefinition(Name)]
  public class DockerHostCreatorFixtureCollection : ICollectionFixture<DockerHostCreatorFixture> {
    public const string Name = nameof(DockerHostCreatorFixtureCollection);
  }
}
