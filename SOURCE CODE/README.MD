## Experiment Details

### Prerequisites
Before building and running the projects, make sure to install the necessary NuGet package:

Install-Package System.Diagnostics.PerformanceCounter -Version 6.0.0


### Experiment 1: Synchronous, Asynchronous, and IOCP Server Performance
- Server Side:
  - `ASyncronous Type Server.csproj`: Implements an asynchronous server.
  - `IOCP Server.csproj`: Implements an IOCP-based server.
  - `Syncronous Type Server.csproj`: Implements a synchronous server.
- Client Side:
  - `Asyncronous Type Client.csproj`: Asynchronous client implementation.
  - `IOCP Client.csproj`: IOCP-based client.
  - `Syncronous Type Client.csproj`: Synchronous client.

### Experiment 2: Adaptive Packet Transmission Simulation
- Server:
  - `fixed and adaption simulation server.csproj`: Simulates adaptive and fixed packet transmission strategies.
- Client:
  - `fixed and adaption packet simulation - client.csproj`: Client for testing adaptive strategies.

### Experiment 3: Dead Reckoning with IOCP
- Server:
  - `IOCP dead reckoning server csharp.csproj`: Dead Reckoning algorithm implemented with IOCP.
- Client:
  - `IOCP dead reckoning client csharp.csproj`: Client using Dead Reckoning.

## How to Build and Run
1. Open `research code.sln` using Visual Studio 2022.
2. Select the desired project and build using the `Debug` or `Release` configuration.
3. Run the project to start the server or client as needed.

## License
This project is licensed under the MIT License. Please see the LICENSE file for more details.
