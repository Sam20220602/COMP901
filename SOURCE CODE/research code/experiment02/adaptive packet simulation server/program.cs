using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Server
{
    public static bool usePriorityQueue = true; // 전역 변수로 설정

    private static Socket _listenerSocket = null!;
    private static bool _isRunning = true;
    public static int totalErrors = 0;  // 로그에서 접근할 수 있도록 public으로 변경
    public static List<string> logBuffer = new List<string>(); // 로그 데이터를 저장할 버퍼
    private static ConcurrentDictionary<string, ClientData> _connectedClients = new ConcurrentDictionary<string, ClientData>();
    private static int maxClients = 10; // 최대 클라이언트 수
    // private static int measureDuration = 30; // 측정 시간 30초
    private static int measureDuration = (60 * 5) + 10; // 측정 시간 5분 + 10초
    private static AdaptiveServerFixedNetworkDelay adaptiveServer = new AdaptiveServerFixedNetworkDelay();
    private static FixedServer fixedServer = new FixedServer();
    private static readonly string logFilePath = "server_log.csv";
    private static readonly string metricsFilePath = "server_metrics.csv";
    private static ManualResetEvent allDone = new ManualResetEvent(false);

    private static Process currentProcess = Process.GetCurrentProcess(); // 현재 실행 중인 프로세스
    private static PerformanceCounter cpuCounter = null!;
    private static PerformanceCounter memCounter = null!;
    private static Stopwatch measureStopwatch = null!;

    private static List<float> cpuUsages = new List<float>();
    private static List<float> availableMemories = new List<float>();
    private static List<double> responseTimes = new List<double>();
    private static List<double> processingTimes = new List<double>();
    private static List<float> cpuUsageSamples = new List<float>();

    private static Timer? measurementTimer;
    private static bool isMeasuring = false;

    private static PriorityPacketQueue priorityQueue = new PriorityPacketQueue(); // 우선순위 큐

    static void Main(string[] args)
    {
        ThreadPool.SetMinThreads(100, 100);

        InitializeLogFile();

        cpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
        memCounter = new PerformanceCounter("Process", "Working Set - Private", Process.GetCurrentProcess().ProcessName);

        cpuCounter.NextValue();
        Thread.Sleep(1000);

        int port = 5001;
        _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            _listenerSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            _listenerSocket.Listen(2000);
            Console.WriteLine($"서버가 {port} 포트에서 IOCP 방식으로 시작되었습니다.");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                ShutdownServer();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine("서버 종료 중... 메트릭 및 로그 저장 중...");
                SaveMetricsToFile();
                SaveLogBufferToFile(); // 서버 종료 시점에 로그를 파일에 기록
            };

            Task.Run(() => ProcessQueuedPackets()); // 패킷 큐 처리 시작

            _listenerSocket.BeginAccept(new AsyncCallback(AcceptCallback), _listenerSocket);

            while (_isRunning)
            {
                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버 초기화 중 오류 발생: {ex.Message}");
        }
    }

    private static void AcceptCallback(IAsyncResult ar)
    {
        Socket listener = (Socket)ar.AsyncState!;
        Socket handler = null;

        try
        {
            handler = listener.EndAccept(ar);
            Console.WriteLine("새 클라이언트 연결 성공!");

            listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);

            StateObject state = new StateObject { workSocket = handler };
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("소켓이 닫혔습니다.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AcceptCallback 중 오류 발생: {ex.Message}");
            handler?.Close();
        }
    }
    private static async void ReadCallback(IAsyncResult ar)
    {
        StateObject state = (StateObject)ar.AsyncState!;
        Socket handler = state.workSocket;

        try
        {
            // 패킷 수신 시작 시간 기록
            var requestStartTime = DateTime.UtcNow;

            int bytesRead = handler.EndReceive(ar);

            if (bytesRead > 0)
            {
                state.sb.Append(Encoding.UTF8.GetString(state.buffer, 0, bytesRead));

                string[] messages = state.sb.ToString().Split('|');

                foreach (string msg in messages)
                {
                    if (string.IsNullOrWhiteSpace(msg))
                        continue;

                    string[] packetInfo = msg.Split(',');

                    if (packetInfo.Length != 4)  // 중요도 포함
                    {
                        Console.WriteLine($"Invalid packet format received: {msg}");
                        totalErrors++;
                        continue;
                    }
                    /*
                                        if (!int.TryParse(packetInfo[0], out int packetSize) ||
                                            !double.TryParse(packetInfo[3], out double importance))  // 중요도 파싱 추가
                                        {
                                            Console.WriteLine($"Error parsing packet size or importance: {msg}");
                                            totalErrors++;
                                            continue;
                                        }*/

                    if (!int.TryParse(packetInfo[0], out int packetSize) ||
                    !int.TryParse(packetInfo[2], out int importance)) // 중요도 파싱 추가
                    {
                        Console.WriteLine($"Error parsing packet size or importance: {packetInfo[0]}, {packetInfo[2]}");
                        totalErrors++;
                        continue;
                    }

                    string serverType = packetInfo[1];
                    string userId = packetInfo[3];

                    _connectedClients.TryAdd(userId, new ClientData(handler));

                    if (_connectedClients.Count == maxClients && !isMeasuring)
                    {
                        StartMeasurementAsync();
                    }

                    if (_connectedClients.TryGetValue(userId, out var clientData))
                    {
                        clientData.NetworkReceived = packetSize;
                    }

                    // 패킷 처리 로직
                    PacketTask packetTask = new PacketTask(packetSize, importance, serverType);  // 중요도 포함
                    priorityQueue.Enqueue(packetTask);

                    // 응답 패킷 송신
                    string responseMessage = "Server Response";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                    Array.Resize(ref responseBytes, 128);  // 송신 패킷 크기 설정

                    await handler.SendAsync(new ArraySegment<byte>(responseBytes), SocketFlags.None);

                    if (_connectedClients.TryGetValue(userId, out clientData))
                    {
                        clientData.NetworkSent = responseBytes.Length;  // 송신한 패킷 크기 기록
                    }

                    // 패킷 처리 종료 시간 기록
                    var requestEndTime = DateTime.UtcNow;
                    var processingTime = (requestEndTime - requestStartTime).TotalMilliseconds;  // 처리 시간 계산

                    // CPU 및 메모리 사용량 가져오기
                    float currentCpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;
                    float smoothedCpuUsage = GetSmoothedCpuUsage(currentCpuUsage);
                    int memoryUsage = (int)memCounter.NextValue();

                    // serverType 값에 따라 마지막 인자 설정
                    int mode = (serverType == "adaptive") ? 1 : 0;  // 'adaptive'일 때 1, 그 외에는 0

                    // 로그 기록
                    LogToBuffer(userId, smoothedCpuUsage, memoryUsage, clientData.NetworkSent, clientData.NetworkReceived, importance, processingTime, 1);
                }

                state.sb.Clear();
                if (!string.IsNullOrEmpty(messages[^1]))
                {
                    state.sb.Append(messages[^1]);
                }

                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
            }
            else
            {
                handler.Close();
            }
        }
        catch (Exception ex)
        {
            totalErrors++;
            Console.WriteLine($"Error processing client: {ex.Message}");
            handler.Close();
        }
    }



    private static async Task ProcessQueuedPackets()
    {
        while (_isRunning)
        {
            var packetTask = await priorityQueue.DequeueAsync();

            if (packetTask != null)
            {
                if (packetTask.ServerType == "adaptive")
                {
                    await adaptiveServer.ProcessPacket(packetTask.PacketSize, packetTask.Importance);
                }
                else
                {
                    await fixedServer.ProcessPacket(packetTask.PacketSize, packetTask.Importance);
                }
            }
        }
    }

    // 패킷 중요도 계산
    private static double GetPacketImportance(int packetSize)
    {
        // 패킷 사이즈에 따라 중요도를 계산 (패킷이 작을수록 중요도가 높음)
        if (packetSize <= 32) return 5.0;  // 작은 패킷은 중요도 높음
        if (packetSize <= 64) return 3.0;  // 중간 크기의 패킷은 중간
        return 1.0;  // 큰 패킷은 중요도 낮음
    }

    // 로그 데이터를 버퍼에 저장하는 함수
    public static void LogToBuffer(string playerId, float cpuUsage, int memoryUsage, int netSent, int netReceived, double importance, double processingTime, int mode)
    {
        var timestamp = DateTime.UtcNow.ToString("o");
        string logEntry = $"{timestamp},{playerId},{cpuUsage},{memoryUsage},{netSent},{netReceived},{importance},{processingTime},{mode},{totalErrors}";

        // 로그 출력
        Console.WriteLine($"Logging: {logEntry}");

        // 버퍼에 저장
        logBuffer.Add(logEntry);
    }

    // 서버 종료 시 버퍼에 저장된 로그를 파일에 기록하는 함수
    private static void SaveLogBufferToFile()
    {
        try
        {
            using (var writer = new StreamWriter(logFilePath, true))
            {
                foreach (var logEntry in logBuffer)
                {
                    writer.WriteLine(logEntry);
                }
            }
            Console.WriteLine("버퍼에 저장된 로그 파일에 기록 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"로그 파일 저장 중 오류 발생: {ex.Message}");
        }
    }

    private static void StartMeasurementAsync()
    {
        if (isMeasuring) return;
        isMeasuring = true;

        measureStopwatch = Stopwatch.StartNew();
        Console.WriteLine("측정 시작...");

        measurementTimer = new Timer(LogPerformanceMetrics, null, 0, 1000);
    }

    private static void StopMeasurement()
    {
        measurementTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        measurementTimer?.Dispose();
        measureStopwatch.Stop();
        isMeasuring = false;
        Console.WriteLine("측정 종료. 메트릭을 파일에 저장 중...");
        SaveMetricsToFile();
        ShutdownServer();
    }

    private static void LogPerformanceMetrics(object? state)
    {
        try
        {
            float currentCpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;
            float smoothedCpuUsage = GetSmoothedCpuUsage(currentCpuUsage);

            float availableMemory = memCounter.NextValue();

            cpuUsages.Add(smoothedCpuUsage);
            availableMemories.Add(availableMemory);

            if (measureStopwatch.Elapsed.TotalSeconds >= measureDuration)
            {
                Console.WriteLine("측정 종료 조건 도달");
                StopMeasurement();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"측정 중 오류 발생: {ex.Message}");
        }
    }

    private static float GetSmoothedCpuUsage(float currentCpuUsage)
    {
        cpuUsageSamples.Add(currentCpuUsage);
        if (cpuUsageSamples.Count > 5)
        {
            cpuUsageSamples.RemoveAt(0);
        }
        return cpuUsageSamples.Average();
    }

    private static void InitializeLogFile()
    {
        try
        {
            using (var writer = new StreamWriter(logFilePath, false))  // overwrite if file exists
            {
                writer.WriteLine("Timestamp,PlayerID,CPUUsage,MemoryUsage,NetworkSent,NetworkReceived,Importance,ProcessingTime,Mode,TotalErrors");
                writer.Flush();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"로그 파일 초기화 중 오류 발생: {ex.Message}");
        }
    }

    private static void SaveMetricsToFile()
    {
        try
        {
            int totalNetworkSent = 0;
            int totalNetworkReceived = 0;

            foreach (var clientData in _connectedClients.Values)
            {
                totalNetworkSent += clientData.NetworkSent;
                totalNetworkReceived += clientData.NetworkReceived;
            }

            using (var writer = new StreamWriter(metricsFilePath))
            {
                writer.WriteLine("Metric,Value,Unit");
                writer.WriteLine($"Total Samples,{cpuUsages.Count},");
                writer.WriteLine($"Average CPU Usage,{GetAverage(cpuUsages):F2},%");
                writer.WriteLine($"Average Available Memory,{GetAverage(availableMemories):F2},MB");
                writer.WriteLine($"Total Network Sent,{totalNetworkSent},Packets");
                writer.WriteLine($"Total Network Received,{totalNetworkReceived},Packets");
                writer.WriteLine($"Total Network Traffic,{totalNetworkSent + totalNetworkReceived},Packets");
                writer.WriteLine($"Average Processing Time,{GetAverage(responseTimes):F2},ms");
                writer.WriteLine($"Total Errors,{totalErrors},");
            }
            Console.WriteLine("메트릭 파일에 저장 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"메트릭 저장 중 오류 발생: {ex.Message}");
        }
    }

    private static double GetAverage(List<float> values)
    {
        if (values.Count == 0) return 0;
        return values.Average();
    }

    private static double GetAverage(List<double> values)
    {
        if (values.Count == 0) return 0;
        return values.Average();
    }

    private static void ShutdownServer()
    {
        _isRunning = false;
        _listenerSocket.Close();
        SaveMetricsToFile();
        SaveLogBufferToFile(); // 로그를 파일에 기록
        Console.WriteLine("서버가 정상적으로 종료되었습니다.");
        Environment.Exit(0);
    }

    public class StateObject
    {
        public Socket workSocket = null!;
        public const int BufferSize = 128;
        public byte[] buffer = new byte[BufferSize];
        public StringBuilder sb = new StringBuilder();
    }

    private class ClientData
    {
        public Socket Socket { get; private set; }
        public int NetworkSent { get; set; }
        public int NetworkReceived { get; set; }

        public ClientData(Socket socket)
        {
            Socket = socket;
            NetworkSent = 0;
            NetworkReceived = 0;
        }
    }
}

// 우선순위 큐 클래스
class PriorityPacketQueue
{
    private readonly ConcurrentQueue<PacketTask> generalQueue = new ConcurrentQueue<PacketTask>(); // 일반 큐 (중요도를 사용하지 않을 때)
    private readonly ConcurrentQueue<PacketTask> priorityQueue1 = new ConcurrentQueue<PacketTask>(); // 중요도 1
    private readonly ConcurrentQueue<PacketTask> priorityQueue2 = new ConcurrentQueue<PacketTask>(); // 중요도 2
    private readonly ConcurrentQueue<PacketTask> priorityQueue3 = new ConcurrentQueue<PacketTask>(); // 중요도 3
    private readonly ConcurrentQueue<PacketTask> priorityQueue4 = new ConcurrentQueue<PacketTask>(); // 중요도 4
    private readonly ConcurrentQueue<PacketTask> priorityQueue5 = new ConcurrentQueue<PacketTask>(); // 중요도 5
    private readonly SemaphoreSlim queueSemaphore = new SemaphoreSlim(0);

    // 패킷을 큐에 추가
    public void Enqueue(PacketTask packetTask)
    {
        if (Server.usePriorityQueue) // 전역 변수 참조
        {
            // 중요도 큐에 추가
            switch (packetTask.Importance)
            {
                case 1:
                    priorityQueue1.Enqueue(packetTask); // 중요도 1
                    break;
                case 2:
                    priorityQueue2.Enqueue(packetTask); // 중요도 2
                    break;
                case 3:
                    priorityQueue3.Enqueue(packetTask); // 중요도 3
                    break;
                case 4:
                    priorityQueue4.Enqueue(packetTask); // 중요도 4
                    break;
                case 5:
                    priorityQueue5.Enqueue(packetTask); // 중요도 5
                    break;
                default:
                    Console.WriteLine("Unknown Importance Level");
                    break;
            }
        }
        else
        {
            // 일반 큐에 추가
            generalQueue.Enqueue(packetTask);
        }

        queueSemaphore.Release();
    }

    // 패킷을 큐에서 가져오기
    public async Task<PacketTask?> DequeueAsync()
    {
        await queueSemaphore.WaitAsync();

        if (Server.usePriorityQueue) // 전역 변수 참조
        {
            // 중요도 큐에서 패킷 가져오기 (우선순위 높은 큐부터)
            if (priorityQueue5.TryDequeue(out var task5)) return task5;  // 중요도 5
            if (priorityQueue4.TryDequeue(out var task4)) return task4;  // 중요도 4
            if (priorityQueue3.TryDequeue(out var task3)) return task3;  // 중요도 3
            if (priorityQueue2.TryDequeue(out var task2)) return task2;  // 중요도 2
            if (priorityQueue1.TryDequeue(out var task1)) return task1;  // 중요도 1
        }
        else
        {
            // 일반 큐에서 패킷 가져오기
            if (generalQueue.TryDequeue(out var generalTask)) return generalTask;
        }

        return null; // 큐가 비어 있을 때
    }
}




// 패킷 작업 클래스
class PacketTask
{
    public int PacketSize { get; }
    public double Importance { get; }
    public string ServerType { get; }

    public PacketTask(int packetSize, double importance, string serverType)
    {
        PacketSize = packetSize;
        Importance = importance;
        ServerType = serverType;
    }
}

abstract class ServerBase
{
    protected object lockObject = new object();
    protected double totalBandwidth = 0;
    protected List<double> processingTimes = new List<double>();
    protected List<float> cpuUsages = new List<float>();
    protected List<int> memoryUsages = new List<int>();

    protected static PerformanceCounter cpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName);
    protected static PerformanceCounter memCounter = new PerformanceCounter("Process", "Working Set - Private", Process.GetCurrentProcess().ProcessName);

    private static List<float> cpuUsageSamples = new List<float>();

    protected static float GetSmoothedCpuUsage(float currentCpuUsage)
    {
        cpuUsageSamples.Add(currentCpuUsage);
        if (cpuUsageSamples.Count > 5)
        {
            cpuUsageSamples.RemoveAt(0);
        }
        return cpuUsageSamples.Average();
    }

    public abstract Task ProcessPacket(int packetSize, double importance);

    public (double avgProcessingTime, double avgCpuUsage, double avgMemoryUsage) GetMetrics()
    {
        lock (lockObject)
        {
            double avgProcessingTime = processingTimes.Any() ? processingTimes.Average() : 0;
            double avgCpuUsage = cpuUsages.Any() ? cpuUsages.Average() : 0;
            double avgMemoryUsage = memoryUsages.Any() ? memoryUsages.Average() : 0;
            return (avgProcessingTime, avgCpuUsage, avgMemoryUsage);
        }
    }
}

