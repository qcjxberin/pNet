using pMd5;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using System.Collections.Generic;

internal delegate void SocketIOEvent(byte code, object[] arguments);
internal class SocketIO
{
    #region Employees

    private bool _Status = false;

    private SocketIOEvent _Handler = null;

    private Socket _Socket = null;
    private Thread _Thread = null;

    private int _Port = 0;
    private int _BufferSize = 0;

    #endregion

    #region Public Members

    public SocketIO(SocketIOEvent handler) { _Handler = handler; }

    public byte Start(int port, int bufferSize)
    {
        try
        {
            if (_Status) { return 1; }
            _Port = port;
            _BufferSize = bufferSize;
            try
            {
                _Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _Socket.ReceiveBufferSize = _BufferSize;
                _Socket.SendBufferSize = _BufferSize;
                _Socket.ReceiveTimeout = 0;
                _Socket.SendTimeout = 0;
            }
            catch
            {
                try { _Socket.Close(); }
                catch { }
                return 2;
            }
            try { _Thread = new Thread(_Listener); }
            catch
            {
                try { _Socket.Close(); }
                catch { }
                return 3;
            }
            try { _Socket.Bind(new IPEndPoint(IPAddress.Any, port)); }
            catch (Exception e)
            {
                try { _Socket.Close(); }
                catch { }
                throw e;//return 4;
            }
            try { _Thread.Start(); }
            catch
            {
                try { _Socket.Close(); }
                catch { }
                try { if (_Thread.IsAlive) { _Thread.Abort(); } }
                catch { }
                return 5;
            }
            _Status = true;
        }
        catch { return 6; }
        return 0;
    }

    public bool Stop()
    {
        if (_Status)
        {
            if (_Socket != null) { try { _Socket.Close(); } catch { } }
            try { if (_Thread != null) { if (_Thread.IsAlive) { _Thread.Abort(); } } } catch { }
            _Status = false;
            return true;
        }
        else
        {
            if (_Socket != null) { try { _Socket.Close(); } catch { } }
            try { if (_Thread != null) { if (_Thread.IsAlive) { _Thread.Abort(); } } } catch { }
            return false;
        }
    }

    public void Send(EndPoint target, byte[] data)
    {
        try { _Socket.SendTo(data, 0, data.Length, SocketFlags.None, target); }
        catch (ObjectDisposedException)
        {
            object[] args = new object[2];
            args[0] = (byte)5;
            args[1] = (string)"An unexpected socket error occured while trying to send data!";
            _Handler(1, args);
        }
        catch { }
    }

    #endregion

    #region Core

    private void _Listener()
    {
        byte[] _Buffer = new byte[_BufferSize];
        EndPoint _InEp = new IPEndPoint(IPAddress.Any, 9696);
        int _RecSize = 0;
        while (_Status)
        {
            try { _RecSize = _Socket.ReceiveFrom(_Buffer, 0, _BufferSize, SocketFlags.None, ref _InEp); }
            catch (ObjectDisposedException)
            {
                if (_Status)
                {
                    object[] args = new object[2];
                    args[0] = (byte)3;
                    args[1] = (string)"An unhandled socket error occured while trying to listen!";
                    _Handler(1, args);
                }
                break;
            }
            catch { continue; }
            if (!_Status) { break; }
            if (_RecSize > 0)
            {
                byte[] inData = new byte[_RecSize];
                for (int i = 0; i < _RecSize; i++) { inData[i] = _Buffer[i]; }
                object[] args = new object[2];
                args[0] = (EndPoint)_InEp;
                args[1] = (byte[])inData;
                _Handler(0, args);
            }
            else if (_RecSize < 0)
            {
                if (_Status)
                {
                    object[] args = new object[2];
                    args[0] = (byte)4;
                    args[1] = (string)"An unexpected socket error occured while listening!";
                    _Handler(1, args);
                }
                break;
            }
        }
        _Status = false;
        try { _Socket.Close(); }
        catch { }
    }

    #endregion
}

internal delegate void PeerEvent(Peer peer, byte code, object[] args);
internal class Peer
{
    #region Employees

    private bool _Status = false;

    private PeerEvent _Handler = null;

    private uint _Id = 0;
    private EndPoint _EndPoint = null;

    private Thread _Thread = null;

