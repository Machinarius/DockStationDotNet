using System.Threading.Tasks;

namespace DockStationDotNet.Bootstrapping.Helpers {
  public interface IWebDownloadClient {
    Task DownloadUrlToFileAsync(string expectedDownloadUrl, string expectedFilename);
  }
}
