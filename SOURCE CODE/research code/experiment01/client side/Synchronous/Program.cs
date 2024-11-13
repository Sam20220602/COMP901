using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class MultiSyncClient
{
    private static readonly string CsvFileName = "LatencyData.csv"; // CSV 파일 이름
    private static readonly SemaphoreSlim fileLock = new SemaphoreSlim(1, 1); // 파일 접근을 동기화하기 위한 SemaphoreSlim

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("사용법: MultiSyncClient <NumberOfClients>");
            return;
        }

        int numberOfClients = int.Parse(args[0]);
        Console.WriteLine($"클라이언트 수: {numberOfClients}");

        // CSV 파일 초기화 (헤더 추가)
        InitializeCsvFile();

        // 서버에 총 클라이언트 수 알리기
        SendTotalClientsToServer(numberOfClients);

        for (int i = 1; i <= numberOfClients; i++)
        {
            int clientId = i;
            Task.Run(() => StartClient(clientId));
            Thread.Sleep(50); // 클라이언트 간의 시작 시간 조정
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

    static void SendTotalClientsToServer(int totalClients)
    {
        try
        {
            using (TcpClient client = new TcpClient("192.168.1.77", 12345))
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = Encoding.UTF8.GetBytes(totalClients.ToString());
                stream.Write(buffer, 0, buffer.Length);
                stream.Flush();

                // 서버로부터 확인 메시지 받기
                buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                Console.WriteLine($"서버 응답: {Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim()}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"서버에 클라이언트 수를 알리는 중 오류 발생: {ex.Message}");
        }
    }

    static void StartClient(int clientId)
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
                    string message = $"클라이언트 {userId} 메시지";
                    byte[] buffer = Encoding.UTF8.GetBytes(message);

                    // 레이턴시 측정 시작
                    DateTime sendTime = DateTime.Now;
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush();

                    buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    DateTime receiveTime = DateTime.Now;

                    if (bytesRead > 0)
                    {
                        string response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        Console.WriteLine($"클라이언트 {userId} - 서버 응답: {response}");

                        // 레이턴시 계산 및 CSV 파일 기록
                        double latency = (receiveTime - sendTime).TotalMilliseconds;
                        AppendLatencyToCsvFile(userId, latency);
                    }

                    Thread.Sleep(1000); // 1초 간격으로 서버와 통신
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

    static void AppendLatencyToCsvFile(string clientId, double latency)
    {
        fileLock.Wait(); // 파일 쓰기 전에 잠금
        try
        {
            using (StreamWriter writer = new StreamWriter(CsvFileName, true))
            {
                string log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff},{clientId},{latency}";
                writer.WriteLine(log);
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
