using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class IOCPServer
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

    private static Socket serverSocket;
    private static ConcurrentDictionary<Socket, SocketAsyncEventArgs> clientSockets = new ConcurrentDictionary<Socket, SocketAsyncEventArgs>();
    private static ConcurrentDictionary<Socket, int> clientIds = new ConcurrentDictionary<Socket, int>(); // 클라이언트 고유 ID 매핑

    // 1초 동안의 반응 시간과 응답 횟수를 저장하기 위한 변수
    private static long currentSecondResponseTime = 0;
    private static int currentSecondResponseCount = 0;

    static void Main(string[] args)
    {
        InitializePerformanceCounters();

        // 서버 소켓 설정
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        serverSocket.Bind(new IPEndPoint(IPAddress.Any, 12345));
        serverSocket.Listen(100);

        // IOCP 설정
        for (int i = 0; i < Environment.ProcessorCount * 2; i++)
        {
            StartAccept();
        }

        Console.WriteLine("IOCP 서버 시작!");

        Console.ReadLine(); // 종료 명령 대기
    }

    static void InitializePerformanceCounters()
    {
        cpuCounter = new PerformanceCounter("Process", "% Processor Time", currentProcess.ProcessName);
    }

    static void StartAccept()
    {
        try
        {
            SocketAsyncEventArgs acceptEventArgs = new SocketAsyncEventArgs();
            acceptEventArgs.Completed += AcceptCompleted;

            if (!serverSocket.AcceptAsync(acceptEventArgs))
            {
                AcceptCompleted(null, acceptEventArgs);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AcceptAsync 시작 중 오류 발생: {ex.Message}");
        }
    }

    static void AcceptCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.SocketError == SocketError.Success)
        {
            Socket clientSocket = e.AcceptSocket;
            Console.WriteLine("클라이언트 연결!");

            SocketAsyncEventArgs readWriteEventArgs = new SocketAsyncEventArgs();
            readWriteEventArgs.Completed += IOCompleted;
            readWriteEventArgs.SetBuffer(new byte[1024], 0, 1024);
            readWriteEventArgs.UserToken = clientSocket;

            int clientId = Interlocked.Increment(ref connectedClients);
            clientIds.TryAdd(clientSocket, clientId);

            if (!clientSocket.ReceiveAsync(readWriteEventArgs))
            {
                IOCompleted(null, readWriteEventArgs);
            }

            StartAccept(); // 다음 연결 대기 시작
        }
        else
        {
            Console.WriteLine($"클라이언트 연결 실패: {e.SocketError}");
            StartAccept(); // 다음 연결 대기 시작
        }
    }

    static void IOCompleted(object? sender, SocketAsyncEventArgs e)
    {
        Socket clientSocket = e.UserToken as Socket;

        // Null 및 폐기 여부 확인
        if (clientSocket == null || !clientSocket.Connected)
        {
            CleanupSocket(e);
            return;
        }

        // 예상 클라이언트 수 설정
        if (expectedClients == 0)
        {
            string receivedData = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred).Trim();
            if (int.TryParse(receivedData, out expectedClients) && expectedClients > 0)
            {
                Console.WriteLine($"예상 클라이언트 수 설정됨: {expectedClients}");
            }
            else
            {
                Console.WriteLine("잘못된 클라이언트 수를 수신했습니다.");
            }

            // 설정 후 클라이언트 연결 종료
            CleanupSocket(e);
            return;
        }

        var startTime = Stopwatch.StartNew(); // 응답 시간 측정을 위한 시작 시간 기록

        if (e.LastOperation == SocketAsyncOperation.Receive && e.SocketError == SocketError.Success && e.BytesTransferred > 0)
        {
            try
            {
                // 수신된 데이터 처리
                string receivedData = Encoding.UTF8.GetString(e.Buffer, e.Offset, e.BytesTransferred).Trim();

                lock (lockObject)
                {
                    totalReceivedBytes += e.BytesTransferred; // 수신된 바이트 누적
                }

                // 클라이언트의 고유 ID 가져오기
                if (clientIds.TryGetValue(clientSocket, out int clientId))
                {
                    // Console.WriteLine($"[{clientId}]로부터 받은 데이터: {receivedData}");
                    Console.Write($"[{clientId}]");
                }
                else
                {
                    Console.WriteLine("알 수 없는 클라이언트로부터 데이터 수신.");
                }

                // 응답 메시지 생성
                string response = $"클라이언트 {clientId} - 서버 응답: 메시지 수신 완료";
                byte[] responseData = Encoding.UTF8.GetBytes(response);
                e.SetBuffer(responseData, 0, responseData.Length);

                // 응답을 전송
                if (!clientSocket.SendAsync(e))
                {
                    OnSendCompleted(e); // 비동기 작업이 즉시 완료된 경우
                }

                lock (lockObject)
                {
                    totalSentBytes += responseData.Length; // 송신된 바이트 누적
                }

                startTime.Stop(); // 응답 시간 측정 완료
                Interlocked.Add(ref totalResponseTime, startTime.ElapsedMilliseconds);
                Interlocked.Increment(ref responseCount);

                // 1초 동안의 반응 시간 및 응답 횟수 업데이트
                lock (lockObject)
                {
                    currentSecondResponseTime += startTime.ElapsedMilliseconds;
                    currentSecondResponseCount++;
                }

                // 모든 클라이언트가 연결되면 성능 모니터링 시작
                if (connectedClients == expectedClients && !isRunning)
                {
                    Console.WriteLine("모든 클라이언트가 연결되었습니다. 성능 모니터링 시작.");
                    isRunning = true; // 모니터링 시작
                    new Thread(MonitorPerformance).Start();
                    new Thread(AnalyzeAndLogFinalResults).Start();
                }
            }
            catch (ObjectDisposedException)
            {
                CleanupSocket(e); // 소켓이 이미 폐기된 경우 안전하게 처리
            }
            catch (Exception ex)
            {
                Console.WriteLine($"데이터 처리 중 오류 발생: {ex.Message}");
                CleanupSocket(e);
            }
        }
        else if (e.LastOperation == SocketAsyncOperation.Send && e.SocketError == SocketError.Success)
        {
            // 데이터 전송 완료 후 수신 대기 설정
            e.SetBuffer(new byte[1024], 0, 1024);
            if (!clientSocket.ReceiveAsync(e))
            {
                IOCompleted(null, e);
            }
        }
        else
        {
            CleanupSocket(e); // 소켓 오류 또는 0 바이트 전송된 경우
        }
    }

    static void CleanupSocket(SocketAsyncEventArgs e)
    {
        Socket clientSocket = e.UserToken as Socket;

        if (clientSocket != null)
        {
            try
            {
                if (clientSocket.Connected)
                {
                    clientSocket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (SocketException se)
            {
                Console.WriteLine($"소켓 종료 중 소켓 오류 발생: {se.Message}");
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine("소켓이 이미 폐기되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"소켓 종료 중 일반 오류 발생: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (clientSocket.Connected)
                    {
                        Console.WriteLine($"클라이언트 연결 종료: {clientSocket.RemoteEndPoint}");
                    }
                }
                catch (ObjectDisposedException)
                {
                    Console.WriteLine("소켓이 이미 폐기되었습니다. EndPoint에 접근할 수 없습니다.");
                }
                clientSocket.Close();
                clientSockets.TryRemove(clientSocket, out _);
                clientIds.TryRemove(clientSocket, out _); // 클라이언트 ID 매핑도 제거
                Interlocked.Decrement(ref connectedClients); // 연결된 클라이언트 수 감소
            }
        }

        e.Dispose();
    }

    static void OnSendCompleted(SocketAsyncEventArgs e)
    {
        Socket clientSocket = e.UserToken as Socket;

        // 송신 완료 후 수신 대기 설정
        e.SetBuffer(new byte[1024], 0, 1024);
        if (!clientSocket.ReceiveAsync(e))
        {
            IOCompleted(null, e);
        }
    }

    static void MonitorPerformance()
    {
        try
        {
            Console.WriteLine("성능 모니터링 스레드 시작.");
            using (StreamWriter writer = new StreamWriter("performance_log.csv", append: false))
            {
                writer.WriteLine("Time, CPU(%), Memory(MB), Sent(Bytes), Received(Bytes), Errors, Average Response Time(ms)");

                while (isRunning)
                {
                    try
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
                            writer.Flush(); // 버퍼를 즉시 디스크에 기록

                            // 1초 동안의 반응 시간 및 응답 횟수를 초기화
                            currentSecondResponseTime = 0;
                            currentSecondResponseCount = 0;
                        }

                        Interlocked.Increment(ref sampleCount); // 샘플 수 증가
                        Interlocked.Add(ref totalCpuUsage, (long)cpuUsage); // 총 CPU 사용량 증가
                        Interlocked.Add(ref totalMemoryUsage, (long)memoryUsage); // 총 메모리 사용량 증가

                        Thread.Sleep(1000); // 1초 간격으로 수집
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"성능 모니터링 중 오류 발생: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"파일을 열거나 기록하는 중 오류 발생: {ex.Message}");
        }
    }

    static void AnalyzeAndLogFinalResults()
    {
        try
        {
            Console.WriteLine("최종 분석 스레드 시작.");
            Thread.Sleep(60000 * 31); // 성능 모니터링 후 60초 * 31 대기

            lock (lockObject)
            {
                isRunning = false; // 모니터링 중지

                float averageCpuUsage = sampleCount > 0 ? totalCpuUsage / (float)sampleCount : 0;
                float averageMemoryUsage = sampleCount > 0 ? totalMemoryUsage / (float)sampleCount : 0;
                float averageResponseTime = responseCount > 0 ? totalResponseTime / (float)responseCount : 0;

                using (StreamWriter writer = new StreamWriter("final_analysis.csv", append: false))
                {
                    writer.WriteLine("Final Analysis");
                    writer.WriteLine($"Total Samples: {sampleCount}");
                    writer.WriteLine($"Average CPU Usage (%): {averageCpuUsage:F2}");
                    writer.WriteLine($"Average Memory Usage (MB): {averageMemoryUsage:F2}");
                    writer.WriteLine($"Total Sent Bytes: {totalSentBytes}");
                    writer.WriteLine($"Total Received Bytes: {totalReceivedBytes}");
                    writer.WriteLine($"Total Errors: {totalErrors}");
                    writer.WriteLine($"Average Response Time (ms): {averageResponseTime:F2}");
                    writer.Flush(); // 즉시 기록
                    writer.Close(); // 파일 닫기
                }

                Console.WriteLine("최종 분석 결과가 기록되었습니다.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"최종 분석 중 오류 발생: {ex.Message}");
        }

        // 서버 종료
        Environment.Exit(0);
    }
}
