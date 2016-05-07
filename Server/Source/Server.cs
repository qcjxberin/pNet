/*
 * Client + Server: Protocol documentation OK
 * Server Callbacks OK
 * Server SetOption OK
 * Socket I/O Start/Stop (with socket io) OK
 * Socket I/O Callbacks OK
 * Peer setup + Login (with callbacks) OK
 * TimedJob + BlockingQueue classes are ready! OK
 * Client: Handle connection response (+reason code etc.) OK
 * Server.Peer: Pinging OK
 * Server.Peer: Drop -Timeout/Error/HighPing OK
 * Client: Handle ping + calculated ping + ping timeout OK
 * Client: Drop -Error OK
 * Client: Drop -Generic reason = Disconnect with 'reasonCode' OK
 * Server: Drop -Generic reason = Kick with 'reasonCode' OK
 * Client: MaxConnRetryCount + ConnRetryInterval option OK
 * Client: Unreliable send + server handle OK
 * Server: Unreliable send + client handle OK
 * Server + Client: 'Try/Catch' blocks in 'public' methods! OK
 * Client: IClientHandler overrides + documentation OK
 * Server: IServerHandler overrides + documentation OK
 * ***** >>> [( The END! )] <<< *****
 * 
 * >> IEvent calls!
 */
using pMd5;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

namespace pNet.Server
{
    public delegate byte ILogin(uint clientId, EndPoint endPoint, byte[] bytes);
    public delegate void IEvent(EventType type, object[] args);
    public enum EventType { Start, Stop, OptionChange, Connect, Disconnect, Receive, Send, PingUpdate }
    public enum PacketType { Reliable, Unreliable }
    public enum ServerOption { Port, MaxClients, BufferSize, MaxPing, PingInterval, PingTimeout, MaxSendQueueSize, SendQueueInterval, PeerDataCapacity }
    public sealed class Server
    {
        #region Employees

        private bool _Status = false;

        private IEvent _EventHandler = null;
        private ILogin _LoginHandler = null;

        private SocketIO _SocketIO = null;

        private Dictionary<string, uint> _Clients = new Dictionary<string, uint>();
        private Dictionary<uint, Peer> _Peers = new Dictionary<uint, Peer>();

        private ArrayList _AvailableIds = new ArrayList();

        #region Protocol Variables

        private int _Port = 9696;
        private int _MaxClients = 256;
        private int _BufferSize = 256;
        private int _MaxPing = 500;
        private int _PingInterval = 1000;
        private int _PingTimeout = 5000;
        private int _MaxSendQueueSize = 128;
        private int _SendQueueInterval = 500;
        private int _PeerDataCapacity = 1024;

        #endregion

        #endregion

        #region Public Members

        public Server(IEvent eventHandler, ILogin loginHandler)
        {
            try
            {
                if (eventHandler == null) { throw new ArgumentNullException("eventHandler", "'Server' requires a valid 'IEvent' function!"); }
                if (loginHandler == null) { throw new ArgumentNullException("loginHandler", "'Server' requires a valid 'IEvent' function!"); }
                _EventHandler = eventHandler;
                _LoginHandler = loginHandler;
                _SocketIO = new SocketIO(_HandleSocketIOEvent);
            }
            catch
            {
                throw new Exception("An unexpected error occured!");
            }
        }

        public bool Status { get { return _Status; } }

        public void Start()
        {
            try
            {
                try { for (uint i = 1; i < 10001; i++) { _AvailableIds.Add((uint)i); } }
                catch
                {
                    object[] a1 = new object[2];
                    a1[0] = (int)6;
                    a1[1] = "An error occured!";
                    _FireEvent(EventType.Start, a1);
                    return;
                }
                byte _StartResult = _SocketIO.Start(_Port, _BufferSize);
                object[] a = new object[2];
                a[0] = (int)_StartResult;
                switch (_StartResult)
                {
                    case 0:
                        _Status = true;
                        a[1] = "Started.";
                        break;
                    case 1:
                        a[1] = "Already started!";
                        break;
                    case 2:
                        a[1] = "Cannot create socket!";
                        break;
                    case 3:
                        a[1] = "Cannot create listen thread!";
                        break;
                    case 4:
                        a[1] = "Cannot bind socket!";
                        break;
                    case 5:
                        a[1] = "Cannot start listen thread!";
                        break;
                    case 6:
                        a[1] = "An error occured!";
                        break;
                }
                _FireEvent(EventType.Start, a);
            }
            catch { object[] a1 = { (int)6, "An error occured!" }; _FireEvent(EventType.Start, a1); _SocketIO.Stop(); }
        }