class AdaptiveServerFixedNetworkDelay : ServerBase
{
    public override async Task ProcessPacket(int packetSize, double importance)
    {
        var tasks = new List<Task>();

        tasks.Add(Task.Run(() =>
        {
            lock (lockObject)
            {
                totalBandwidth += packetSize;
            }

            var processingTime = (DateTime.UtcNow - DateTime.UtcNow).TotalSeconds;

            lock (lockObject)
            {
                processingTimes.Add(processingTime);
                cpuUsages.Add(GetSmoothedCpuUsage(cpuCounter.NextValue() / Environment.ProcessorCount));
                memoryUsages.Add((int)memCounter.NextValue());
            }
        }));

        await Task.WhenAll(tasks);
    }
}

class FixedServer : ServerBase
{
    public override async Task ProcessPacket(int packetSize, double importance)
    {
        var tasks = new List<Task>();

        tasks.Add(Task.Run(() =>
        {
            lock (lockObject)
            {
                totalBandwidth += packetSize;
            }

            var processingTime = (DateTime.UtcNow - DateTime.UtcNow).TotalSeconds;

            lock (lockObject)
            {
                processingTimes.Add(processingTime);
                cpuUsages.Add(GetSmoothedCpuUsage(cpuCounter.NextValue() / Environment.ProcessorCount));
                memoryUsages.Add((int)memCounter.NextValue());
            }
        }));

        await Task.WhenAll(tasks);
    }
}
