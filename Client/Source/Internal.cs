using pMd5;
using System;
using System.Text;
using System.Threading;
using System.Collections.Generic;

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

    public bool Start() { try { _Thread = new Thread(_Process); _Status = true; _Thread.Start(); return true; } catch (Exception e) { Console.WriteLine(e.ToString()); return false; } }

    public void Stop() { if (_Thread.IsAlive) { try { _Status = false; _Thread.Abort(); } catch (ThreadAbortException) { } catch { } } }

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