        public void Stop()
        {
            bool s = _SocketIO.Stop();

            if (_Peers != null)
            {
                lock (_Peers)
                {
                    foreach (KeyValuePair<uint, Peer> kvp in _Peers)
                    {
                        object[] x = { kvp.Value.Id, (int)10, "Server stopped!", (byte)0 };
                        _FireEvent(EventType.Disconnect, x);
                        kvp.Value.Close();
                    }
                }
            }
            _Peers.Clear();

            if (s)
            {
                object[] a = { 0, "Server stopped." };
                _FireEvent(EventType.Stop, a);
            }
            else
            {
                object[] a = { 1, "Server already stopped!" };
                _FireEvent(EventType.Stop, a);
            }
        }

        public void Send(uint id, byte[] data, PacketType type)
        {
            try
            {
                if (!_Status)
                {
                    object[] a = { (int)0, "Server must be running to send data!", (uint)0 };
                    _FireEvent(EventType.Send, a);
                    return;
                }
                if (id == 0) { object[] a = { (int)2, "Client not found!", (uint)0 }; _FireEvent(EventType.Send, a); return; }
                if (data == null || data.Length == 0) { object[] a = { (int)1, "Cannot perform a successful send operation because data is null or empty (zero length)!", (uint)0 }; _FireEvent(EventType.Send, a); return; }
                Peer peer = null;
                lock (_Peers) { if (!_Status) { return; } if (_Peers.ContainsKey(id)) { peer = _Peers[id]; } else { object[] a = { (int)2, "Client not found!", (uint)id }; _FireEvent(EventType.Send, a); return; } }
                switch (type)
                {
                    case PacketType.Reliable:
                        peer.EnqueueOutgoingData(data);
                        break;
                    case PacketType.Unreliable:
                        byte[] _Out = new byte[data.Length + 1];
                        _Out[0] = (byte)122;
                        for (int i = 0; i < data.Length; i++) { _Out[i + 1] = data[i]; }
                        _SocketIO.Send(peer.EndPoint, _Out);
                        break;
                }
            }
            catch
            {
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
                object[] a = { (int)2, "An error occured!", (uint)0 }; _FireEvent(EventType.Stop, a);
            }
        }

        public void Send(uint[] id, byte[] data, PacketType type)
        {
            try
            {
                if (!_Status)
                {
                    object[] a = { (int)0, "Server must be running to send data!", (uint)0 };
                    _FireEvent(EventType.Send, a);
                    return;
                }
                if (id == null || id.Length == 0) { object[] a = { (int)2, "Client not found!", (uint)0 }; _FireEvent(EventType.Send, a); return; }
                if (data == null || data.Length == 0) { object[] a = { (int)1, "Cannot perform a successful send operation because data is null or empty (zero length)!", (uint)0 }; _FireEvent(EventType.Send, a); return; }
                lock (_Peers)
                {
                    Peer peer = null;
                    foreach (uint clientId in id)
                    {
                        if (!_Status) { return; }
                        if (_Peers.ContainsKey(clientId))
                        {
                            peer = _Peers[clientId];
                            switch (type)
                            {
                                case PacketType.Reliable:
                                    peer.EnqueueOutgoingData(data);
                                    break;
                                case PacketType.Unreliable:
                                    byte[] _Out = new byte[data.Length + 1];
                                    _Out[0] = (byte)122;
                                    for (int i = 0; i < data.Length; i++) { _Out[i + 1] = data[i]; }
                                    _SocketIO.Send(peer.EndPoint, _Out);
                                    break;
                            }
                        }
                        else
                        {
                            object[] a = { (int)2, "Client not found!", (uint)clientId };
                            _FireEvent(EventType.Send, a);
                        }
                    }
                }
            }
            catch
            {
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
                object[] a = { (int)2, "An error occured!", (uint)0 }; _FireEvent(EventType.Stop, a);
            }
        }

