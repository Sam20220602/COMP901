using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class AsyncClient
{
    private static readonly string CsvFileName = "LatencyData.csv"; // CSV 파일 이름
    private static readonly SemaphoreSlim fileLock = new SemaphoreSlim(1, 1); // 파일 접근을 동기화하기 위한 SemaphoreSlim

    static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("사용법: AsyncClient <NumberOfClients>");
            return;
        }

        int numberOfClients = int.Parse(args[0]);
        Console.WriteLine($"클라이언트 수: {numberOfClients}");

        // CSV 파일 초기화 (헤더 추가)
        InitializeCsvFile();

        // 서버에 총 클라이언트 수 알리기
        await SendTotalClientsToServerAsync(numberOfClients);

        for (int i = 1; i <= numberOfClients; i++)
        {
            int clientId = i; // 클라이언트 ID
            _ = StartClientAsync(clientId); // 비동기적으로 클라이언트 시작
            await Task.Delay(50); // 클라이언트 시작 간격을 두어 서버 부하를 점진적으로 증가
        }

        Console.WriteLine("모든 클라이언트가 시작되었습니다. 종료하려면 아무 키나 누르세요.");
        Console.ReadLine();
    }

    static void InitializeCsvFile()
    {
        // CSV 파일에 헤더 추가
        if (!File.Exists(CsvFileName))
        {
            using (StreamWriter writer = new StreamWriter(CsvFileName, true))
            {
                writer.WriteLine("Timestamp,ClientID,Latency(ms)");
            }
        }
    }

    static async Task SendTotalClientsToServerAsync(int totalClients)
    {
        try
        {
            using (TcpClient client = new TcpClient("192.168.1.77", 12345))
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = Encoding.UTF8.GetBytes(totalClients.ToString());
                await stream.WriteAsync(buffer, 0, buffer.Length);
                await stream.FlushAsync();

                // 서버로부터 확인 메시지 받기
                buffer = new byte[1024];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                Console.WriteLine($"서버 응답: {Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim()}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버에 클라이언트 수를 알리는 중 오류 발생: {ex.Message}");
        }
    }

    static async Task StartClientAsync(int clientId)
    {
        try
        {
            string userId = clientId.ToString();
            TcpClient client = new TcpClient("192.168.1.77", 12345);
            NetworkStream stream = client.GetStream();

            while (client.Connected)
            {
                try
                {
                    // 서버로 데이터 전송
                    string message = $"클라이언트 {userId} 메시지";
                    byte[] buffer = Encoding.UTF8.GetBytes(message);

                    // 레이턴시 측정 시작
                    DateTime sendTime = DateTime.Now;
                    await stream.WriteAsync(buffer, 0, buffer.Length);
                    await stream.FlushAsync();

                    // 서버로부터 응답 수신
                    buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    DateTime receiveTime = DateTime.Now;

                    if (bytesRead > 0)
                    {
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Console.WriteLine($"클라이언트 {userId} - 서버 응답: {response}");

                        // 레이턴시 계산 및 CSV 파일 기록
                        double latency = (receiveTime - sendTime).TotalMilliseconds;
                        await AppendLatencyToCsvFile(userId, latency);
                    }

                    // 1초 대기
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"클라이언트 {userId} 데이터 송수신 중 오류 발생: {ex.Message}");
                    break;
                }
            }

            client.Close();
            Console.WriteLine($"클라이언트 {userId} 종료됨.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"클라이언트 {clientId} 서버에 연결할 수 없습니다: {ex.Message}");
        }
    }

    static async Task AppendLatencyToCsvFile(string clientId, double latency)
    {
        await fileLock.WaitAsync(); // 파일 쓰기 전에 잠금
        try
        {
            using (StreamWriter writer = new StreamWriter(CsvFileName, true))
            {
                string log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{clientId},{latency}";
                await writer.WriteLineAsync(log);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CSV 파일에 레이턴시 기록 중 오류 발생: {ex.Message}");
        }
        finally
        {
            fileLock.Release(); // 파일 쓰기 완료 후 잠금 해제
        }
    }
}
