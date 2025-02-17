using System.Diagnostics;
using System.Text;
using TCP.Servers;

namespace TCPServerUnitTest
{
    [TestClass]
    public class UnitTest1
    {
        ITcpServer tcpServer;

        [TestMethod]
        public void TCPServerUnitTest()
        {
            tcpServer = new TcpServer(1470);
            tcpServer.DataReceived += TCP_OnDataReiceved;
            tcpServer.
            tcpServer.StartAsync();

     

        }


        public void TCP_OnDataReiceved(byte[] data, string ip)
        {
            string str = Encoding.Default.GetString(data);


            Debug.WriteLine(str);
            Debug.WriteLine(str);
        }

    }
}
