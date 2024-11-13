using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class AsyncServer
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

    static async Task Main(string[] args)
    {
        InitializePerformanceCounters();

        TcpListener server = new TcpListener(IPAddress.Any, 12345);
        server.Start();
        Console.WriteLine("서버 시작!");

        while (true)
        {
            TcpClient client = await server.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    static void InitializePerformanceCounters()
    {
        cpuCounter = new PerformanceCounter("Process", "% Processor Time", currentProcess.ProcessName);
    }

    static async Task MonitorPerformance()
    {
        try
        {
            using (StreamWriter writer = new StreamWriter("performance_log.csv"))
            {
                writer.WriteLine("Time, CPU(%), Memory(MB), Sent(Bytes), Received(Bytes), Errors, Average Response Time(ms)");

                while (true)
                {
                    if (!isRunning)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

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

                    await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"성능 모니터링 중 오류 발생: {ex.Message}");
        }
    }

    static async Task AnalyzeAndLogFinalResults()
    {
        while (!isRunning)
        {
            await Task.Delay(500);
        }

        await Task.Delay(60000 * 31); // 31분
        // await Task.Delay(10000); // 10초

        lock (lockObject)
        {
            isRunning = false;

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

    static async Task HandleClientAsync(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        try
        {
            if (expectedClients == 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                expectedClients = int.Parse(Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim());
                Console.WriteLine($"예상되는 클라이언트 수: {expectedClients}");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("클라이언트 수 확인"), 0, "클라이언트 수 확인".Length);
                return;
            }

            lock (lockObject)
            {
                connectedClients++;
                Console.WriteLine($"클라이언트 연결됨! 현재 연결된 클라이언트 수: {connectedClients}");
            }

            if (connectedClients == expectedClients && !isRunning)
            {
                Console.WriteLine("모든 클라이언트가 연결되었습니다. 성능 측정을 시작합니다.");
                isRunning = true; // 모니터링 시작
                _ = MonitorPerformance(); // 성능 모니터링 시작
                _ = AnalyzeAndLogFinalResults(); // 최종 결과 분석 시작
            }

            while (client.Connected)
            {
                if (stream.DataAvailable)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
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
                        await stream.WriteAsync(responseData, 0, responseData.Length);
                        await stream.FlushAsync();

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

                await Task.Delay(100);
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
                await stream.DisposeAsync();
            }

            if (client != null)
            {
                client.Close();
            }

            lock (lockObject)
            {
                if (connectedClients > 0) // 클라이언트 수가 음수가 되지 않도록 확인
                {
                    connectedClients--; // 연결된 클라이언트 수 감소
                }
                Console.WriteLine($"클라이언트 연결 종료. 현재 연결된 클라이언트 수: {connectedClients}");
            }
        }
    }
}
