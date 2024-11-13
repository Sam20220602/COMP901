using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Server
{
    private Socket serverSocket;
    private ConcurrentDictionary<int, ClientData> clients = new ConcurrentDictionary<int, ClientData>();
    private const int port = 5001;
    private const double threshold = 0.1; // 임계값: x, y, z의 차이값 총합
    private string logFilePath = "server_log.csv"; // 로그를 저장할 CSV 파일 경로
    private int totalErrors = 0;
    private static readonly object fileLock = new object(); // 파일 접근 동기화 객체
    private const int maxClients = 10; // 접속할 클라이언트 갯수를 하드코딩
    private int connectedClients = 0; // 현재 접속한 클라이언트 수
    private bool loggingStarted = false; // 로그 기록 시작 여부
    private Timer? logStartTimer; // 로그 기록 시작을 위한 타이머
    private Timer? logStopTimer; // 전역 변수로 설정
    private static int logDuration = (5 * 60 * 1000) + 10000; // 로그 활성화 시간을 5분 + 10초로 하드코딩

    public void Start()
    {
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));
        serverSocket.Listen(100);

        // CSV 파일에 헤더 추가
        if (!File.Exists(logFilePath))
        {
            lock (fileLock)
            {
                using (var sw = new StreamWriter(logFilePath, true))
                {
                    sw.WriteLine("Timestamp,UserID,CPUUsage,MemoryUsage,NetworkReceived,NetworkSent,DiskRead,DiskWrite,ProcessingTime,Mode,PositionDifference,PacketReturned,TotalErrors");
                }
            }
        }

        Console.WriteLine("Server started, waiting for clients...");
        StartAccept(null); // 비동기 클라이언트 수락 시작
    }

    private void StartAccept(SocketAsyncEventArgs acceptEventArg)
    {
        if (acceptEventArg == null)
        {
            acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptCompleted);
        }
        else
        {
            acceptEventArg.AcceptSocket = null; // 재사용 시 초기화
        }

        bool willRaiseEvent = serverSocket.AcceptAsync(acceptEventArg);
        if (!willRaiseEvent)
        {
            AcceptCompleted(this, acceptEventArg);
        }
    }

    private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            Console.WriteLine("Client connected.");
            Socket clientSocket = e.AcceptSocket;

            Interlocked.Increment(ref connectedClients); // 클라이언트 수 증가

            // 클라이언트가 모두 연결되면 10초 후 로그 기록 시작
            if (connectedClients == maxClients && !loggingStarted)
            {
                Console.WriteLine("All clients connected. Starting log after 5 seconds...");
                logStartTimer = new Timer(StartLogging, null, 10000, Timeout.Infinite); // 10초 후에 로그 기록 시작
            }

            // 클라이언트 소켓 처리
            ProcessClient(clientSocket);
        }

        // 다음 클라이언트 수락
        StartAccept(e);
    }

    private void StartLogging(object? state)
    {
        Console.WriteLine("Starting to log client data...");
        loggingStarted = true; // 로그 기록 시작을 확인

        // 로그 기능 비활성화를 위한 타이머 설정 (하드코딩된 시간 후에 로그 비활성화)
        logStopTimer = new Timer(StopLogging, null, logDuration, Timeout.Infinite);
    }

    private void StopLogging(object? state)
    {
        Console.WriteLine("Logging has stopped.");
        loggingStarted = false; // 로그 기능 비활성화

        // 프로그램 종료
        Console.WriteLine("Shutting down the server.");
        Environment.Exit(0); // 프로그램 종료
    }

    private void ProcessClient(Socket clientSocket)
    {
        SocketAsyncEventArgs receiveEventArgs = new SocketAsyncEventArgs();
        receiveEventArgs.SetBuffer(new byte[1024], 0, 1024);
        receiveEventArgs.UserToken = clientSocket;
        receiveEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ReceiveCompleted);

        // 비동기 수신 시작
        bool willRaiseEvent = clientSocket.ReceiveAsync(receiveEventArgs);
        if (!willRaiseEvent)
        {
            ReceiveCompleted(this, receiveEventArgs);
        }
    }

    private void ReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        Stopwatch stopwatch = new Stopwatch(); // 패킷 처리 시간 측정을 위한 스톱워치
        Socket clientSocket = e.UserToken as Socket;

        if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
        {
            try
            {
                stopwatch.Start(); // 처리 시간 측정 시작

                // 처음 4바이트에서 데이터의 길이 추출
                int dataLength = BitConverter.ToInt32(e.Buffer, 0);

                if (e.BytesTransferred > 40)
                {
                    Console.WriteLine($"Received data error");
                }

                // 데이터 수신 처리 (userId(4바이트), x(8바이트), y(8바이트), z(8바이트), mode(4바이트))
                int offset = 4; // 처음 4바이트는 데이터 길이
                int userId = BitConverter.ToInt32(e.Buffer, offset);
                offset += 4;
                double newX = BitConverter.ToDouble(e.Buffer, offset);
                offset += 8;
                double newY = BitConverter.ToDouble(e.Buffer, offset);
                offset += 8;
                double newZ = BitConverter.ToDouble(e.Buffer, offset);
                offset += 8;
                int mode = BitConverter.ToInt32(e.Buffer, offset);

                Console.WriteLine($"[{userId}]");

                if (!clients.ContainsKey(userId))
                {
                    clients[userId] = new ClientData(userId, newX, newY, newZ, mode);
                    Console.WriteLine($"New client {userId} registered with initial position: {newX}, {newY}, {newZ}, Mode: {mode}");
                }

                var clientData = clients[userId];

                int sentBytes = 0; // 실제로 보낸 패킷 크기를 기록하기 위한 변수
                double deltaX = 0, deltaY = 0, deltaZ = 0; // 포지션 차이 변수 미리 선언 및 초기화
                double totalDifference = 0; // 총 포지션 차이 변수 미리 선언 및 초기화
                bool packetReturned = false; // 패킷 리턴 여부 변수

                // 데드레커닝 모드에 따른 처리
                if (mode == 1) // 데드레커닝 활성화 (Mode 1)
                {
                    DateTime currentTime = DateTime.Now;
                    TimeSpan elapsedTime = currentTime - clientData.lastPacketTime;
                    clientData.lastPacketTime = currentTime;

                    double elapsedSeconds = elapsedTime.TotalSeconds; // 초 단위로 시간 차이 계산

                    // 위치 예측
                    double predictedX = clientData.x + clientData.velocity * elapsedSeconds;
                    double predictedY = clientData.y + clientData.velocity * elapsedSeconds;
                    double predictedZ = clientData.z + clientData.velocity * elapsedSeconds;

                    deltaX = Math.Abs(predictedX - newX);
                    deltaY = Math.Abs(predictedY - newY);
                    deltaZ = Math.Abs(predictedZ - newZ);

                    totalDifference = deltaX + deltaY + deltaZ;

                    Console.WriteLine($"Difference: {totalDifference}, Elapsed Time: {elapsedSeconds} seconds");
                    if (totalDifference > threshold)
                    {
                        Console.WriteLine($"Client {userId} out of bounds, sending update.");

                        // 'UPDATE' 문자열을 바이트로 변환
                        byte[] updateBytes = Encoding.UTF8.GetBytes("UPDATE");

                        // 좌표값을 각각 바이트로 변환 (x, y, z 각각 8바이트)
                        byte[] xBytes = BitConverter.GetBytes(newX);
                        byte[] yBytes = BitConverter.GetBytes(newY);
                        byte[] zBytes = BitConverter.GetBytes(newZ);

                        // 모든 바이트 배열을 하나로 결합
                        byte[] finalData = new byte[updateBytes.Length + xBytes.Length + yBytes.Length + zBytes.Length];
                        Array.Copy(updateBytes, 0, finalData, 0, updateBytes.Length);
                        Array.Copy(xBytes, 0, finalData, updateBytes.Length, xBytes.Length);
                        Array.Copy(yBytes, 0, finalData, updateBytes.Length + xBytes.Length, yBytes.Length);
                        Array.Copy(zBytes, 0, finalData, updateBytes.Length + xBytes.Length + yBytes.Length, zBytes.Length);

                        // 최종 데이터를 클라이언트에 전송
                        sentBytes = finalData.Length; // 실제 전송한 패킷 크기 기록
                        clientSocket.Send(finalData);
                    }
                }
                else // 데드레커닝 비활성화 (Mode 0)
                {
                    byte[] updateBytes = Encoding.UTF8.GetBytes("UPDATE");

                    byte[] xBytes = BitConverter.GetBytes(newX);
                    byte[] yBytes = BitConverter.GetBytes(newY);
                    byte[] zBytes = BitConverter.GetBytes(newZ);

                    byte[] finalData = new byte[updateBytes.Length + xBytes.Length + yBytes.Length + zBytes.Length];
                    Array.Copy(updateBytes, 0, finalData, 0, updateBytes.Length);
                    Array.Copy(xBytes, 0, finalData, updateBytes.Length, xBytes.Length);
                    Array.Copy(yBytes, 0, finalData, updateBytes.Length + xBytes.Length, yBytes.Length);
                    Array.Copy(zBytes, 0, finalData, updateBytes.Length + xBytes.Length + yBytes.Length, zBytes.Length);

                    sentBytes = finalData.Length; // 실제 전송한 패킷 크기 기록
                    clientSocket.Send(finalData);
                }

                clientData.UpdatePosition(newX, newY, newZ);

                stopwatch.Stop();
                long processingTime = stopwatch.ElapsedMilliseconds;

                totalDifference = deltaX + deltaY + deltaZ;
                packetReturned = totalDifference > threshold || mode == 0;

                if (loggingStarted) // 로그 기록이 시작되었을 때만 LogData 호출
                {
                    LogData(userId, e.BytesTransferred, sentBytes, mode, processingTime, totalDifference, packetReturned);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing data from client: {ex.Message}");
            }
            finally
            {
                bool willRaiseEvent = clientSocket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ReceiveCompleted(this, e);
                }
            }
        }
        else
        {
            Console.WriteLine($"Client disconnected or error occurred. Socket Error: {e.SocketError}");
            clientSocket.Close();
        }
    }

    private void LogData(int userId, int receivedBytes, int sentBytes, int mode, long processingTime, double totalDifference, bool packetReturned)
    {
        using (PerformanceCounter cpuCounter = new PerformanceCounter("Process", "% Processor Time", Process.GetCurrentProcess().ProcessName))
        {
            cpuCounter.NextValue();
            float cpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;

            long memoryUsage = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            int diskRead = 0;
            int diskWrite = 0;

            string positionDifference = totalDifference.ToString("F2");
            string packetReturnStatus = packetReturned ? "1" : "0";

            lock (fileLock)
            {
                using (var sw = new StreamWriter(logFilePath, true))
                {
                    sw.WriteLine($"{timestamp},{userId},{cpuUsage:F2},{memoryUsage},{receivedBytes},{sentBytes},{diskRead},{diskWrite},{processingTime},{mode},{positionDifference},{packetReturnStatus},{totalErrors}");
                }
            }
        }
    }

    class ClientData
    {
        public int userId;
        public double x, y, z;
        public double velocity = 1.0; // 기본 고정 속도
        public DateTime lastPacketTime; // 마지막 패킷의 시간
        public int mode; // 데드레커닝 모드 (0: 비활성화, 1: 활성화)

        public ClientData(int userId, double x, double y, double z, int mode)
        {
            this.userId = userId;
            this.x = x;
            this.y = y;
            this.z = z;
            this.mode = mode;
            this.lastPacketTime = DateTime.Now;
        }

        public void UpdatePosition(double newX, double newY, double newZ)
        {
            this.x = newX;
            this.y = newY;
            this.z = newZ;
        }
    }

    static void Main(string[] args)
    {
        Server server = new Server();
        server.Start();

        while (true)
        {
            System.Threading.Thread.Sleep(1000);
        }
    }
}