    private BlockingQueue _IncomingDataQueue = null;

    private ArrayList _RPacketList = null;

    #region Ping

    private class Ping
    {
        private uint _Id;
        private int _CreationTime;

        public Ping(uint id)
        {
            _Id = id;
            _CreationTime = Environment.TickCount;
        }

        public uint Id { get { return _Id; } }

        public int CreationTime { get { return _CreationTime; } }

        public uint Calculate() { return (uint)(Environment.TickCount - _CreationTime); }
    }

    private TimedJob _PingJob = null;
    private TimedJob _PingTimeoutJob = null;
    private Dictionary<uint, Ping> _Pings = new Dictionary<uint, Ping>();
    private uint _LastPingId = 0;
    private uint _LastCalculatedPing = 0;

    #endregion

    #region Reliablity

    private TimedJob _ReliabilityJob = null;
    private Dictionary<int, ReliablePacket> _OutgoingPackets = null;
    private int _LastPacketId = 65500;

    #endregion

    #region Protocol Variables

    private int _BufferSize = 0;
    private int _MaxPing = 0;
    private int _PingInterval = 0;
    private int _PingTimeout = 0;
    private int _MaxSendQueueSize = 0;
    private int _SendQueueInterval = 0;
    private int _PeerDataCapacity = 0;

    #endregion

    #endregion

    #region Public Members

    public uint Id { get { return _Id; } }

    public EndPoint EndPoint { get { return _EndPoint; } }

    public Peer(PeerEvent handler) { _Handler = handler; }

    public bool Setup(uint id, EndPoint endPoint, int bufferSize, int maxPing, int pingInterval, int pingTimeout, int maxSendQueueSize, int sendQueueInterval, int peerDataCapacity)
    {
        try
        {
            _Status = true;
            _Id = id;
            _EndPoint = endPoint;
            _BufferSize = bufferSize;
            _MaxPing = maxPing;
            _PingInterval = pingInterval;
            _PingTimeout = pingTimeout;
            _MaxSendQueueSize = maxSendQueueSize;
            _SendQueueInterval = sendQueueInterval;
            _PeerDataCapacity = peerDataCapacity;
            _IncomingDataQueue = new BlockingQueue(_PeerDataCapacity);
            _OutgoingPackets = new Dictionary<int, ReliablePacket>(_MaxSendQueueSize + 1);
            _RPacketList = new ArrayList();
            try
            {
                _PingJob = new TimedJob(_HandlePing, _PingInterval, null);
                _PingTimeoutJob = new TimedJob(_HandlePingTimeout, _PingTimeout, null);
                _ReliabilityJob = new TimedJob(_Reliability, _SendQueueInterval, null);
            }
            catch { Close(); return false; }
            try
            {
                _Thread = new Thread(_Process);
                _Thread.Start();
            }
            catch { Close(); return false; }
            if (!_PingJob.Start() || !_PingTimeoutJob.Start() || !_ReliabilityJob.Start()) { Close(); return false; }
            try
            {
                byte _Id1 = (byte)(_Id / 256);
                byte _Id2 = (byte)(_Id - (256 * _Id1));
                byte _BS1 = (byte)(_BufferSize / 256);
                byte _BS2 = (byte)(_BufferSize - (256 * _BS1));
                byte[] _Out = { 112, _Id1, _Id2, _BS1, _BS2 };
                object[] args = new object[1];
                args[0] = _Out;
                _Handler(this, 2, args);
            }
            catch { Close(); return false; }
            return true;
        }
        catch { return false; }
    }

    public void EnqueueIncomingData(byte[] data)
    {
        if (!_IncomingDataQueue.Enqueue(data))
        {
            object[] args1 = new object[1];
            byte[] _Out = { 117 };//Drop packet -Error
            args1[0] = _Out;
            _Handler(this, 2, args1);
            if (_Status)
            {
                object[] args2 = new object[1];
                args2[0] = (int)1;
                _Handler(this, 0, args2);//Drop
            }
            Close();
        }
    }

