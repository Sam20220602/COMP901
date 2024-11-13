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

## License
This dataset is licensed under the MIT License. You are free to use, copy, modify, merge, publish, distribute, and sublicense the dataset under the following conditions:

### Attribution
Please include appropriate credit when using this dataset in your research or projects, citing the source as follows:

Samyeol LEE, "Dataset for MMO Game Server Performance Analysis," 2024. Available at: https://github.com/Sam20220602/COMP901.git

### No Warranty
The dataset is provided "as-is," without any warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and non-infringement.

### Contributions
If you use this dataset in your research, we would appreciate it if you could share your results or cite this dataset in your publications.