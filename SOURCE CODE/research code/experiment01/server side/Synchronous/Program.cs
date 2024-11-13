using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class SyncServer
{
    private static PerformanceCounter cpuCounter;
    private static Process currentProcess = Process.GetCurrentProcess();
    private static long totalCpuUsage = 0;
    private static long totalMemoryUsage = 0;
    private static long totalSentBytes = 0; // 송신된 바이트 총량
    private static long totalReceivedBytes = 0; // 수신된 바이트 총량
    private static long totalResponseTime = 0;
    private static int totalErrors = 0;
    private static int sampleCount = 0;
    private static int responseCount = 0;
    private static int expectedClients = 0;
    private static int connectedClients = 0;
    private static object lockObject = new object();
    private static bool isRunning = false;

    // 1초 동안의 반응 시간과 응답 횟수를 저장하기 위한 변수
    private static long currentSecondResponseTime = 0;
    private static int currentSecondResponseCount = 0;

    static void Main(string[] args)
    {
        InitializePerformanceCounters();

        TcpListener server = new TcpListener(IPAddress.Any, 12345);
        server.Start();
        Console.WriteLine("서버 시작!");

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Thread thread = new Thread(() => HandleClient(client));
            thread.Start();
        }
    }

    static void InitializePerformanceCounters()
    {
        cpuCounter = new PerformanceCounter("Process", "% Processor Time", currentProcess.ProcessName);
    }

    static void MonitorPerformance()
    {
        try
        {
            using (StreamWriter writer = new StreamWriter("performance_log.csv"))
            {
                writer.WriteLine("Time, CPU(%), Memory(MB), Sent(Bytes), Received(Bytes), Errors, Average Response Time(ms)");

                while (isRunning)
                {
                    float cpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;
                    float memoryUsage = currentProcess.WorkingSet64 / (1024 * 1024); // MB 단위로 변환

                    // 현재 1초 동안의 평균 반응 시간 계산
                    float averageCurrentSecondResponseTime = currentSecondResponseCount > 0
                        ? currentSecondResponseTime / (float)currentSecondResponseCount
                        : 0;

                    lock (lockObject)
                    {
                        string log = $"{DateTime.Now}, {cpuUsage:F2}, {memoryUsage:F2}, {totalSentBytes}, {totalReceivedBytes}, {totalErrors}, {averageCurrentSecondResponseTime:F2}";
                        Console.WriteLine(log);
                        writer.WriteLine(log);
                        writer.Flush();

                        totalCpuUsage += (long)cpuUsage;
                        totalMemoryUsage += (long)memoryUsage;
                        sampleCount++;

                        // 1초 동안의 반응 시간 및 응답 횟수를 초기화
                        currentSecondResponseTime = 0;
                        currentSecondResponseCount = 0;
                    }

                    Thread.Sleep(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"성능 모니터링 중 오류 발생: {ex.Message}");
        }
    }

    static void AnalyzeAndLogFinalResults()
    {
        // 초기 대기 상태, 모든 클라이언트가 연결될 때까지 대기
        while (!isRunning)
        {
            Thread.Sleep(500); // 0.5초마다 확인
        }

        // 클라이언트가 모두 연결된 후 60초 * 31 대기
        Thread.Sleep(60000 * 31);
        // Thread.Sleep(10000); // 10초

        lock (lockObject)
        {
            isRunning = false; // 모니터링 중지

            float averageCpuUsage = totalCpuUsage / (float)sampleCount;
            float averageMemoryUsage = totalMemoryUsage / (float)sampleCount;
            float averageResponseTime = responseCount > 0 ? totalResponseTime / (float)responseCount : 0;

            using (StreamWriter writer = new StreamWriter("final_analysis.csv"))
            {
                writer.WriteLine("Final Analysis");
                writer.WriteLine($"Total Samples: {sampleCount}");
                writer.WriteLine($"Average CPU Usage (%): {averageCpuUsage:F2}");
                writer.WriteLine($"Average Memory Usage (MB): {averageMemoryUsage:F2}");
                writer.WriteLine($"Total Sent Bytes: {totalSentBytes}");
                writer.WriteLine($"Total Received Bytes: {totalReceivedBytes}");
                writer.WriteLine($"Total Errors: {totalErrors}");
                writer.WriteLine($"Average Response Time (ms): {averageResponseTime:F2}");
            }

            Console.WriteLine("최종 분석 결과가 기록되었습니다.");
        }

        Environment.Exit(0);
    }

    static void HandleClient(TcpClient client)
    {
        if (client == null)
        {
            Console.WriteLine("클라이언트가 null 상태입니다.");
            return;
        }

        NetworkStream stream = null;

        try
        {
            stream = client.GetStream();
            byte[] buffer = new byte[1024];

            // 클라이언트 수 수신
            if (expectedClients == 0)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                expectedClients = int.Parse(Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim());
                Console.WriteLine($"예상되는 클라이언트 수: {expectedClients}");
                stream.Write(Encoding.UTF8.GetBytes("클라이언트 수 확인"), 0, "클라이언트 수 확인".Length);
                return;
            }

            lock (lockObject)
            {
                connectedClients++;
                Console.WriteLine($"클라이언트 연결됨! 현재 연결된 클라이언트 수: {connectedClients}");
            }

            // 모든 클라이언트가 연결되면 모니터링 시작
            if (connectedClients == expectedClients && !isRunning)
            {
                Console.WriteLine("모든 클라이언트가 연결되었습니다. 성능 측정을 시작합니다.");
                isRunning = true;
                new Thread(MonitorPerformance).Start();
                new Thread(AnalyzeAndLogFinalResults).Start(); // 모든 클라이언트가 연결된 후 분석 시작
            }

            while (client.Connected && stream != null)
            {
                if (stream.DataAvailable)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        lock (lockObject)
                        {
                            totalReceivedBytes += bytesRead; // 수신된 바이트 누적
                        }

                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Console.WriteLine($"[{connectedClients}]로부터 받은 데이터: {receivedData}");

                        string response = $"서버에서 받은 메시지: {receivedData}";
                        byte[] responseData = Encoding.UTF8.GetBytes(response);
                        stream.Write(responseData, 0, responseData.Length);
                        stream.Flush();

                        lock (lockObject)
                        {
                            totalSentBytes += responseData.Length; // 송신된 바이트 누적
                        }

                        stopwatch.Stop();
                        lock (lockObject)
                        {
                            totalResponseTime += stopwatch.ElapsedMilliseconds;
                            responseCount++;

                            // 1초 동안의 반응 시간 및 응답 횟수 업데이트
                            currentSecondResponseTime += stopwatch.ElapsedMilliseconds;
                            currentSecondResponseCount++;
                        }
                    }
                }

                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"오류 발생: {ex.Message}");

            lock (lockObject)
            {
                totalErrors++;
            }
        }
        finally
        {
            if (stream != null)
            {
                stream.Close();
            }

            if (client != null)
            {
                client.Close();
            }

            lock (lockObject)
            {
                // connectedClients가 음수로 감소하지 않도록 수정
                if (connectedClients > 0)
                {
                    connectedClients--;
                    Console.WriteLine($"클라이언트 연결 종료. 현재 연결된 클라이언트 수: {connectedClients}");
                }
                else
                {
                    Console.WriteLine("클라이언트 연결 종료 시 연결된 클라이언트 수가 이미 0입니다.");
                }
            }
        }
    }
}