    public void EnqueueOutgoingData(byte[] data)
    {
        ReliablePacket rPacket = new ReliablePacket(_LastPacketId, data);
        object[] args = new object[1];
        args[0] = rPacket.RawData;
        _Handler(this, 2, args);
        lock (_OutgoingPackets)
        {
            if (_OutgoingPackets.Count + 1 > _MaxSendQueueSize)
            {
                if (_Status)
                {
                    object[] _args1 = new object[1];
                    byte[] _Out = { 119 };//Drop packet -network locked
                    _args1[0] = _Out;
                    _Handler(this, 2, _args1);
                    object[] args2 = new object[1];
                    args2[0] = (int)2;
                    _Handler(this, 0, args2);//Drop
                }
                Close();
            }
            if (_OutgoingPackets.ContainsKey(_LastPacketId)) { _OutgoingPackets.Remove(_LastPacketId); }
            _OutgoingPackets.Add(_LastPacketId, rPacket);
        }
        if (_LastPacketId > 65534) { _LastPacketId = 0; } else { _LastPacketId++; }
    }

    public void Close()
    {
        _Status = false;
        _IncomingDataQueue.Close();
        try { _Thread.Abort(); } catch { }
        _PingJob.Stop();
        _PingTimeoutJob.Stop();
        _ReliabilityJob.Stop();
    }

    #endregion

    #region Core

    private void _Process()
    {
        while (_Status)
        {
            object packet = _IncomingDataQueue.Dequeue();
            if (!_Status) { return; }
            if (packet != null)
            {
                try
                {
                    byte[] data = (byte[])packet;
                    int dataLength = data.Length;
                    if (dataLength > 0)
                    {
                        switch (data[0])//Header
                        {
                            case 111://Connection request
                                if (dataLength > 4 && data[0] == (byte)255 && dataLength < 36)
                                {
                                    byte _Id1 = (byte)(_Id / 256);
                                    byte _Id2 = (byte)(_Id - (256 * _Id1));
                                    byte _BS1 = (byte)(_BufferSize / 256);
                                    byte _BS2 = (byte)(_BufferSize - (256 * _BS1));
                                    byte[] _Out = { 112, _Id1, _Id2, _BS1, _BS2 };
                                    object[] args = new object[1];
                                    args[0] = _Out;
                                    _Handler(this, 2, args);
                                }
                                break;
                            case 116://Ping response
                                if (dataLength == 3)
                                {
                                    uint id = (uint)(data[1] * 256) + data[2];
                                    lock (_Pings)
                                    {
                                        if (_Pings.ContainsKey(id))
                                        {
                                            _LastCalculatedPing = _Pings[id].Calculate();
                                            object[] args = new object[1];
                                            args[0] = (uint)_LastCalculatedPing;
                                            _Handler(this, 3, args);
                                            _Pings.Remove(id);
                                            if (_LastCalculatedPing > _MaxPing)
                                            {
                                                object[] args1 = new object[1];
                                                byte[] _Out = { 120 };//Drop packet -High ping
                                                args1[0] = _Out;
                                                _Handler(this, 2, args1);
                                                object[] args2 = new object[1];
                                                args2[0] = (int)4;
                                                _Handler(this, 0, args2);
                                                Close();
                                                return;
                                            }
                                        }
                                    }
                                    _PingTimeoutJob.Stop();
                                    if (!_PingTimeoutJob.Start()) { Close(); if (_Status) { object[] args = new object[1]; args[0] = (int)1; _Handler(this, 0, args); } }
                                }
                                break;
                            case 117://Drop packet -Error
                                if (dataLength == 1)
                                {
                                    object[] args = new object[1];
                                    args[0] = (int)5;
                                    _Handler(this, 0, args);
                                    Close();
                                }
                                break;
                            case 118://Drop packet -Timeout
                                if (dataLength == 1)
                                {
                                    object[] args = new object[1];
                                    args[0] = (int)6;
                                    _Handler(this, 0, args);
                                    Close();
                                }
                                break;
                            case 119://Drop packet -Network lock
                                if (dataLength == 1)
                                {
                                    object[] args = new object[1];
                                    args[0] = (int)7;
                                    _Handler(this, 0, args);
                                    Close();
                                }
                                break;
                            case 121://Drop packet -Generic reason
                                if (dataLength == 2)
                                {
                                    object[] args = new object[2];
                                    args[0] = (int)8;
                                    args[1] = (byte)data[1];
                                    _Handler(this, 0, args);
                                    Close();
                                }
                                break;
                            case 122://Unreliable Data Request
                                if (dataLength > 1)
                                {
                                    byte[] inData = new byte[dataLength - 1];
                                    for (int i = 0; i < dataLength - 1; i++) { inData[i] = data[i + 1]; }
                                    object[] args = new object[2];
                                    args[0] = inData;
                                    args[1] = (int)1;
                                    _Handler(this, 1, args);
                                }
                                break;
                            case 123://Reliable Data Request (Pkt)
                                if (dataLength > 4)
                                {
                                    ReliablePacket rPacket = new ReliablePacket(data);
                                    if (ReliablePacket.Check(rPacket))
                                    {
                                        if (!_RPacketList.Contains(rPacket.Id))
                                        {
                                            _RPacketList.Add(rPacket.Id);
                                            int fromPos = rPacket.Id - 10240;
                                            if (fromPos < 0) { fromPos += 65535; }
                                            if (_RPacketList.Contains(fromPos)) { _RPacketList.Remove(fromPos); }
                                            object[] args1 = new object[2];
                                            args1[0] = rPacket.Data;
                                            args1[1] = (int)0;
                                            _Handler(this, 1, args1);
                                        }
                                        byte[] _Out = { 124, data[1], data[2] };
                                        object[] args2 = new object[1];
                                        args2[0] = _Out;
                                        _Handler(this, 2, args2);
                                    }
                                }
                                break;
                            case 124://Reliable Data Response (Ack)
                                if (dataLength == 3)
                                {
                                    int packetId = (data[1] * 256) + data[2];
                                    lock (_OutgoingPackets) { if (_OutgoingPackets.ContainsKey(packetId)) { _OutgoingPackets.Remove(packetId); } }
                                }
                                break;
                        }
                    }
                }
                catch
                {
                    if (_Status)
                    {
                        object[] args1 = new object[1];
                        byte[] _Out = { 117 };//Drop packet -Error
                        args1[0] = _Out;
                        _Handler(this, 2, args1);
                        object[] args = new object[1];
                        args[0] = (int)1;
                        _Handler(this, 0, args);
                    }
                    Close();
                }
            }
        }
    }