        public void SendToAll(byte[] data, PacketType type)
        {
            try
            {
                if (!_Status)
                {
                    object[] a = { (int)0, "Server must be running to send data!" };
                    _FireEvent(EventType.Send, a);
                    return;
                }
                if (data == null || data.Length == 0) { object[] a = { (int)1, "Cannot perform a successful send operation because data is null or empty (zero length)!" }; _FireEvent(EventType.Send, a); return; }
                lock (_Peers)
                {
                    foreach (KeyValuePair<uint, Peer> kvp in _Peers)
                    {
                        switch (type)
                        {
                            case PacketType.Reliable:
                                kvp.Value.EnqueueOutgoingData(data);
                                break;
                            case PacketType.Unreliable:
                                byte[] _Out = new byte[data.Length + 1];
                                _Out[0] = (byte)122;
                                for (int i = 0; i < data.Length; i++) { _Out[i + 1] = data[i]; }
                                _SocketIO.Send(kvp.Value.EndPoint, _Out);
                                break;
                        }
                    }
                }
            }
            catch
            {
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
                object[] a = { (int)2, "An error occured!" }; _FireEvent(EventType.Stop, a);
            }
        }

        public void Drop(uint id, byte reasonCode)
        {
            try
            {
                lock (_Peers)
                {
                    if (_Peers.ContainsKey(id))
                    {
                        Peer peer = _Peers[id];
                        peer.Close();
                        byte[] _Out = { 121, reasonCode };
                        _SocketIO.Send(peer.EndPoint, _Out);
                        lock (_Clients) { _Clients.Remove(peer.EndPoint.ToString()); }
                        _Peers.Remove(id);
                        object[] a = { id, (int)7, "Server dropped the connection!", (byte)0 };
                        _FireEvent(EventType.Disconnect, a);
                    }
                }
            }
            catch
            {
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
                object[] a = { (int)2, "An error occured!" };
                _FireEvent(EventType.Stop, a);
            }
        }

        public void SetOption(ServerOption option, int value)
        {
            try
            {
                if (_Status)
                {
                    object[] a1 = { (int)99, "Server must be stopped to change any option!", option };
                    _FireEvent(EventType.OptionChange, a1);
                    return;
                }
                object[] a = new object[3];
                a[2] = option;
                switch (option)
                {
                    case ServerOption.Port:
                        if (value < 1) { a[0] = 1; a[1] = "Port number must be higher than zero!"; }
                        else if (value > 65535) { a[0] = 2; a[1] = "Port number must be lower than 65536!"; }
                        else { _Port = value; a[0] = 0; a[1] = "Port number changed."; }
                        break;
                    case ServerOption.MaxClients:
                        if (value < 1) { a[0] = 1; a[1] = "MaxClients must be higher than zero!"; }
                        else if (value > 10000) { a[0] = 2; a[1] = "MaxClients must be lower than 10001!"; }
                        else { _MaxClients = value; a[0] = 0; a[1] = "MaxClients changed."; }
                        break;
                    case ServerOption.BufferSize:
                        if (value < 128) { a[0] = 1; a[1] = "BufferSize must be higher than 127!";}
                        else if (value > 4096) { a[0] = 2; a[1] = "BufferSize must be lower than 4097!"; }
                        else { _BufferSize = value; a[0] = 0; a[1] = "BufferSize changed."; }
                        break;
                    case ServerOption.MaxPing:
                        if (value < 100) { a[0] = 1; a[1] = "MaxPing must be higher than 99!"; }
                        else if (value > 1000) { a[0] = 2; a[1] = "MaxPing must be lower than 1001!"; }
                        else { _MaxPing = value; a[0] = 0; a[1] = "MaxPing changed."; }
                        break;
                    case ServerOption.PingInterval:
                        if (value < 200) { a[0] = 1; a[1] = "PingInterval must be higher than 199!"; }
                        else if (value > 2000) { a[0] = 2; a[1] = "PingInterval must be lower than 2001!"; }
                        else { _PingInterval = value; a[0] = 0; a[1] = "PingInterval changed."; }
                        break;
                    case ServerOption.PingTimeout:
                        if (value < 2500) { a[0] = 1; a[1] = "PingTimeout must be higher than 2499!"; }
                        else if (value > 10000) { a[0] = 2; a[1] = "PingTimeout must be lower than 10001!"; }
                        else { _PingTimeout = value; a[0] = 0; a[1] = "PingTimeout changed."; }
                        break;
                    case ServerOption.MaxSendQueueSize:
                        if (value < 32) { a[0] = 1; a[1] = "MaxSendQueueSize must be higher than 31!"; }
                        else if (value > 1024) { a[0] = 2; a[1] = "MaxSendQueueSize must be lower than 1025!"; }
                        else { _MaxSendQueueSize = value; a[0] = 0; a[1] = "MaxSendQueueSize changed."; }
                        break;
                    case ServerOption.SendQueueInterval:
                        if (value < 1) { a[0] = 1; a[1] = "SendQueueInterval must be higher than zero!"; }
                        else if (value > 2000) { a[0] = 2; a[1] = "SendQueueInterval must be lower than 2001!"; }
                        else { _SendQueueInterval = value; a[0] = 0; a[1] = "SendQueueInterval changed."; }
                        break;
                    case ServerOption.PeerDataCapacity:
                        if (value < 32) { a[0] = 1; a[1] = "PeerDataCapacity must be higher than 31!"; }
                        else if (value > 4096) { a[0] = 2; a[1] = "PeerDataCapacity must be lower than 4097!"; }
                        else { _PeerDataCapacity = value; a[0] = 0; a[1] = "PeerDataCapacity changed."; }
                        break;
                }
                _FireEvent(EventType.OptionChange, a);
            }
            catch
            {
                object[] a = { (int)2, "An error occured!" };
                _FireEvent(EventType.Stop, a);
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
            }
        }

