using System;
using System.Runtime.Serialization;

namespace DockStationDotNet {
  internal class DockerContainerCreationException : Exception {
    public DockerContainerCreationException() {
    }

    public DockerContainerCreationException(string message) : base(message) {
    }

    public DockerContainerCreationException(string message, Exception innerException) : base(message, innerException) {
    }

    protected DockerContainerCreationException(SerializationInfo info, StreamingContext context) : base(info, context) {
    }
  }
}
