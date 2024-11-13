using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

class Client
{
    private static ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>(); // 로그 큐
    private static readonly SemaphoreSlim logSemaphore = new SemaphoreSlim(1, 1); // 로그 파일 쓰기 동기화용 세마포어
    private static bool isWritingLogs = false; // 로그 쓰기 플래그
    private static string csvFilePath = "Latency_Log.csv"; // 로그 파일 경로
    static int numClients = 0;

    // 실시간 패킷 크기 및 중요도 조정을 위한 플래그
    private static bool isCriticalEvent = false; // 전투 중인지 여부를 판단하는 플래그
    private static bool isMovementHeavy = true; // 캐릭터가 이동 중인지 여부를 판단하는 플래그

    // 새로운 쓰레드로 키보드 입력을 처리하는 메서드 추가
    static void MonitorUserInput()
    {
        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.C)
            {
                isCriticalEvent = !isCriticalEvent;
                Console.WriteLine(isCriticalEvent ? "전투 모드가 활성화되었습니다." : "전투 모드가 비활성화되었습니다.");
            }
            else if (key.Key == ConsoleKey.M)
            {
                isMovementHeavy = !isMovementHeavy;
                Console.WriteLine(isMovementHeavy ? "이동 모드가 활성화되었습니다." : "이동 모드가 비활성화되었습니다.");
            }
        }
    }

    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Client <number_of_clients> <mode>");
            Console.WriteLine("Example: Client 100 adaptive");
            return;
        }

        numClients = int.Parse(args[0]); // 첫 번째 인수: 클라이언트 수
        string packetType = args[1]; // 두 번째 인수: "adaptive" 또는 "fixed"

        string serverIp = "127.0.0.1";
        int port = 5001;
        int waitTimeBeforeSendingPackets = 5000; // 클라이언트가 패킷을 전송하기 전 대기 시간 (밀리초)

        // 키보드 입력을 처리할 쓰레드 시작
        Task.Run(() => MonitorUserInput());

        // 클라이언트별로 로그 파일 헤더 추가
        await InitializeLogFile();

        var clientTasks = new List<Task>();
        for (int i = 1; i <= numClients; i++)
        {
            string userId = i.ToString(); // 유저 아이디를 숫자로 설정 (1, 2, 3, ...)
            clientTasks.Add(Task.Run(() => SimulateClient(serverIp, port, packetType, userId, waitTimeBeforeSendingPackets)));
        }

        // 모든 클라이언트 작업 완료를 기다리는 동시에 로그를 비동기적으로 쓰기 시작
        var logTask = Task.Run(() => ProcessLogQueueAsync());
        await Task.WhenAll(clientTasks);
        isWritingLogs = false; // 로그 쓰기 종료

        // 로그 처리 완료를 기다림
        await logTask;

        Console.WriteLine("모든 클라이언트 실행 및 로그 기록 완료");
    }

    // 로그 파일 초기화
    static async Task InitializeLogFile()
    {
        await logSemaphore.WaitAsync();
        try
        {
            if (!File.Exists(csvFilePath))
            {
                using (var writer = new StreamWriter(csvFilePath, true))
                {
                    await writer.WriteLineAsync("Timestamp,UserID,Importance,Latency(ms)");
                }
            }
        }
        finally
        {
            logSemaphore.Release();
        }
    }

    // 클라이언트 시뮬레이션
    static async Task SimulateClient(string serverIp, int port, string packetType, string userId, int waitTimeBeforeSendingPackets)
    {
        List<PacketType> packetTypes = packetType == "adaptive" ? GetAdaptivePacketTypes() : GetFixedPacketType();

        using (var client = new TcpClient())
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // 서버 연결 시도
                await client.ConnectAsync(serverIp, port);
                Console.WriteLine($"클라이언트 {userId}가 서버에 연결되었습니다.");
                var stream = client.GetStream();
                await Task.Delay(waitTimeBeforeSendingPackets);
                Console.WriteLine($"클라이언트 {userId}: 예상 대기 시간 이후 패킷 전송을 시작합니다.");

                bool measuring = true;
                Random rand = new Random();
                while (measuring)
                {
                    // 빈도 기반으로 패킷 선택
                    var weightedList = packetTypes.SelectMany(pt => Enumerable.Repeat(pt, (int)(pt.Frequency * 100))).ToList();
                    var packet = weightedList[rand.Next(weightedList.Count)];

                    // 패킷 크기 및 중요도 조정
                    AdjustPacketSizeAndImportance(ref packet);

                    Stopwatch stopwatch = Stopwatch.StartNew();

                    string message = $"{packet.Size},{packetType},{packet.Importance},{userId}|";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(data, 0, data.Length);

                    Console.WriteLine($"Sending packet of size {packet.Size} with importance {packet.Importance}");

                    byte[] buffer = new byte[128];
                    int totalBytesRead = 0;

                    while (totalBytesRead < 128)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, totalBytesRead, 128 - totalBytesRead);
                        if (bytesRead == 0)
                        {
                            Console.WriteLine("서버 연결 종료");
                            measuring = false;
                            break;
                        }
                        totalBytesRead += bytesRead;
                    }

                    if (totalBytesRead == 128)
                    {
                        stopwatch.Stop();
                        double latency = stopwatch.Elapsed.TotalMilliseconds;

                        string logEntry = $"{DateTime.Now},{userId},{packet.Importance},{latency:F2}";
                        logQueue.Enqueue(logEntry);
                    }

                    // 패킷 크기 복원
                    packet.ResetSize();

                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"클라이언트 {userId} 연결 실패: {ex.Message}");
            }
        }
    }

    // 패킷 크기 및 중요도를 실시간으로 조정하는 함수
    static void AdjustPacketSizeAndImportance(ref PacketType packet)
    {
        if (isCriticalEvent)
        {
            // 전투 중일 때 "Attack", "Skill Use", "Defense" 타입만 크기를 늘리고 중요도를 높임
            if (packet.Name == "Attack" || packet.Name == "Skill Use" || packet.Name == "Defense")
            {
                packet.Size = Math.Max(packet.Size * 2, 64); // 패킷 크기를 최소 64로 증가
                packet.Importance = Math.Min(packet.Importance + 1, 5); // 중요도를 최대 5로 설정
            }
        }
        else if (isMovementHeavy)
        {
            // 이동 중일 때 패킷 크기를 줄임
            // packet.Size = Math.Min(packet.Size / 2, 16); // 패킷 크기를 최소 16으로 설정
            // packet.Importance = Math.Max(packet.Importance - 1, 1); // 중요도를 최소 1로 설정

            // 이동 중일 때 패킷 크기와 중요도를 원래 값으로 복원
            packet.ResetSize(); // 원래 크기로 복원
            packet.ResetImportance(); // 원래 중요도로 복원
        }
    }
    // 비동기적으로 로그 큐를 처리하여 파일에 기록
    static async Task ProcessLogQueueAsync()
    {
        isWritingLogs = true;

        while (isWritingLogs || !logQueue.IsEmpty)
        {
            await Task.Delay(500); // 0.5초 간격으로 로그 처리

            var logEntries = new List<string>();
            while (logQueue.TryDequeue(out string logEntry))
            {
                logEntries.Add(logEntry); // 큐에서 데이터를 미리 모아둠
            }

            if (logEntries.Count > 0)
            {
                await logSemaphore.WaitAsync(); // 파일 접근 동기화
                try
                {
                    using (var writer = new StreamWriter(csvFilePath, true))
                    {
                        foreach (var entry in logEntries)
                        {
                            await writer.WriteLineAsync(entry); // 미리 모아둔 로그를 한번에 기록
                        }
                    }
                }
                finally
                {
                    logSemaphore.Release();
                }
            }
        }
    }

    // 적응형 패킷 유형 목록
    static List<PacketType> GetAdaptivePacketTypes()
    {
        return new List<PacketType>
        {
            new PacketType("Move", 16, 1, 10),   // 이동 시 작은 패킷 (128 bytes -> 16 bytes)
            new PacketType("Attack", 64, 2, 5),  // 전투 시 큰 패킷 (512 bytes -> 64 bytes)
            new PacketType("Skill Use", 32, 2, 5),  // 스킬 사용 (256 bytes -> 32 bytes)
            new PacketType("Defense", 32, 2, 5),   // 방어 시 패킷 크기
            new PacketType("Chat", 128, 3, 1)   // 채팅 패킷 (큰 메시지, 중요도 낮음)
        };
    }

    // 고정형 패킷 유형 목록
    static List<PacketType> GetFixedPacketType()
    {
        return new List<PacketType> { new PacketType("Fixed Packet", 128, 1, 1) };
    }
}

class PacketType
{
    public string Name { get; }
    public int Size { get; set; }
    public int OriginalSize { get; } // 초기 크기 저장
    public int Importance { get; set; }
    public int OriginalImportance { get; } // 초기 중요도 저장
    public double Frequency { get; }

    public PacketType(string name, int size, int importance, double frequency)
    {
        Name = name;
        Size = size;
        OriginalSize = size; // 초기 크기 저장
        Importance = importance;
        OriginalImportance = importance; // 초기 중요도 저장
        Frequency = frequency;
    }

    // 패킷 크기를 초기화하는 메소드 추가
    public void ResetSize()
    {
        Size = OriginalSize; // 패킷 크기를 초기화
    }

    // 패킷 중요도를 초기화하는 메소드
    public void ResetImportance()
    {
        Importance = OriginalImportance; // 패킷 중요도를 초기화
    }
}