        #endregion

        #region Core

        private class _Event
        {
            public EventType Type;
            public object[] Args;

            public _Event(EventType type, object[] args)
            {
                Type = type;
                Args = args;
            }
        }
        private void _FireEvent(EventType type, object[] args)
        {
            try
            {
                Thread thr = new Thread(_EventProcess);
                thr.Start(new _Event(type, args));
            }
            catch
            {
                object[] a = { (int)2, "An error occured!" };
                _EventHandler(EventType.Stop, a);
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
            }
        }
        private void _EventProcess(object e)
        {
            _Event evt = (_Event)e;
            _EventHandler(evt.Type, evt.Args);
        }

        private void _HandleSocketIOEvent(byte code, object[] args)
        {
            try
            {
                switch (code)
                {
                    case 0://Receive
                        _ProcessData((EndPoint)args[0], (byte[])args[1]);
                        break;
                    case 1://Close
                        object[] a = { (int)2, "An error occured!" };
                        _FireEvent(EventType.Stop, a);
                        _Status = false;
                        lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                        lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
                        break;
                }
            }
            catch
            {
                object[] a = { (int)2, "An error occured!" };
                _FireEvent(EventType.Stop, a);
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
            }
        }

        private void _HandlePeerEvent(Peer peer, byte code, object[] args)
        {
            switch (code)
            {
                case 0://Drop
                    lock (_Clients) { _Clients.Remove(peer.EndPoint.ToString()); }
                    lock (_Peers) { _Peers.Remove(peer.Id); }
                    object[] a = new object[4];
                    a[0] = peer.Id;
                    a[3] = (byte)0;
                    switch ((int)args[0])
                    {
                        case 1://error -s
                            a[1] = 2;
                            a[2] = "An error occured! (server-side)";
                            break;
                        case 2://network locked -s
                            a[1] = 4;
                            a[2] = "Network locked! (server-side)";
                            break;
                        case 3:
                            a[1] = 6;
                            a[2] = "Connection timed out! (server-side)";
                            break;
                        case 4:
                            a[1] = 8;
                            a[2] = "Too high ping! (server-side)";
                            break;
                        case 5:
                            a[1] = 1;
                            a[2] = "An error occured! (client-side)";
                            break;
                        case 6:
                            a[1] = 5;
                            a[2] = "Connection timed out! (client-side)";
                            break;
                        case 7:
                            a[1] = 3;
                            a[2] = "Network locked! (client-side)";
                            break;
                        case 8:
                            a[1] = 0;
                            a[3] = (byte)args[1];
                            a[2] = "Client disconnected.";
                            break;
                    }
                    lock (_AvailableIds) { if(!_AvailableIds.Contains(peer.Id)) { _AvailableIds.Add(peer.Id); } }
                    _FireEvent(EventType.Disconnect, a);
                    break;
                case 1://Receive
                    byte[] inData = (byte[])args[0];
                    switch ((int)args[1])
                    {
                        case 0://Reliable
                            object[] a1 = { peer.Id, inData, inData.Length, PacketType.Reliable };
                            _FireEvent(EventType.Receive, a1);
                            break;
                        case 1://Unreliable
                            object[] a2 = { peer.Id, inData, inData.Length, PacketType.Unreliable };
                            _FireEvent(EventType.Receive, a2);
                            break;
                    }
                    break;
                case 2://Send
                    _SocketIO.Send(peer.EndPoint, (byte[])args[0]);
                    break;
                case 3://Ping update
                    object[] a3 = { peer.Id, args[0] };
                    _FireEvent(EventType.PingUpdate, a3);
                    break;
            }
        }

