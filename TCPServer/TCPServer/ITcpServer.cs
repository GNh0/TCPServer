using System.Diagnostics;
using System.Net.Sockets;

namespace TCP.Server
{
    public interface ITcpServer
    {
        object Port { get; set; }
        List<byte[]> CustomEndOfMessageBytes
        {
            get; set;
        }



        Task StartAsync();
        void Stop();
        bool DisconnectClient(string ipAddress);
        bool IsListen();
        IEnumerable<TcpClient> GetConnectedClients();
        bool BroadcastData(string data);
        bool SendDataToClient(string ip, string data);
        bool IsPortAvailable(int _nPort);

        event Action<byte[], string> DataReceived;

    }


    public class DataBuffer
    {
        public byte[] bydata;

        public string ipaddress;
    }
}