    private void _HandlePing(object arg)
    {
        if (!_Status) { return; }
        try
        {
            if (!_PingJob.Start()) { if (_Status) { object[] args = new object[1]; args[0] = (int)1; _Handler(this, 0, args); } Close(); return; }
            lock (_Pings)
            {
                if (_Pings.ContainsKey(_LastPingId)) { _Pings.Remove(_LastPingId); }
                Ping ping = new Ping(_LastPingId);
                _Pings.Add(_LastPingId, ping);
                uint i1, i2;
                i1 = (uint)_LastPingId / 256;
                i2 = (uint)_LastPingId - (i1 * 256);
                object o = null;
                if (_LastCalculatedPing < 65535)
                {
                    uint p1, p2;
                    p1 = _LastCalculatedPing / 256;
                    p2 = _LastCalculatedPing - (p1 * 256);
                    byte[] _Out = { 115, (byte)i1, (byte)i2, (byte)p1, (byte)p2 };
                    o = _Out;
                }
                object[] args = new object[1];
                args[0] = o;
                _Handler(this, 2, args);
                if (_LastPingId < 65535) { _LastPingId++; } else { _LastPingId = 0; }
            }
        }
        catch { if (_Status) { object[] args = new object[1]; args[0] = (int)1; _Handler(this, 0, args); } Close(); }
    }

    private void _HandlePingTimeout(object arg)
    {
        if (!_Status) { return; }
        object[] args = new object[1];
        args[0] = (int)3;
        _Handler(this, 0, args);//Drop
        Close();
    }

    private void _Reliability(object arg)
    {
        if (!_Status) { return; }
        lock (_OutgoingPackets)
        {
            foreach (KeyValuePair<int, ReliablePacket> o in _OutgoingPackets)
            {
                object[] args = new object[1];
                args[0] = o.Value.RawData;
                _Handler(this, 2, args);
            }
        }
        if (!_ReliabilityJob.Start())
        {
            if (_Status)
            {
                object[] args1 = new object[1];
                byte[] _Out = { 117 };//Drop packet -Error
                args1[0] = _Out;
                _Handler(this, 2, args1);
                object[] args2 = new object[1];
                args2[0] = (int)1;
                _Handler(this, 0, args2);//Drop
            }
            Close();
        }
    }

