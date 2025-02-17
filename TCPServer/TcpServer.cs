using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TCP.Servers
{
    public class TcpServer : ITcpServer
    {
        public object Port
        {
            get
            {
                return port;
            }

            set
            {
                if (value is int intValue)
                {
                    port = intValue;
                }
                else if (value is string strValue && int.TryParse(strValue, out int parsedPort))
                {
                    port = parsedPort;
                }
                else
                {
                    throw new ArgumentException("Invalid port number. Port must be an integer or a valid string representation of an integer.");
                }

                // _listener = new TcpListener(IPAddress.Any, port);
            }

        }

        private int port = 0;
        private TcpListener _listener;
        private ConcurrentDictionary<string, TcpClient> _clients;
        private ConcurrentDictionary<string, MemoryStream> _clientBuffers; // 클라이언트마다 데이터 버퍼 관리
        private ConcurrentDictionary<string, ConcurrentQueue<DataBuffer>> _clientBufferQueue; // 클라이언트마다 데이터 버퍼 관리
        private bool _isRunning;
       
        

        public List<byte[]> CustomEndOfMessageBytes
        {
            get; set;
        } = new List<byte[]> { new byte[] { 0x0A, 0x0D }, new byte[] { 0x0D, 0x0A }, new byte[] { 0x0A }, new byte[] { 0x0D } };

        private static TcpServer _instance = new TcpServer();
        private static readonly object _lock = new object();
        public static TcpServer Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new TcpServer();
                    }
                    return _instance;
                }
            }
        }
    
       

        //public event DataReceivedEventHandler _DataReceiveded;
        //public delegate void DataReceivedEventHandler(byte[] data, string clientIp = null);

        public event Action<byte[], string> DataReceived;

        protected virtual void OnDataReceived(byte[] data, string clientIp = null)
        {
            DataReceived?.Invoke(data, clientIp);
        }

        public TcpServer(int _port) : this()
        {
            Port = _port;
        }


        public TcpServer()
        {
            _clients = new ConcurrentDictionary<string, TcpClient>();
            _clientBuffers = new ConcurrentDictionary<string, MemoryStream>(); // 데이터 버퍼 초기화
            _clientBufferQueue = new ConcurrentDictionary<string, ConcurrentQueue<DataBuffer>>();
        }
        

        private CancellationTokenSource _cancellationTokenSource;

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _cancellationTokenSource = new CancellationTokenSource();
            _listener.Start();
            _isRunning = true;
            while (_isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().WithCancellation(_cancellationTokenSource.Token);
                    var clientId = client?.Client?.RemoteEndPoint?.ToString();
                    if (AddClient(clientId, client))
                    {
                        Task.Run(() => HandleClientAsync(clientId, client));
                    }
                    else
                    {
                        client.Close();
                    }
                }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogException(nameof(StartAsync), ex);
                    // System.Diagnostics.Debug.WriteLine($"서버 시작 중 오류 발생: {ex.Message}");
                }
            }
        }


        private bool AddClient(string clientId, TcpClient client)
        {
            bool addedClient = _clients.TryAdd(clientId, client);
            bool addedBuffer = _clientBuffers.TryAdd(clientId, new MemoryStream());
            bool addedBufferQueue = _clientBufferQueue.TryAdd(clientId, new ConcurrentQueue<DataBuffer>());
            if (!addedClient || !addedBuffer || !addedBufferQueue)
            {
                RemoveClient(clientId, addedClient, addedBuffer, addedBufferQueue);
                return false;
            }
            return true;
        }
        private void RemoveClient(string clientId, bool addedClient, bool addedBuffer, bool addedBufferQueue)
        {
            if (addedClient)
                _clients.TryRemove(clientId, out _);
            if (addedBuffer)
                _clientBuffers.TryRemove(clientId, out _);
            if (addedBufferQueue)
                _clientBufferQueue.TryRemove(clientId, out _);
        }



        private void ClearClients()
        {
            foreach (var client in _clients)
            {
                CloseClient(client.Value);
                RemoveClientBuffers(client.Key);
            }
            _clientBuffers.Clear();
            _clientBufferQueue.Clear();
            _clients.Clear();
        }
        private void CloseClient(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                LogException(nameof(ClearClients), ex);

                // System.Diagnostics.Debug.WriteLine($"클라이언트 종료 중 오류 발생: {ex.Message}");
            }
        }


        private void RemoveClientBuffers(string ipAddress)
        {
            if (_clientBuffers.TryRemove(ipAddress, out var buffer))
                buffer.Dispose();
            if (_clientBufferQueue.TryRemove(ipAddress, out var bufferQueue))
            {
                while (bufferQueue.TryDequeue(out _))
                {
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource.Cancel();
            _listener.Stop();
            ClearClients();
        }

        // 특정 IP의 클라이언트 연결을 종료하는 메서드
        public bool DisconnectClient(string ipAddress)
        {
            {
                if (_clients.TryRemove(ipAddress, out TcpClient client))
                {
                    CloseClient(client);
                    RemoveClientBuffers(ipAddress);
                    return true;
                }
                return false;
            }
        }

        public bool IsListen() => _isRunning;


        // 클라이언트 목록을 반환하는 메서드
        public IEnumerable<TcpClient> GetConnectedClients() => _clients.Values.ToList();



        private async Task HandleClientAsync(string clientId, TcpClient client)
        {
            NetworkStream stream = null;
            MemoryStream messageBuffer = _clientBuffers[clientId];
            ConcurrentQueue<DataBuffer> messageBufferQueue = _clientBufferQueue[clientId];

            try
            {
                stream = client?.GetStream();

                var buffer = new byte[16384];

                while (_isRunning)
                {
                    try
                    {
                        int bytesRead = await stream?.ReadAsync(buffer, 0, buffer.Length);

                        if (bytesRead == 0)
                        {
                            break;
                        }
                        messageBuffer.Write(buffer, 0, bytesRead);

                        if (messageBuffer.Length > 0)
                        {
                            ProcessClientMessage(messageBuffer, messageBufferQueue, clientId);
                        }

                        while (messageBufferQueue.TryDequeue(out DataBuffer deque))
                        {
                            OnDataReceived(deque.bydata, deque.ipaddress);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"{nameof(HandleClientAsync)} _ {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(nameof(HandleClientAsync), ex);
                //Console.WriteLine($"{nameof(HandleClientAsync)} _ {ex.Message}");
                throw;
            }
            finally
            {
            }
            ClearResources(clientId, stream, client);
        }


        private void ClearResources(string clientId, NetworkStream stream, TcpClient client)
        {
            try
            {
                stream?.Close();
                client?.Close();
                _clients?.TryRemove(clientId, out _);
                if (_clientBuffers.TryRemove(clientId, out var buffer))
                {
                    buffer.Dispose();
                }
                if (_clientBufferQueue.TryRemove(clientId, out var bufferQueue))
                {
                    while (bufferQueue.TryDequeue(out _))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(nameof(ClearResources), ex);
                //Console.WriteLine($"{nameof(ClearResources)} _ {ex.Message}");
            }
        }


        private void ProcessClientMessage(MemoryStream messageBuffer, ConcurrentQueue<DataBuffer> BufferQueue, string clientIp)
        {
            try
            {
                byte[] bufferArray = messageBuffer.ToArray();
                int endOfMessageIndex;
                // 메시지 주기적으로 확인
                while ((endOfMessageIndex = FindEndOfMessageIndex(bufferArray)) != -1)
                {
                    // 여기서 endCodeLength를 찾은 엔드 코드의 길이에 맞게 조정합니다.
                    int matchingEndCodeLength = FindMatchingEndCodeLength(bufferArray, endOfMessageIndex);
                    int completeMessageLength = endOfMessageIndex + matchingEndCodeLength;
                    var completeMessage = new byte[completeMessageLength];

                    Array.Copy(bufferArray, completeMessage, completeMessageLength);
                    BufferQueue.Enqueue(new DataBuffer() { bydata = completeMessage, ipaddress = clientIp });

                    // 처리된 메시지 제거 후 나머지 버퍼 다시 설정
                    byte[] remaining = new byte[bufferArray.Length - completeMessageLength];
                    Array.Copy(bufferArray, completeMessageLength, remaining, 0, remaining.Length);

                    messageBuffer.SetLength(0);
                    messageBuffer.Write(remaining, 0, remaining.Length);
                    bufferArray = remaining;
                }
            }

            catch (Exception ex)
            {
                throw;
            }
        }



        private int FindMatchingEndCodeLength(byte[] bufferArray, int endOfMessageIndex)
        {

            foreach (var endBytes in CustomEndOfMessageBytes)
            {
                bool match = true;

                // 엔드 코드의 길이만큼 비교하기 전에 범위를 확인
                if (endOfMessageIndex + endBytes.Length > bufferArray.Length)
                {
                    continue; // 범위를 벗어나면 다음 엔드 코드로 넘어감
                }

                // 엔드 코드의 길이만큼 비교
                for (int j = 0; j < endBytes.Length; j++)
                {
                    if (bufferArray[endOfMessageIndex + j] != endBytes[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match == true)
                {
                    // 매칭되는 엔드 코드의 길이를 반환
                    return endBytes.Length;
                }
            }
            // 기본값으로 0 반환
            return 0;
        }

        private int FindEndOfMessageIndex(byte[] buffer)
        {
            // 엔드코드가 설정되어 있지 않다면, 메시지의 끝을 찾을 수 없습니다.
            if (CustomEndOfMessageBytes == null)
            {
                return -1;
            }

            int earliestIndex = -1;

            // 가장 먼저 나타나는 엔드코드의 시작 인덱스를 찾습니다.
            foreach (var endBytes in CustomEndOfMessageBytes)
            {
                for (int i = 0; i <= buffer.Length - endBytes.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < endBytes.Length; j++)
                    {
                        if (buffer[i + j] != endBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        // 가장 먼저 발견된 엔드코드의 인덱스를 저장
                        if (earliestIndex == -1 || i < earliestIndex)
                        {
                            earliestIndex = i;
                        }
                        break; // 이 엔드코드에 대해서는 더 이상 검사할 필요 없음
                    }
                }
            }

            return earliestIndex;
        }


        // 모든 클라이언트에게 데이터 전송
        public bool BroadcastData(string data)
        {
            return BroadcastData(Encoding.UTF8.GetBytes(data));
        }

        // 특정 IP의 클라이언트에게만 데이터 전송
        public bool SendDataToClient(string ip, string data)
        {
            return SendDataToClient(ip, Encoding.UTF8.GetBytes(data));
        }

        // 모든 클라이언트에게 데이터 전송
        public bool BroadcastData(byte[] data)
        {
            List<Exception> exceptions = new List<Exception>();
            bool result = false;
            foreach (var client in _clients.Values)
            {
                NetworkStream stream = client.GetStream();
                if (stream.CanWrite == true)
                {
                    try
                    {
                        stream.Write(data, 0, data.Length);
                        result = true;
                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e); // 예외를 리스트에 추가
                    }
                }
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions); // 모든 예외를 하나로 묶어서 발생
            }
            return result;
        }

        // 특정 IP의 클라이언트에게만 데이터 전송
        public bool SendDataToClient(string ip, byte[] data)
        {
            if (_clients.TryGetValue(ip, out TcpClient client) == true)
            {
                NetworkStream stream = client.GetStream();
                if (stream.CanWrite == true)
                {
                    try
                    {
                        stream.Write(data, 0, data.Length);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        throw;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Client not found."); // 클라이언트를 찾을 수 없는 경우 예외 발생
            }
            return false;
        }



        /// <summary>
        /// 사용 가능한 포트인지 확인
        /// </summary>
        public bool CanListen()
        {
            if (port <= 0 || port > 65535)
            {
                return false;
            }


            IPGlobalProperties ip = IPGlobalProperties.GetIPGlobalProperties();
            // 활성 연결 확인
            TcpConnectionInformation[] tcpInfo = ip.GetActiveTcpConnections();
            foreach (TcpConnectionInformation objTCP_Info in tcpInfo)
            {
                if (objTCP_Info.LocalEndPoint.Port == port)
                {
                    return false;
                }
            }

            // 대기 중인 포트 확인
            IPEndPoint[] endPoints = ip.GetActiveTcpListeners();
            foreach (IPEndPoint objIPEnd in endPoints)
            {
                if (objIPEnd.Port == port)
                {
                    return false;
                }
            }

            return true;
        }

        public static void LogException(string methodName, Exception ex)
        {
#if DEBUG
            // Debug 환경에서
            System.Diagnostics.Debug.WriteLine($"{methodName} _ {ex.Message}");
#else
            // Console 환경에서
            Console.WriteLine($"{methodName} _ {ex.Message}"); 
#endif
        }


        /// <summary>
        /// 사용 가능한 포트인지 확인
        /// </summary>
        public bool IsPortAvailable(int _nPort)
        {
            if (_nPort <= 0 || _nPort > 65535)
            {
                return false;
            }


            IPGlobalProperties ip = IPGlobalProperties.GetIPGlobalProperties();
            // 활성 연결 확인
            TcpConnectionInformation[] tcpInfo = ip.GetActiveTcpConnections();
            foreach (TcpConnectionInformation objTCP_Info in tcpInfo)
            {
                if (objTCP_Info.LocalEndPoint.Port == _nPort)
                {
                    return false;
                }
            }

            // 대기 중인 포트 확인
            IPEndPoint[] endPoints = ip.GetActiveTcpListeners();
            foreach (IPEndPoint objIPEnd in endPoints)
            {
                if (objIPEnd.Port == _nPort)
                {
                    return false;
                }
            }

            return true;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 관리되는 리소스 해제
                Stop();
                // _listener, _clients 등의 리소스 해제
            }
            // 비관리 리소스 해제 (필요한 경우)
        }

    }

    public static class TaskExtensions
    {
        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await task;
        }
    }

    public class AuthenticationException : Exception
    {
        public AuthenticationException(string message) : base(message) { }
    }
}

