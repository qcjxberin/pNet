using System;
using System.Net;
using System.Text;
using pNet.Client;
using pNet.Server;
using pNet.Externals;
using MonoLightTech.Serialization;


class OrdererTest
{
    static Orderer orderer = null;

    static void Main()
    {
        orderer = new Orderer(HandleOrderedStream);

        Server server = new Server(ServerEventHandler, ServerLoginHandler);

        Client client = new Client(ClientEventHandler);

        server.SetOption(ServerOption.Port, 1234);
        server.Start();

        client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 1234), null);

        Console.ReadLine();

        //Bunlar reliable unordered
        SerializableArray unordered0 = new SerializableArray();
        SerializableArray unordered1 = new SerializableArray();
        SerializableArray unordered2 = new SerializableArray();

        unordered0.AddByte(0);
        unordered0.AddUshort(0);
        unordered0.AddString("I'm unordered #0!");

        unordered1.AddByte(0);
        unordered1.AddUshort(1);
        unordered1.AddString("I'm unordered #1!");

        unordered2.AddByte(0);
        unordered2.AddUshort(2);
        unordered2.AddString("I'm unordered #2!");

        //Bunlar reliable ordered
        SerializableArray array0 = new SerializableArray();
        SerializableArray array1 = new SerializableArray();
        SerializableArray array2 = new SerializableArray();
        SerializableArray array3 = new SerializableArray();
        SerializableArray array4 = new SerializableArray();
        SerializableArray array5 = new SerializableArray();

        array0.AddByte(123);
        array0.AddUshort(0);
        array0.AddString("I'm ordered packet #0!");

        array1.AddByte(123);
        array1.AddUshort(1);
        array1.AddString("I'm ordered packet #1!");

        array2.AddByte(123);
        array2.AddUshort(2);
        array2.AddString("I'm ordered packet #2!");

        array3.AddByte(123);
        array3.AddUshort(3);
        array3.AddString("I'm ordered packet #3!");

        array4.AddByte(123);
        array4.AddUshort(4);
        array4.AddString("I'm ordered packet #4!");

        array5.AddByte(123);
        array5.AddUshort(5);
        array5.AddString("I'm ordered packet #5!");

        client.Send(array3.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(array0.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(unordered1.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(array1.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(unordered0.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(array5.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(array2.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(unordered2.Serialize(), pNet.Client.PacketType.Reliable);
        client.Send(array4.Serialize(), pNet.Client.PacketType.Reliable);

        Console.ReadLine();

        server.Stop();
        client.Close();
    }

    static void HandleOrderedStream(byte[] data)
    {
        Console.WriteLine(Encoding.UTF8.GetString(data));
    }

    static byte ServerLoginHandler(uint clientId, EndPoint endPoint, byte[] bytes)
    {
        orderer.AddClient(clientId);
        return 0;
    }

    static void ServerEventHandler(pNet.Server.EventType type, object[] args)
    {
        switch (type)
        {
            case pNet.Server.EventType.Start:
                Console.WriteLine("[Start] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                break;
            case pNet.Server.EventType.Stop:
                Console.WriteLine("[Stop] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                break;
            case pNet.Server.EventType.OptionChange:
                Console.WriteLine("[OptionChange] code:{0} message:{1} option:{2}", ((int)args[0]).ToString(), (string)args[1], ((ServerOption)args[2]).ToString());
                break;
            case pNet.Server.EventType.Connect:
                Console.WriteLine("[Connect] clientId:{0} code:{1} message:{2}", ((uint)args[0]).ToString(), ((int)args[1]).ToString(), (string)args[2]);
                break;
            case pNet.Server.EventType.Disconnect:
                Console.WriteLine("[Disconnect] clientId:{0} code:{1} message:{2} reasonCode:{3}", ((uint)args[0]).ToString(), ((int)args[1]).ToString(), (string)args[2], ((byte)args[3]).ToString());
                break;
            case pNet.Server.EventType.Receive:
                {
                    uint clientId = (uint)args[0];
                    byte[] data = (byte[])args[1];
                    pNet.Server.PacketType packetType = (pNet.Server.PacketType)args[3];

                    if (packetType == pNet.Server.PacketType.Reliable)
                    {
                        SerializableArray dataArray = SerializableArray.FromDeserialize(data);
                        if (dataArray == null) { throw new Exception("Cannot deserialize a data packet that received as reliable!"); }

                        byte flag = dataArray.GetByte(0);
                        ushort packetId = dataArray.GetUshort(1);
                        string message = dataArray.GetString(2);

                        if (flag != 123)
                        {
                            Console.WriteLine("Reliable Unordered Packet #{0}: {1}", packetId.ToString(), message);
                            return;
                        }

                        byte[] messageRaw = Encoding.UTF8.GetBytes(message);
                        Console.WriteLine("Reliable Ordered Packet #{0}", packetId.ToString());
                        orderer.Handle(clientId,packetId, messageRaw);
                    }
                }
                break;
            case pNet.Server.EventType.Send:
                Console.WriteLine("[Send] code:{0} message:{1} clientId:{2}", ((int)args[0]).ToString(), (string)args[1], (uint)args[2]);
                break;
            case pNet.Server.EventType.PingUpdate:
                //Console.WriteLine("[PingUpdate] clientId:{0} ping:{1}", ((uint)args[0]).ToString(), ((uint)args[1]).ToString());
                break;
        }
    }

    static void ClientEventHandler(pNet.Client.EventType type, object[] args)
    {
        switch (type)
        {
            case pNet.Client.EventType.Connect:
                Console.WriteLine("[Connect] code:{0} message:{1} reasonCode:{2}", ((int)args[0]).ToString(), (string)args[1], ((byte)args[2]).ToString());
                break;
            case pNet.Client.EventType.Disconnect:
                Console.WriteLine("[Disconnect] code:{0} message:{1} reasonCode:{2}", ((int)args[0]).ToString(), (string)args[1], ((byte)args[2]).ToString());
                break;
            case pNet.Client.EventType.Receive:
                Console.WriteLine("[Receive] dataLenght:{0} packetType:{1}", ((int)args[1]).ToString(), ((pNet.Client.PacketType)args[2]).ToString());
                break;
            case pNet.Client.EventType.Send:
                Console.WriteLine("[Send] code:{0} message:{1}", ((int)args[0]).ToString(), (string)args[1]);
                break;
            case pNet.Client.EventType.OptionChange:
                Console.WriteLine("[OptionChange] code:{0} message:{1} option:{2}", ((int)args[0]).ToString(), (string)args[1], ((ClientOption)args[2]).ToString());
                break;
        }
    }
}