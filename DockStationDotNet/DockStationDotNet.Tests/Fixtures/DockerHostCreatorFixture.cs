using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using Docker.DotNet;
using Docker.DotNet.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using SystemX509Certificate2 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace DockStationDotNet.Tests.Fixtures {
  public class DockerHostCreatorFixture {
    private const string MachineName = "DockStationVM";

    private const string MachineDriverVariable = "DOCKER_MACHINE_DRIVER";
    private const string DockerHostVariable = "DOCKER_HOST";

    private const string HyperVSwitchNameVariable = "HYPERV_SWITCH_NAME";
    private const string DefaultHyperVSwitchName = "DockStationSwitch";

    private const string DockerMachineDownloadBase = "https://github.com/docker/machine/releases/download/v0.16.1";
    private const string DockerMachineExecutableName = ".docker-machine";

    private const string DockerWindowsPipeName = @"\\.\pipe\docker_engine";
    private const string DockerUnixPipeFilename = "/var/run/docker.sock";

    public string DockerHost { get; private set; }
    public DockerClient ConfiguredClient { get; private set; }

    public DockerHostCreatorFixture() {
      CreateDockerHostIfNeeded();
    }

    private void CreateDockerHostIfNeeded() {
      var dockerHostValue = Environment.GetEnvironmentVariable(DockerHostVariable);
      if (!string.IsNullOrEmpty(dockerHostValue)) {
        ConfigureClientWithHost(dockerHostValue);
        return;
      }

      if (CanUseNativeDocker()) {
        return;
      }

      DownloadDockerMachineIfNeeded();
      if (!MachineExists()) {
        CreateAndStartMachine();
      } else {
        StartMachineIfNeeded();
      }

      ConfigureClientWithMachineEnv();
    }

    private void ConfigureClientWithMachineEnv() {
      var envResult = RunDockerMachine("env", MachineName);
      if (envResult.ExitCode != 0) {
        throw new DockerMachineExecutionException("Could not read machine environment", envResult.ExitCode, envResult.Output);
      }

      var outputLines = envResult.Output.Split('\n').Where(x => x.Contains('=')).ToArray();
      var envOutput = outputLines
        .Select(x => x.StartsWith("$Env:") ? x.Substring("$Env:".Length) : x)
        .Select(x => x.StartsWith("SET") ? x.Substring("SET".Length) : x)
        .Select(x => x.Split('='))
        .Select(x => (key: x[0].Trim(), value: x[1].Trim()))
        .ToDictionary(x => x.key, x => x.value);

      var host = envOutput["DOCKER_HOST"];
      var machineName = envOutput["DOCKER_MACHINE_NAME"];

      var machineUsesTls = false;
      try {
        var tlsVerify = envOutput["DOCKER_TLS_VERIFY"];

        if (!string.IsNullOrEmpty(tlsVerify)) {
          machineUsesTls = true;
        }
      } catch (KeyNotFoundException) { }

      var certsPath = envOutput["DOCKER_CERT_PATH"];
      var pfxPath = Path.Combine(certsPath, "key.pfx");

      // Adapted from https://stackoverflow.com/a/46598020/528131
      if (machineUsesTls) {
        var cerPath = Path.Combine(certsPath, "cert.pem");
        var keyPath = Path.Combine(certsPath, "key.pem");

        X509Certificate publicKey = null;
        AsymmetricCipherKeyPair privateKey = null;

        using (var cerFile = File.OpenRead(cerPath)) {
          using (var cerDataReader = new StreamReader(cerFile)) {
            var cerReader = new PemReader(cerDataReader);
            publicKey = (X509Certificate)cerReader.ReadObject();
          }
        }

        using (var keyFile = File.OpenRead(keyPath)) {
          using (var keyDataReader = new StreamReader(keyFile)) {
            var keyReader = new PemReader(keyDataReader);
            privateKey = (AsymmetricCipherKeyPair)keyReader.ReadObject();
          }
        }

        var certStore = new Pkcs12StoreBuilder().Build();
        var certChain = new X509CertificateEntry[1];

        var privateKeyEntry = new AsymmetricKeyEntry(privateKey.Private);
        var publicKeyEntry = new X509CertificateEntry(publicKey);

        certChain[0] = publicKeyEntry;
        certStore.SetKeyEntry("dockerHostKey", privateKeyEntry, certChain);

        if (File.Exists(pfxPath)) {
          File.Delete(pfxPath);
        }

        using (var pfxFile = File.Create(pfxPath)) {
          certStore.Save(pfxFile, new char[0], new SecureRandom());
        }
      }

      ConfigureClientWithHost(host, machineUsesTls, pfxPath);
    }

    private void ConfigureClientWithHost(string host, bool enforceTls = false, string pfxFilePath = null) {
      if (enforceTls && string.IsNullOrEmpty(pfxFilePath)) {
        throw new InvalidOperationException("A PFX file with the TLS certificates must be provided to enforce TLS");
      }

      var hostUri = new Uri(host);
      DockerClientConfiguration clientConfig = null;
      
      if (!enforceTls) {
        clientConfig = new DockerClientConfiguration(hostUri);
      } else {
        var tlsCertificate = new SystemX509Certificate2(pfxFilePath);
        var credentials = new CertificateCredentials(tlsCertificate);
        credentials.ServerCertificateValidationCallback += (o, c, ch, er) => true;

        clientConfig = new DockerClientConfiguration(hostUri, credentials);
      }
      var client = clientConfig.CreateClient();

      if (host.StartsWith("npipe") || host.StartsWith("unix")) {
        DockerHost = "localhost";
      } else {
        DockerHost = hostUri.Host;
      }
      ConfiguredClient = client;
    }

    private bool CanUseNativeDocker() {
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
        var pipeExists = Directory.GetFiles(@"\\.\pipe\").Contains(DockerWindowsPipeName);
        if (pipeExists) {
          ConfigureClientWithHost("npipe://./pipe/docker_engine");
          return true;
        }
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
        var pipeExists = File.Exists(DockerUnixPipeFilename);
        if (pipeExists) {
          ConfigureClientWithHost("unix:///var/run/docker.sock");
          return true;
        }
      }

      return false;
    }

    private void CreateAndStartMachine() {
      var driverName = GetDriverName();
      var driverOptions = GetDriverOptions(driverName);

      var machineArgs = new List<string>(new[] { "create", "--driver", driverName });
      machineArgs.AddRange(driverOptions.SelectMany(kvp => new[] { kvp.Key, kvp.Value }));
      machineArgs.Add(MachineName);

      Console.WriteLine("Running docker-machine with args: " + string.Join(' ', machineArgs));
      var machineResult = RunDockerMachine(machineArgs.ToArray());
      if (machineResult.ExitCode != 0) {
        throw new DockerMachineExecutionException("Could not create a Docker Machine", machineResult.ExitCode, machineResult.Output);
      }
    }

    private Dictionary<string, string> GetDriverOptions(string driverName) {
      if (driverName == "hyperv") {
        var switchName = Environment.GetEnvironmentVariable(HyperVSwitchNameVariable);
        if (string.IsNullOrEmpty(switchName)) {
          Console.WriteLine($"Assuming default HyperV Switch name '{DefaultHyperVSwitchName}' - Give environment variable '{HyperVSwitchNameVariable}' a value to override this.");
          Console.WriteLine("Instructions for creating an External switch can be found on https://docs.microsoft.com/en-us/windows-server/virtualization/hyper-v/get-started/create-a-virtual-switch-for-hyper-v-virtual-machines");
          switchName = DefaultHyperVSwitchName;
        } else {
          Console.WriteLine($"Using overriden HyperV switch name - {switchName}");
        }

        return new Dictionary<string, string> {
          { "--hyperv-virtual-switch", switchName }
        };
      }

      return new Dictionary<string, string>();
    }

    private string GetDriverName() {
      var envDriverValue = Environment.GetEnvironmentVariable(MachineDriverVariable);
      if (!string.IsNullOrEmpty(envDriverValue)) {
        return envDriverValue;
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
        Console.WriteLine($"Windows detected, assuming Hyper-V is available.\n " +
          $"Set environment variable {MachineDriverVariable}=virtualbox to use VirtualBox instead");
        return "hyperv";
      }

      return "virtualbox";
    }

    private void StartMachineIfNeeded() {
      var currentMachines = GetCurrentMachinesInfo();
      var dockStationMachine = currentMachines.FirstOrDefault(x => x.Name == MachineName);
      if (dockStationMachine == null) {
        throw new InvalidOperationException($"Cannot start the '{MachineName}' machine as it does not exist");
      }

      if (dockStationMachine.IsRunning) {
        return;
      }

      StartMachine(MachineName);
    }

    private string GetMachineUrl() {
      var currentMachines = GetCurrentMachinesInfo();
      var dockStationMachine = currentMachines.FirstOrDefault(x => x.Name == MachineName);
      if (dockStationMachine == null || !dockStationMachine.IsRunning) {
        throw new InvalidOperationException($"The '{MachineName}' Docker Machine is not running");
      }

      return dockStationMachine.URL;
    }

    private bool MachineExists() {
      var currentMachines = GetCurrentMachinesInfo();
      var dockStationMachine = currentMachines.FirstOrDefault(x => x.Name == MachineName);
      if (dockStationMachine == null) {
        return false;
      }

      return true;
    }

    private void StartMachine(string machineName) {
      var startResult = RunDockerMachine("start", machineName);
      if (startResult.ExitCode != 0) {
        throw new DockerMachineExecutionException($"Could not start Docker Machine {machineName}", startResult.ExitCode, startResult.Output);
      }
    }

    private IEnumerable<DockerMachineInfo> GetCurrentMachinesInfo() {
      var lsResult = RunDockerMachine("ls", "--format", "{{.Name}};{{.DriverName}};{{.State}};{{.URL}}");
      var outputLines = lsResult.Output.Split('\n').Where(x => !string.IsNullOrEmpty(x));
      var machines = outputLines.Select(lineText => {
        var values = lineText.Split(';');
        var name = values[0];
        var driver = values[1];
        var state = values[2];
        var url = values[3];

        if (driver == "hyperv" && (state == "Uknown" || string.IsNullOrEmpty(state))) {
          throw new InvalidOperationException("These tests must run with admin permissions to interact with Hyper-V machines");
        }

        return new DockerMachineInfo {
          Name = name,
          Driver = driver,
          IsRunning = state == "Running",
          URL = url
        };
      }).ToArray();

      return machines.ToArray().AsEnumerable();
    }

    private (string Output, int ExitCode) RunDockerMachine(params string[] args) {
      var machineOutput = "";
      var machineExitCode = int.MinValue;

      using (var machineProcess = new Process()) {
        machineProcess.StartInfo.FileName = GetDockerMachineExecutablePath();
        machineProcess.StartInfo.Arguments = string.Join(' ', args);
        machineProcess.StartInfo.UseShellExecute = false;
        machineProcess.StartInfo.CreateNoWindow = true;
        machineProcess.StartInfo.RedirectStandardOutput = true;

        machineProcess.Start();
        machineProcess.WaitForExit();

        machineExitCode = machineProcess.ExitCode;
        using (var outputReader = machineProcess.StandardOutput) {
          machineOutput = outputReader.ReadToEnd();
        }
      }

      return (machineOutput, machineExitCode);
    }

    private void DownloadDockerMachineIfNeeded() {
      if (File.Exists(GetDockerMachineExecutablePath())) {
        return;
      }

      var osName = GetOSName();
      var archString = GetArchString();
      var extensionSuffix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

      var downloadUrl = $"{DockerMachineDownloadBase}/docker-machine-{osName}-{archString}{extensionSuffix}";
      var webClient = new WebClient();

      try {
        webClient.DownloadFile(downloadUrl, GetDockerMachineExecutablePath());
      } catch (Exception ex) {
        throw new Exception($"Could not download the Docker Machine executable from path '{downloadUrl}'", ex);
      }
    }

    private string GetDockerMachineExecutablePath() {
      return DockerMachineExecutableName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "");
    }

    private string GetOSName() {
      string GetNameOrNull(OSPlatform platform, string name) {
        return RuntimeInformation.IsOSPlatform(platform) ? name : null;
      }

      return
        GetNameOrNull(OSPlatform.Windows, "Windows") ??
        GetNameOrNull(OSPlatform.Linux, "Linux") ??
        GetNameOrNull(OSPlatform.OSX, "Darwin") ??
        throw new InvalidOperationException("The current operating system is not supported");
    }

    private string GetArchString() {
      string GetNameOrNull(Architecture arch, string name) {
        return RuntimeInformation.OSArchitecture == arch ? name : null;
      }

      return
        GetNameOrNull(Architecture.X64, "x86_64") ??
        GetNameOrNull(Architecture.X86, "i386") ??
        GetNameOrNull(Architecture.Arm, "armhf") ??
        GetNameOrNull(Architecture.Arm64, "aarch64") ??
        throw new InvalidOperationException("The current OS Architecture is not supported");
    }

    private class DockerMachineInfo {
      public string Name { get; set; }
      public string Driver { get; set; }
      public bool IsRunning { get; set; }
      public string URL { get; set; }
    }

    private class DockerMachineExecutionException : Exception {
      private readonly int _exitCode;
      private readonly string _output;

      public DockerMachineExecutionException(string message, int exitCode, string output) : base(message) {
        _exitCode = exitCode;
        _output = output;
      }

      public override string ToString() {
        return $"docker-machine exit code: {_exitCode} \n" +
          $"docker-machine output: \n " +
          $"{_output}";
      }
    }
  }
}
