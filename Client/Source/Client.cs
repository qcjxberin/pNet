using pMd5;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace pNet.Client
{
    public delegate void IEvent(EventType type, object[] args);
    public enum EventType { Connect, Disconnect, Receive, Send, OptionChange }
    public enum PacketType { Reliable, Unreliable }
    public enum ClientOption { MaxConnRetryCount, ConnRetryInterval, PingTimeout, MaxSendQueueSize, SendQueueInterval }
    public sealed class Client
    {
        #region Employees

        private bool _Status = false;
        private bool _ConnOp = false;

        private IEvent _EventHandler = null;

        private Socket _Socket = null;
        private Thread _Thread = null;
        private EndPoint _RemoteEp = null;

        private uint _Id = 0;

        private ArrayList _RPacketList = null;

        #region Connection

        private TimedJob _ConnJob = null;
        private byte[] _ConnReq = null;
        private int _ConnTries = 0;

        #endregion

        #region Ping

        private uint _LastPing = 0;
        private TimedJob _PingTimeoutJob = null;

        #endregion

        #region Reliability

        private TimedJob _ReliabilityJob = null;
        private Dictionary<int, ReliablePacket> _OutgoingPackets = null;
        private int _LastPacketId = 65500;

        #endregion

        #region Protocol Variables

        private int _BufferSize = 128;
        private int _MaxConnRetryCount = 5;
        private int _ConnRetryInterval = 1000;
        private int _PingTimeout = 5000;
        private int _MaxSendQueueSize = 128;
        private int _SendQueueInterval = 500;

        #endregion

        #endregion

        #region Public Members

        public Client(IEvent eventHandler)
        {
            try
            {
                if (eventHandler == null) { throw new ArgumentNullException("eventHandler", "'Client' requires a valid 'IEvent' function!"); }
                _EventHandler = eventHandler;
            }
            catch
            {
                throw new Exception("An unexpected error occured!");
            }
        }

        public bool Status { get { return _Status; } }

        public uint Id { get { return _Id; } }

        public uint Ping { get { return _LastPing; } }

        public void Connect(EndPoint remoteEp, byte[] bytes)
        {
            if (_Status || _ConnOp) { object[] a = { (int)6, "Client must be disconnected to establish another connection!", (byte)0 }; _FireEvent(EventType.Connect, a); return; }
            if (remoteEp == null) { object[] a = { (int)7, "'remoteEp' must be a valid 'EndPoint' instance!", (byte)0 }; _FireEvent(EventType.Connect, a); return; }
            //username null or empty pass -> (8, "Username cannot be null or empty!")
            //password null or empty pass -> (9, "Password cannot be null or empty!")
            //username length pass -> (10, "Username must be shorter than 33 chars length!")
            //password length pass -> (11, "Password must be shorter than 33 chars length!")
            if (bytes != null)
            {
                if (bytes.Length == 0) { object[] a = { (int)8, "Input bytes are not null but empty! (Zero length? Pass 'null' instead.)", (byte)0 }; _FireEvent(EventType.Connect, a); return; }
                if (bytes.Length > 96) { object[] a = { (int)9, "Input bytes are too long, must be shorter than 97 bytes length!", (byte)0 }; _FireEvent(EventType.Connect, a); return; }
            }
            try
            {
                _Reset();
                _ConnOp = true;
                _RemoteEp = remoteEp;
                _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _Socket.ReceiveTimeout = 0;
                _Socket.SendTimeout = 0;
                _Socket.ReceiveBufferSize = _BufferSize;
                _Socket.SendBufferSize = _BufferSize;
                _RPacketList = new ArrayList();
                try { _Socket.Bind(new IPEndPoint(IPAddress.Any, 0)); }
                catch { object[] a = { (int)10, "Cannot bind socket!", 0 }; _FireEvent(EventType.Connect, a); _Reset(); return; }
                try
                {
                    _Thread = new Thread(_Listen);
                    _Thread.Start();
                }
                catch { object[] a = { (int)1, "An error occured! (client-side)", 0 }; _FireEvent(EventType.Connect, a); _Reset(); return; }
                if (bytes == null)
                {
                    _ConnReq = new byte[1];
                    _ConnReq[0] = (byte)111;
                }
                else
                {
                    _ConnReq = new byte[33 + bytes.Length];
                    _ConnReq[0] = (byte)111;
                    Array.Copy(Md5Calculator.CalculateMd5Bytes(bytes), 0, _ConnReq, 1, 32);
                    Array.Copy(bytes, 0, _ConnReq, 33, bytes.Length);
                }
                try { _ConnJob = new TimedJob(_HandleConnection, _ConnRetryInterval, null); }
                catch { object[] a = { (int)1, "An error occured! (client-side)", 0 }; _FireEvent(EventType.Connect, a); _Reset(); return; }
                _HandleConnection(null);
            }
            catch { object[] a = { (int)1, "An error occured! (client-side)", 0 }; _FireEvent(EventType.Connect, a); _Reset(); }
        }

        public void Disconnect(byte reasonCode)
        {
            try
            {
                if (_Status)
                {
                    byte[] _Out = { 121, reasonCode };
                    _Send(_Out);
                    object[] a = { (int)0, "Disconnected.", (byte)0 };
                    _FireEvent(EventType.Disconnect, a);
                    _Reset();
                    return;
                }

                if (_ConnOp)
                {
                    byte[] _Out = { 121, reasonCode };
                    _Send(_Out);
                    object[] a = { (int)10, "Pending connection request cancalled!", (byte)0 };
                    _FireEvent(EventType.Disconnect, a);
                    _Reset();
                    return;
                }
                object[] a1 = { (int)9, "Already disconnected!", (byte)0 };
                _FireEvent(EventType.Disconnect, a1);
                _Reset();
            }
            catch
            { if (_Status) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Disconnect, a); } }
        }

        public void Send(byte[] data, PacketType type)
        {
            try
            {
                if (!_Status) { object[] a = { (int)0, "Client must be connected to send data!" }; _FireEvent(EventType.Send, a); return; }
                if (data == null || data.Length == 0) { object[] a = { (int)1, "Cannot perform a successful send operation because data is null or empty (zero length)!" }; _FireEvent(EventType.Send, a); return; }
                switch (type)
                {
                    case PacketType.Reliable:
                        ReliablePacket rPacket = new ReliablePacket(_LastPacketId, data);
                        _Send(rPacket.RawData);
                        lock (_OutgoingPackets)
                        {
                            if (_OutgoingPackets.Count + 1 > _MaxSendQueueSize)
                            {
                                byte[] _OutR = { 119 };
                                _Send(_OutR);
                                object[] a = { 3, "Network locked! (client-side)", (byte)0 };
                                _FireEvent(EventType.Disconnect, a);
                                _Reset();
                                return;
                            }
                            if (_OutgoingPackets.ContainsKey(_LastPacketId)) { _OutgoingPackets.Remove(_LastPacketId); }
                            _OutgoingPackets.Add(_LastPacketId, rPacket);
                        }
                        if (_LastPacketId > 65534) { _LastPacketId = 0; } else { _LastPacketId++; }
                        break;
                    case PacketType.Unreliable:
                        byte[] _Out = new byte[data.Length + 1];
                        _Out[0] = (byte)122;
                        for (int i = 0; i < data.Length; i++) { _Out[i + 1] = data[i]; }
                        _Send(_Out);
                        break;
                }
            }
            catch
            {
                if (_Status) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Disconnect, a); }
                _Reset();
            }
        }

        public void SetOption(ClientOption option, int value)
        {
            try
            {
                if (_Status || _ConnOp) { object[] a = { (int)99, "Client must be disconnected to change any option!", (ClientOption)option }; _FireEvent(EventType.OptionChange, a); return; }
                object[] args = new object[3];
                args[2] = (ClientOption)option;
                switch (option)
                {
                    case ClientOption.MaxConnRetryCount://1-10
                        if (value < 1) { args[0] = (int)1; args[1] = "MaxConnRetryCount must be higher than zero!"; }
                        else if (value > 10) { args[0] = (int)2; args[1] = "MaxConnRetryCount must be lower than 11!"; }
                        else { _MaxConnRetryCount = value; args[0] = (int)0; args[1] = "MaxConnRetryCount changed."; }
                        break;
                    case ClientOption.ConnRetryInterval://500-3000
                        if (value < 500) { args[0] = (int)1; args[2] = "ConnRetryInterval must be higher than 499!"; }
                        else if (value > 3000) { args[0] = (int)2; args[1] = "ConnRetryInterval must be lower than 3001!"; }
                        else { _ConnRetryInterval = value; args[0] = (int)0; args[1] = "ConnRetryInterval changed."; }
                        break;
                    case ClientOption.PingTimeout://2500-10000
                        if (value < 2500) { args[0] = (int)1; args[1] = "PingTimeout must be higher than 2499!"; }
                        else if (value > 10000) { args[0] = (int)2; args[1] = "PingTimeout must be lower than 10001!"; }
                        else { _PingTimeout = value; args[0] = (int)0; args[1] ="PingTimeout changed."; }
                        break;
                    case ClientOption.MaxSendQueueSize://32-1024
                        if (value < 32) { args[0] = (int)1; args[1] = "MaxSendQueueSize must be higher than 31!"; }
                        else if (value > 1024) { args[0] = (int)2; args[1] = "MaxSendQueueSize must be lower than 1025!"; }
                        else { _PingTimeout = value; args[0] = (int)0; args[1] = "MaxSendQueueSize changed."; }
                        break;
                    case ClientOption.SendQueueInterval://100-2000
                        if (value < 1) { args[0] = (int)1; args[1] = "SendQueueInterval must be higher than zero!"; }
                        else if (value > 2000) { args[0] = (int)2; args[1] = "SendQueueInterval must be lower than 2001!"; }
                        else { _PingTimeout = value; args[0] = (int)0; args[1] = "SendQueueInterval changed."; }
                        break;
                }
                _FireEvent(EventType.OptionChange, args);
            }
            catch
            {
                if (_Status) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Disconnect, a); }
                _Reset();
            }
        }

        public void Close()
        {
            try
            {
                _Reset();
                _EventHandler = null;
            }
            catch { }
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
                object[] a = { (int)2, "An error occured! (server-side)" };
                _EventHandler(EventType.Disconnect, a);
                _Reset();
            }
        }

        private void _EventProcess(object e)
        {
            _Event evt = (_Event)e;
            _EventHandler(evt.Type, evt.Args);
        }

        private void _Reset()
        {
            try
            {
                if (_Socket != null) { try { _Socket.Close(); } catch { } }
                _Id = 0;
                _LastPing = 0;
                _BufferSize = 128;
                _ConnTries = 0;
                _Status = false;
                _ConnOp = false;
                if (_Thread != null) { if (_Thread.IsAlive) { try { _Thread.Abort(); } catch { } } }
                _ConnJob.Stop();
                _PingTimeoutJob.Stop();
                _ReliabilityJob.Stop();
            }
            catch { }
        }

        private void _Send(byte[] data)
        {
            try { _Socket.SendTo(data, 0, data.Length, SocketFlags.None, _RemoteEp); }
            catch (ObjectDisposedException)
            {
                if (_ConnOp) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Connect, a); }
                else if (_Status) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Disconnect, a); }
                _Reset();
            }
            catch { }
        }

        private void _Listen()
        {
            byte[] _Buffer = new byte[_BufferSize];
            EndPoint _InEp = new IPEndPoint(IPAddress.Any, 0);
            int _RecSize = 0;
            while (_Status || _ConnOp)
            {
                try { _RecSize = _Socket.ReceiveFrom(_Buffer, 0, _BufferSize, SocketFlags.None, ref _InEp); }
                catch (ObjectDisposedException)
                {
                    if (_ConnOp) { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Connect, a); }
                    else { object[] a = { (int)1, "An error occured! (client-side)", (byte)0 }; _FireEvent(EventType.Disconnect, a); }
                }
                catch { continue; }
                if (!_Status && !_ConnOp) { break; }
                if (_RecSize > 0)
                {
                    if (_ConnOp)
                    {
                        if (_RecSize == 1 && _Buffer[0] == (byte)113)
                        {
                            object[] a = { (int)3, "Server full!", (byte)0 };
                            _FireEvent(EventType.Connect, a);
                            _Reset();
                        }
                        else if (_RecSize == 2 && _Buffer[0] == (byte)114)
                        {
                            object[] a = { (int)4, "Server refused the connection!", _Buffer[1] };
                            _FireEvent(EventType.Connect, a);
                            _Reset();
                        }
                        else if (_RecSize == 5 && _Buffer[0] == (byte)112)
                        {
                            try
                            {
                                _ConnJob.Stop();
                                _BufferSize = (_Buffer[3] * 256) + _Buffer[4];
                                _Socket.ReceiveBufferSize = _BufferSize;
                                _Socket.SendBufferSize = _BufferSize;
                                _Id = (uint)((_Buffer[1] * 256) + _Buffer[2]);
                                _Buffer = new byte[_BufferSize];
                                _Status = true;
                                _ConnOp = false;
                                _OutgoingPackets = new Dictionary<int, ReliablePacket>(_MaxSendQueueSize + 1);
                                _PingTimeoutJob = new TimedJob(_HandlePingTimeout, _PingTimeout, null);
                                _ReliabilityJob = new TimedJob(_Reliability, _SendQueueInterval, null);
                                if (!_PingTimeoutJob.Start() || !_ReliabilityJob.Start())
                                {
                                    _Status = false;
                                    byte[] _Out = { 117 };
                                    _Send(_Out);
                                    object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                                    _FireEvent(EventType.Connect, a);
                                    _Reset();
                                    return;
                                }
                                object[] a1 = { (int)0, "Connected.", (byte)0 };
                                _FireEvent(EventType.Connect, a1);
                            }
                            catch
                            {
                                _Status = false;
                                byte[] _Out = { 117 };
                                _Send(_Out);
                                object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                                _FireEvent(EventType.Connect, a);
                                _Reset();
                            }
                        }
                    }
                    else
                    {
                        switch (_Buffer[0])
                        {
                            case 115://Ping request
                                if (_RecSize == 3)//Doesn't contains 'LastPing'
                                {
                                    byte[] _Out = { 116, _Buffer[1], _Buffer[2] };
                                    _Send(_Out);
                                    _PingTimeoutJob.Stop();
                                    if (!_PingTimeoutJob.Start())
                                    {
                                        object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                                        _FireEvent(EventType.Disconnect, a);
                                        _Reset();
                                    }
                                }
                                else if (_RecSize == 5)//Contains 'LastPing'
                                {
                                    _LastPing = (uint)(_Buffer[3] * 256) + _Buffer[4];
                                    byte[] _Out = { 116, _Buffer[1], _Buffer[2] };
                                    _Send(_Out);
                                    _PingTimeoutJob.Stop();
                                    if (!_PingTimeoutJob.Start())
                                    {
                                        object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                                        _FireEvent(EventType.Disconnect, a);
                                        _Reset();
                                    }
                                }
                                break;
                            case 117://Drop packet -Error
                                if (_RecSize == 1)
                                {
                                    object[] a = { (int)2, "An error occured! (server-side)", (byte)0 };
                                    _FireEvent(EventType.Disconnect, a);
                                    _Reset();
                                }
                                break;
                            case 118://Drop packet -Timeout
                                if (_RecSize == 1)
                                {
                                    object[] a = { (int)6, "Connection timed out! (server-side)", (byte)0 };
                                    _FireEvent(EventType.Disconnect, a);
                                    _Reset();
                                }
                                break;
                            case 119://Drop packet -Network locked
                                if (_RecSize == 1)
                                {
                                    object[] a = { (int)4, "Network locked! (server-side)", (byte)0 };
                                    _FireEvent(EventType.Disconnect, a);
                                    _Reset();
                                }
                                break;
                            case 120://Drop packet -High ping
                                if (_RecSize == 1)
                                {
                                    object[] a = { (int)8, "Too high ping! (server-side)", (byte)0 };
                                    _FireEvent(EventType.Disconnect, a);
                                    _Reset();
                                }
                                break;
                            case 121://Drop packet -Generic reason
                                if (_RecSize == 2)
                                {
                                    object[] a = { (int)7, "Server dropped the connection!", (byte)_Buffer[1] };
                                    _FireEvent(EventType.Disconnect, a);
                                    _Reset();
                                }
                                break;
                            case 122://Unreliable Data Send
                                if (_RecSize > 1)
                                {
                                    byte[] _Out = new byte[_RecSize - 1];
                                    for (int i = 0; i < _RecSize - 1; i++) { _Out[i] = _Buffer[i + 1]; }
                                    object[] a = { (byte[])_Out, (int)(_RecSize - 1), PacketType.Unreliable };
                                    _FireEvent(EventType.Receive, a);
                                }
                                break;
                            case 123://Reliable Data Request (Pkt)
                                if (_RecSize > 4)
                                {
                                    byte[] data = new byte[_RecSize];
                                    for (int i = 0; i < _RecSize; i++) { data[i] = _Buffer[i]; }
                                    ReliablePacket rPacket = new ReliablePacket(data);
                                    if (ReliablePacket.Check(rPacket))
                                    {
                                        if (!_RPacketList.Contains(rPacket.Id))
                                        {
                                            _RPacketList.Add(rPacket.Id);
                                            int fromPos = rPacket.Id - 10240;
                                            if (fromPos < 0) { fromPos += 65535; }
                                            if (_RPacketList.Contains(fromPos)) { _RPacketList.Remove(fromPos); }
                                            object[] a = { (byte[])rPacket.Data, (int)rPacket.Data.Length, PacketType.Reliable };
                                            _FireEvent(EventType.Receive, a);
                                        }

                                        byte[] _Out = { 124, data[1], data[2] };
                                        _Send(_Out);
                                    }
                                }
                                break;
                            case 124://Reliable Data Response (Ack)
                                if (_RecSize == 3)
                                {
                                    int packetId = (_Buffer[1] * 256) + _Buffer[2];
                                    lock (_OutgoingPackets) { if (_OutgoingPackets.ContainsKey(packetId)) { _OutgoingPackets.Remove(packetId); } }
                                }
                                break;
                        }
                    }
                }
                else if (_RecSize < 0)
                {
                    if (_ConnOp)
                    {
                        object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                        _FireEvent(EventType.Connect, a);
                    }
                    else
                    {
                        byte[] _Out = { 117 };
                        _Send(_Out);
                        object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                        _FireEvent(EventType.Connect, a);
                    }
                    break;
                }
            }
            _Reset();
        }

        private void _HandleConnection(object arg)
        {
            if (!_ConnOp) { return; }

            _ConnTries++;
            if (_ConnTries > _MaxConnRetryCount)
            {
                object[] a = { (int)2, "Connection failed!", (byte)0 };
                _FireEvent(EventType.Connect, a);
                _Reset();
                return;
            }
            _Send(_ConnReq);
            if (!_ConnJob.Start())
            {
                byte[] _Out = { 117 };
                _Send(_Out);
                object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                _FireEvent(EventType.Connect, a);
                _Reset();
            }
        }

        private void _HandlePingTimeout(object arg)
        {
            if (!_Status) { return; }
            byte[] _Out = { 118 };
            _Send(_Out);
            object[] a = { (int)5, "Connection timed out! (client-side)", (byte)0 };
            _FireEvent(EventType.Disconnect, a);
            _Reset();
        }

        private void _Reliability(object arg)
        {
            if (!_Status) { return; }
            lock (_OutgoingPackets) { foreach (KeyValuePair<int, ReliablePacket> o in _OutgoingPackets) { _Send(o.Value.RawData); } }
            if (!_ReliabilityJob.Start())
            {
                byte[] _Out = { 117 };
                _Send(_Out);
                object[] a = { (int)1, "An error occured! (client-side)", (byte)0 };
                _FireEvent(EventType.Connect, a);
                _Reset();
            }
        }

        #endregion
    }
}