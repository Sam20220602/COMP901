#  Analysis for 'Improving MMO Game Server Performance with Adaptive Packet Management and IOCP'

## Table of Contents
1. [Introduction]
2. [Source Code]
3. [Data]
4. [License]

---

## Introduction
This repository contains both the source code and data used for analyzing the performance of MMO game servers. The experiments include performance comparisons of synchronous, asynchronous, and IOCP server architectures, as well as adaptive packet transmission and Dead Reckoning algorithms.

---

## Source Code
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

---

## DATA
## Overview
This dataset is based on performance comparison experiments of synchronous, asynchronous, and IOCP server architectures, as well as experiments involving adaptive packet transmission strategies and Dead Reckoning algorithms for position prediction. The dataset is organized into three folders, each corresponding to a specific experiment.


## Folder Structure
- '01' folder: Server performance data for synchronous, asynchronous, IOCP, and client-side data
- '02' folder: Server performance data for testing adaptive packet transmission strategies
- '03' folder: Server performance data with the application of the Dead Reckoning algorithm

### '01' folder file list  
(Client Side) Async 10 client - LatencyData.csv  
(Client Side) Async 50 client - LatencyData.csv  
(Client Side) Async 100 client - LatencyData.csv  
(Client Side) Async 100 client (200ms) - LatencyData.csv  
(Client Side) IOCP 10 client - LatencyData.csv  
(Client Side) IOCP 50 client - LatencyData.csv  
(Client Side) IOCP 100 client - LatencyData.csv  
(Client Side) IOCP 100 client (200ms) - LatencyData.csv  
(Client Side) Sync 10 client - LatencyData.csv  
(Client Side) Sync 50 client - LatencyData.csv  
(Client Side) Sync 100 client - LatencyData.csv  
(Client Side) Sync 100 client (200ms) - LatencyData.csv  
(Server Side) Async 10 client performance_log.csv  
(Server Side) Async 50 client performance_log.csv  
(Server Side) Async 100 client performance_log.csv  
(Server Side) Async 100 client (200ms) performance_log.csv  
(Server Side) IOCP 10 client performance_log.csv  
(Server Side) IOCP 50 client performance_log.csv  
(Server Side) IOCP 100 client performance_log.csv  
(Server Side) IOCP 100 client (200ms) performance_log.csv  
(Server Side) Sync 10 client performance_log.csv  
(Server Side) Sync 50 client performance_log.csv  
(Server Side) Sync 100 client performance_log.csv  
(Server Side) Sync 100 client (200ms) performance_log.csv  

### '02' folder file list  
(Server Side) adaptive 10 client battle mode no prior que - server_log.csv  
(Server Side) adaptive 10 client battle mode yes prior que - server_log.csv  
(Server Side) adaptive 10 client moving mode no prior que - server_log.csv  
(Server Side) adaptive 10 client moving mode yes prior que - server_log.csv  
(Server Side) fixed 10 client server_log.csv  

### '03' folder file list  
(Server Side) dead reckoning mode off 10ms server_log.csv  
(Server Side) dead reckoning mode off 500ms server_log.csv  
(Server Side) dead reckoning mode on 10ms server_log.csv  
(Server Side) dead reckoning mode on 100ms server_log.csv  
(Server Side) dead reckoning mode on 200ms server_log.csv  
(Server Side) dead reckoning mode on 300ms server_log.csv  
(Server Side) dead reckoning mode on 400ms server_log.csv  
(Server Side) dead reckoning mode on 450ms server_log.csv  
(Server Side) dead reckoning mode on 500ms server_log.csv  

## File Descriptions
- ..LatencyData.csv: Latency data collected on the client side. Each file is categorized by the number of clients (10, 50, 100) and network conditions (200ms).
- ..performance_log.csv: Performance logs collected on the server side, including CPU usage, memory usage, and network traffic data.
- ..server_log.csv: Data logging server performance under specific modes (adaptive, dead reckoning).

### Attribution
Please include appropriate credit when using this dataset in your research or projects, citing the source as follows:

Samyeol LEE, "Dataset for MMO Game Server Performance Analysis," 2024. Available at: https://github.com/Sam20220602/COMP901.git

### No Warranty
The dataset is provided "as-is," without any warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and non-infringement.

### Contributions
If you use this dataset in your research, we would appreciate it if you could share your results or cite this dataset in your publications.

---

## License
This project is licensed under the MIT License. Please see the LICENSE file for more details.