        private void _ProcessData(EndPoint sender, byte[] data)
        {
            try
            {
                int dataLength = data.Length;
                lock (_Clients)
                {
                    if (!_Status) { return; }
                    if (_Clients.ContainsKey(sender.ToString())) { lock (_Peers) { lock (_Clients) { _Peers[_Clients[sender.ToString()]].EnqueueIncomingData(data); } } }
                    else if (data[0] == (byte)111)
                    {
                        if (_Clients.Count > _MaxClients)
                        {
                            byte[] _Out = { 113 };
                            _SocketIO.Send(sender, _Out);
                            return;
                        }

                        lock (_AvailableIds)
                        {
                            uint availableId = (uint)_AvailableIds[0];
                            _AvailableIds.RemoveAt(0);

                            if (dataLength == 1)
                            {
                                try
                                {
                                    Thread thr = new Thread(_HandleLogin);
                                    thr.Start(new _LoginData(availableId, sender, null));
                                }
                                catch { _AvailableIds.Add(availableId); }
                            }
                            else if (dataLength > 33)
                            {
                                try
                                {
                                    byte[] checkSum = new byte[32];
                                    Array.Copy(data, 1, checkSum, 0, 32);
                                    byte[] bytes = new byte[dataLength - 33];
                                    Array.Copy(data, 33, bytes, 0, dataLength - 33);
                                    if (Encoding.UTF8.GetString(checkSum) != Md5Calculator.CalculateMd5String(bytes)) { return; }
                                    Thread thr = new Thread(_HandleLogin);
                                    thr.Start(new _LoginData(availableId, sender, bytes));
                                }
                                catch { _AvailableIds.Add(availableId); }
                            }
                        }
                    }
                    /* OLD else if (data[0] == (byte)111)
                    {
                        if (dataLength == 1)
                        {
                            if (_Clients.Count > _MaxClients)
                            {
                                byte[] _Out = { 113 };
                                _SocketIO.Send(sender, _Out);
                                return;
                            }
                            uint availableId = 0;
                            lock (_AvailableIds)
                            {
                                availableId = (uint)_AvailableIds[0];
                                byte loginResult = _LoginHandler(availableId, sender, null);
                                if (loginResult == 0)
                                {
                                    object[] a = new object[3];
                                    a[0] = availableId;
                                    Peer clientPeer = new Peer(_HandlePeerEvent);
                                    if (clientPeer.Setup(availableId, sender, _BufferSize, _MaxPing, _PingInterval, _PingTimeout, _MaxSendQueueSize, _SendQueueInterval, _PeerDataCapacity))
                                    {
                                        _AvailableIds.RemoveAt(0);
                                        lock (_Peers) { _Peers.Add(availableId, clientPeer); }
                                        _Clients.Add(sender.ToString(), availableId);
                                        a[1] = 0;
                                        a[2] = "Client connected.";
                                    }
                                    else
                                    {
                                        lock (_AvailableIds) { if (!_AvailableIds.Contains(availableId)) { _AvailableIds.Add(availableId); } }
                                        a[1] = 1;
                                        a[2] = "Client peer setup failed!";
                                    }
                                    _FireEvent(EventType.Connect, a);
                                }
                                else
                                {
                                    byte[] _Out = { 114, loginResult };
                                    _SocketIO.Send(sender, _Out);
                                }
                            }
                        }
                        else if (dataLength > 33)//contains bytes!
                        {
                            byte[] checkSum = new byte[32];
                            Array.Copy(data, 1, checkSum, 0, 32);
                            byte[] bytes = new byte[dataLength - 33];
                            Array.Copy(data, 33, bytes, 0, dataLength - 33);
                            if (Encoding.UTF8.GetString(checkSum) == Md5Calculator.CalculateMd5String(bytes))
                            {
                                if (_Clients.Count > _MaxClients)
                                {
                                    byte[] _Out = { 113 };
                                    _SocketIO.Send(sender, _Out);
                                    return;
                                }
                                uint availableId = 0;
                                lock (_AvailableIds)
                                {
                                    availableId = (uint)_AvailableIds[0];
                                    byte loginResult = _LoginHandler(availableId, sender, bytes);
                                    if (loginResult == 0)
                                    {
                                        object[] a = new object[3];
                                        a[0] = availableId;
                                        Peer clientPeer = new Peer(_HandlePeerEvent);
                                        if (clientPeer.Setup(availableId, sender, _BufferSize, _MaxPing, _PingInterval, _PingTimeout, _MaxSendQueueSize, _SendQueueInterval, _PeerDataCapacity))
                                        {
                                            _AvailableIds.RemoveAt(0);
                                            lock (_Peers) { _Peers.Add(availableId, clientPeer); }
                                            _Clients.Add(sender.ToString(), availableId);
                                            a[1] = 0;
                                            a[2] = "Client connected.";
                                        }
                                        else
                                        {
                                            lock (_AvailableIds) { if (!_AvailableIds.Contains(availableId)) { _AvailableIds.Add(availableId); } }
                                            a[1] = 1;
                                            a[2] = "Client peer setup failed!";
                                        }
                                        _FireEvent(EventType.Connect, a);
                                    }
                                    else
                                    {
                                        byte[] _Out = { 114, loginResult };
                                        _SocketIO.Send(sender, _Out);
                                    }
                                }
                            }
                        }
                    }*/
                }
            }
            catch
            {
                object[] a = { (int)2, "An error occured!" };
                _EventHandler(EventType.Stop, a);
                _SocketIO.Stop();
                _Status = false;
                lock (_Peers) { if (_Peers != null) { foreach (KeyValuePair<uint, Peer> o in _Peers) { o.Value.Close(); } _Peers.Clear(); } }
                lock (_Clients) { if (_Clients != null) { _Clients.Clear(); } }
            }
        }

