using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Client
{
    private Socket clientSocket;
    private const string serverIp = "127.0.0.1"; // 서버 IP (로컬)
    private const int port = 5001;
    private int userId;
    private double x, y, z; // 현재 위치 좌표
    private double velocity = 1.0; // 고정된 속도
    private int mode; // 0: 데드레커닝 비활성화, 1: 데드레커닝 활성화
    private byte[] receiveBuffer = new byte[1024]; // 수신 버퍼

    public Client(int userId, double startX, double startY, double startZ, int mode)
    {
        this.userId = userId;
        this.x = startX;
        this.y = startY;
        this.z = startZ;
        this.mode = mode;
    }

    public void Start()
    {
        clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        clientSocket.Connect(serverIp, port);

        Console.WriteLine($"Client {userId} connected to the server.");

        // 서버로부터의 응답을 비동기적으로 처리
        StartReceiving();

        // 클라이언트 이동 루프
        while (true)
        {
            Thread.Sleep(1000); // 1초마다 이동
            Move();
            SendPosition();
        }
    }

    private void Move()
    {
        if (mode == 1) // 데드레커닝 모드일 경우
        {
            x += velocity; // x 방향으로만 이동
            y += velocity; // y 방향으로만 이동
            z += velocity; // z 방향으로만 이동
        }

        // Console.WriteLine($"Client {userId} moved to new position: {x}, {y}, {z}");
    }

    private void SendPosition()
    {
        // userId(4바이트) + x(8바이트) + y(8바이트) + z(8바이트) + mode(4바이트) 전송
        byte[] userIdBytes = BitConverter.GetBytes(userId);
        byte[] xBytes = BitConverter.GetBytes(x);
        byte[] yBytes = BitConverter.GetBytes(y);
        byte[] zBytes = BitConverter.GetBytes(z);
        byte[] modeBytes = BitConverter.GetBytes(mode);

        // 각 바이트 배열을 합침
        byte[] data = new byte[userIdBytes.Length + xBytes.Length + yBytes.Length + zBytes.Length + modeBytes.Length];
        Array.Copy(userIdBytes, 0, data, 0, userIdBytes.Length);
        Array.Copy(xBytes, 0, data, userIdBytes.Length, xBytes.Length);
        Array.Copy(yBytes, 0, data, userIdBytes.Length + xBytes.Length, yBytes.Length);
        Array.Copy(zBytes, 0, data, userIdBytes.Length + xBytes.Length + yBytes.Length, zBytes.Length);
        Array.Copy(modeBytes, 0, data, userIdBytes.Length + xBytes.Length + yBytes.Length + zBytes.Length, modeBytes.Length);

        // 패킷 길이 추가 (4바이트)
        byte[] dataLength = BitConverter.GetBytes(data.Length);
        byte[] finalData = new byte[dataLength.Length + data.Length];

        Array.Copy(dataLength, 0, finalData, 0, dataLength.Length);
        Array.Copy(data, 0, finalData, dataLength.Length, data.Length);

        clientSocket.Send(finalData);
        // Console.WriteLine($"Client {userId} sent position to server: {x}, {y}, {z}, Mode: {mode}");
    }

    private void StartReceiving()
    {
        SocketAsyncEventArgs receiveEventArgs = new SocketAsyncEventArgs();
        receiveEventArgs.SetBuffer(receiveBuffer, 0, receiveBuffer.Length);
        receiveEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(ReceiveCompleted);
        receiveEventArgs.UserToken = clientSocket;

        bool willRaiseEvent = clientSocket.ReceiveAsync(receiveEventArgs);
        if (!willRaiseEvent)
        {
            ReceiveCompleted(this, receiveEventArgs);
        }
    }

    private void ReceiveCompleted(object sender, SocketAsyncEventArgs e)
    {
        Socket clientSocket = e.UserToken as Socket;

        if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
        {
            // 먼저 'UPDATE' 문자열 (6바이트) 확인
            string serverMessage = Encoding.UTF8.GetString(e.Buffer, 0, 6);  // 'UPDATE'는 항상 6바이트
            if (serverMessage == "UPDATE")
            {
                Console.WriteLine($"!! Client {userId} received position update from server.");

                // 6바이트 이후부터 좌표값들 (x, y, z 각각 8바이트씩) 처리
                int offset = 6; // 'UPDATE' 문자열 이후부터 시작
                x = BitConverter.ToDouble(e.Buffer, offset);
                offset += 8;
                y = BitConverter.ToDouble(e.Buffer, offset);
                offset += 8;
                z = BitConverter.ToDouble(e.Buffer, offset);

                Console.WriteLine($"Client {userId} updated position to: {x}, {y}, {z}");
            }

            // 수신 대기 다시 시작
            StartReceiving();
        }
        else
        {
            // 연결이 종료되었거나 오류가 발생한 경우
            Console.WriteLine($"Client {userId} disconnected from server.");
            clientSocket.Close();
        }
    }


    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: ClientApp <max_clients> <dead_reckoning_mode>");
            return;
        }

        int maxClients = int.Parse(args[0]); // 최대 클라이언트 수
        int mode = int.Parse(args[1]); // 데드레커닝 모드 (0 or 1)

        Thread[] clientThreads = new Thread[maxClients];

        for (int i = 0; i < maxClients; i++)
        {
            int userId = i + 1;
            double startX = 0.0 + i; // 각 클라이언트가 서로 다른 초기 위치를 가짐
            double startY = 0.0 + i;
            double startZ = 0.0 + i;

            Client client = new Client(userId, startX, startY, startZ, mode);
            clientThreads[i] = new Thread(client.Start);
            clientThreads[i].Start();
        }
    }
}