    #endregion
}

internal class BlockingQueue
{
    #region Employees

    private bool _Status = false;
    private Queue<object> _Queue = null;
    private int _Size = 0;

    #endregion

    #region Public Members

    public BlockingQueue(int size)
    {
        _Queue = new Queue<object>();
        _Size = size;
        _Status = true;
    }

    public bool Enqueue(object item)
    {
        try
        {
            if (!_Status) { return false; }
            lock (_Queue)
            {
                if (!_Status) { return false; }
                if (_Queue.Count >= _Size) { return true; }
                _Queue.Enqueue(item);
                Monitor.PulseAll(_Queue);
                return true;
            }
        }
        catch { return false; }
    }

    public object Dequeue()
    {
        try
        {
            if (!_Status) { return null; }
            lock (_Queue)
            {
                if (!_Status) { return null; }
                while (_Queue.Count == 0)
                {
                    Monitor.Wait(_Queue);
                    if (!_Status) { return null; }
                }
                return _Queue.Dequeue();
            }
        }
        catch { return null; }
    }

    public void Close()
    {
        _Status = false;
        try { Monitor.PulseAll(_Queue); }
        catch { }
    }

    #endregion
}

internal delegate void TimedJobTarget(object arg);
internal class TimedJob
{
    #region Employees

    private bool _Status = false;

    private TimedJobTarget _Target = null;
    private Thread _Thread = null;
    private int _Delay = 0;
    private object _Arg = null;

    #endregion

    #region Public Members

    public TimedJob(TimedJobTarget target, int delay, object arg)
    {
        if (delay < 0) { throw new ArgumentOutOfRangeException("delay", "Delay cannot be lower than zero!"); }
        if (target == null) { throw new ArgumentNullException("target", "TimedJob requires a valid TimedJobTarget instance!"); }
        _Target = target;
        _Delay = delay;
        _Arg = arg;
    }

    public bool Start() { _Status = true; try { _Thread = new Thread(_Process); _Thread.Start(); return true; } catch { return false; } }

    public void Stop() { _Status = false; try { _Thread.Abort(); } catch { } }

    #endregion

    #region Core

    private void _Process() { if (!_Status) { return; } Thread.Sleep(_Delay); if (!_Status) { return; } _Target(_Arg); }

    #endregion
}

internal class ReliablePacket
{
    #region Employees

    private int _PacketId;
    private byte[] _CheckSum;
    private byte[] _Data;

    private byte[] _RawData;

    #endregion

    #region Public Members

    public ReliablePacket(byte[] rawData)
    {
        if (rawData.Length < 36) { return; }//md5(32) + length(2) + reliableFlag(1) + minSize(1) = 36
        _RawData = rawData;
        int rawDataLength = rawData.Length;
        _PacketId = (_RawData[1] * 256) + _RawData[2];
        _CheckSum = new byte[32];
        Array.Copy(_RawData, 3, _CheckSum, 0, 32);
        _Data = new byte[rawDataLength - 35];
        for (int i = 0; i < rawDataLength - 35; i++) { _Data[i] = _RawData[i + 35]; }
    }

    public ReliablePacket(int packetId, byte[] data)
    {
        _PacketId = packetId;
        _Data = data;
        int dataLength = data.Length;
        _RawData = new byte[dataLength + 35];
        _RawData[0] = (byte)123;
        int i1 = _PacketId / 256;
        int i2 = _PacketId - (i1 * 256);
        _RawData[1] = (byte)i1;
        _RawData[2] = (byte)i2;
        _CheckSum = Md5Calculator.CalculateMd5Bytes(data);
        Array.Copy(_CheckSum, 0, _RawData, 3, 32);
        for (int i = 0; i < dataLength; i++) { _RawData[i + 35] = data[i]; }
    }

    public int Id { get { return _PacketId; } }

    public byte[] CheckSum { get { return _CheckSum; } }

    public byte[] Data { get { return _Data; } }

    public byte[] RawData { get { return _RawData; } }

    public static bool Check(ReliablePacket packet) { return (Md5Calculator.CalculateMd5String(packet.Data) == Encoding.UTF8.GetString(packet.CheckSum)); }

    #endregion
}