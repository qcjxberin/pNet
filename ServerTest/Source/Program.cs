using System;
using pNet.Server;
using pNet.Externals;
using System.Net;
using System.Text;

internal class Program
{
    private static Server _Server = null;

    private static void Main()
    {
        _Server = new Server(_EventHandler, _LoginHandler);
        _Server.SetOption(ServerOption.Port, 1234);
        _Server.SetOption(ServerOption.BufferSize, 512);
        _Server.Start();

        string input = "";
        while (true)
        {
            input = Console.ReadLine();
            if (input == "exit") { break; }
            _Server.SendToAll(System.Text.Encoding.UTF8.GetBytes(input), PacketType.Unreliable);
            _Server.SendToAll(System.Text.Encoding.UTF8.GetBytes(input), PacketType.Reliable);
        }

        _Server.Drop(0, 123);
        _Server.Stop();
        Console.ReadLine();
    }

    private static void _EventHandler(EventType type, object[] args)
    {
        try
        {
            switch (type)
            {
                case EventType.Start:
                    Console.WriteLine("[Start] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                    break;
                case EventType.Stop:
                    Console.WriteLine("[Stop] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                    break;
                case EventType.OptionChange:
                    Console.WriteLine("[OptionChange] code:{0} message:{1} option:{2}", ((int)args[0]).ToString(), (string)args[1], ((ServerOption)args[2]).ToString());
                    break;
                case EventType.Connect:
                    Console.WriteLine("[Connect] clientId:{0} code:{1} message:{2}", ((uint)args[0]).ToString(), ((int)args[1]).ToString(), (string)args[2]);
                    break;
                case EventType.Disconnect:
                    Console.WriteLine("[Disconnect] clientId:{0} code:{1} message:{2} reasonCode:{3}", ((uint)args[0]).ToString(), ((int)args[1]).ToString(), (string)args[2], ((byte)args[3]).ToString());
                    break;
                case EventType.Receive:
                    Console.WriteLine("[Receive] clientId:{0} dataLenght:{1} packetType:{2}", ((uint)args[0]).ToString(), ((int)args[2]).ToString(), ((PacketType)args[3]).ToString());
                    break;
                case EventType.Send:
                    Console.WriteLine("[Send] code:{0} message:{1} clientId:{2}", ((int)args[0]).ToString(), (string)args[1], (uint)args[2]);
                    break;
                case EventType.PingUpdate:
                    //Console.WriteLine("[PingUpdate] clientId:{0} ping:{1}", ((uint)args[0]).ToString(), ((uint)args[1]).ToString());
                    break;
            }
        }
        catch (Exception e) { Console.WriteLine(e.ToString()); }
    }

    private static byte _LoginHandler(uint clientId, EndPoint endPoint, byte[] bytes)
    {
        if (bytes == null)
        { Console.WriteLine("[Login] clientId:{0} endPoint:{1}", clientId.ToString(), endPoint.ToString()); }
        else
        {
            string[] loginFields = Encoding.UTF8.GetString(bytes).Split('@');
            Console.WriteLine("[Login] clientId:{0} endPoint:{1} bytes:{2} -> [username: {3}] [password: {4}]", clientId.ToString(), endPoint.ToString(), Encoding.UTF8.GetString(bytes), loginFields[0], loginFields[1]);
        }
        return 0;
    }
}