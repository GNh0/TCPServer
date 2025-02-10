using System.Diagnostics;
using System.Text;
using TCP.Server;

namespace TcpServer_Winform
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            tcpserver = new TcpServer();
        }

        ITcpServer tcpserver;

        private void button1_Click(object sender, EventArgs e)
        {
            tcpserver.Port = textBox1.Text;
            tcpserver.DataReceived += TCPServer_OnDataReceived;

            tcpserver.CustomEndOfMessageBytes = new List<byte[]> { new byte[] { 0x0A, 0x0D }, new byte[] { 0x0D, 0x0A }, new byte[] { 0x0A }, new byte[] { 0x0D } };
            tcpserver.StartAsync();

        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        
        public void TCPServer_OnDataReceived(byte[] data, string ipadd)
        {
            Debug.WriteLine($"{ipadd} - {Encoding.Default.GetString(data)}");
        }
    }
}
