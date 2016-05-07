using System;
using System.Net;
using pNet.Client;
using pNet.Externals;
using System.Text;
using pMd5;
using MonoLightTech.Serialization;

internal class Program
{
    private static Client _Client = null;

    private static void Main()
    {
        _Client = new Client(_EventHandler);
        Console.ReadLine();
        EndPoint connectEp = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234);

        string username = "test";
        string password = Md5Calculator.CalculateMd5String("test");
        string loginData = username + "@" + password;

        _Client.Connect(connectEp, Encoding.UTF8.GetBytes(loginData));
        string input = "";
        while (true)
        {
            input = Console.ReadLine();
            if (input == "exit") { break; }
            _Client.Send(System.Text.Encoding.UTF8.GetBytes(input), PacketType.Unreliable);
            _Client.Send(System.Text.Encoding.UTF8.GetBytes(input), PacketType.Reliable);
        }
        _Client.Disconnect(10);
        Console.ReadLine();
    }

    private static void _EventHandler(EventType type, object[] args)
    {
        switch (type)
        {
            case EventType.Connect:
                Console.WriteLine("[Connect] code:{0} message:{1} reasonCode:{2}", ((int)args[0]).ToString(), (string)args[1], ((byte)args[2]).ToString());
                break;
            case EventType.Disconnect:
                Console.WriteLine("[Disconnect] code:{0} message:{1} reasonCode:{2}", ((int)args[0]).ToString(), (string)args[1], ((byte)args[2]).ToString());
                break;
            case EventType.Receive:
                Console.WriteLine("[Receive] dataLenght:{0} packetType:{1}", ((int)args[1]).ToString(), ((PacketType)args[2]).ToString());
                break;
            case EventType.Send:
                Console.WriteLine("[Send] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                break;
            case EventType.OptionChange:
                Console.WriteLine("[OptionChange] code:{0} message:{1} option:{2}", ((int)args[0]).ToString(), (string)args[1], ((ClientOption)args[2]).ToString());
                break;
        }
    }
}