        private class _LoginData
        {
            public uint ClientId;
            public EndPoint EndPoint;
            public byte[] Bytes;

            public _LoginData(uint clientId, EndPoint endPoint, byte[] bytes)
            {
                ClientId = clientId;
                EndPoint = endPoint;
                Bytes = bytes;
            }
        }
        private void _HandleLogin(object arg)
        {
            _LoginData loginData = (_LoginData)arg;
            try
            {
                byte loginResult = _LoginHandler(loginData.ClientId, loginData.EndPoint, loginData.Bytes);
                if (loginResult == 0)
                {
                    object[] a = new object[3];
                    a[0] = loginData.ClientId;
                    Peer clientPeer = new Peer(_HandlePeerEvent);
                    if (clientPeer.Setup(loginData.ClientId, loginData.EndPoint, _BufferSize, _MaxPing, _PingInterval, _PingTimeout, _MaxSendQueueSize, _SendQueueInterval, _PeerDataCapacity))
                    {
                        lock (_Peers) { _Peers.Add(loginData.ClientId, clientPeer); }
                        lock (_Clients) { _Clients.Add(loginData.EndPoint.ToString(), loginData.ClientId); }
                        a[1] = 0;
                        a[2] = "Client connected.";
                    }
                    else
                    {
                        lock (_AvailableIds) { if (!_AvailableIds.Contains(loginData.ClientId)) { _AvailableIds.Add(loginData.ClientId); } }
                        a[1] = 1;
                        a[2] = "Client peer setup failed!";
                    }
                    _FireEvent(EventType.Connect, a);
                }
                else
                {
                    byte[] _Out = { 114, loginResult };
                    _SocketIO.Send(loginData.EndPoint, _Out);
                }
            }
            catch { lock (_AvailableIds) { _AvailableIds.Add(loginData.ClientId); } }
        }

        #endregion
    }
}