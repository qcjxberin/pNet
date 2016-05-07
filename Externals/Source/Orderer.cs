using System;
using System.Collections.Generic;
using MonoLightTech.Concurrent;

namespace pNet.Externals
{
    public delegate void HandleOrderedStream(byte[] data);
    public sealed class Orderer
    {
        HandleOrderedStream m_Handler = null;

        CcuDictionary<ushort, byte[]> m_Packets = null;
        CcuDictionary<uint, CcuDictionary<ushort, byte[]>> m_ClientPackets = null;

        CcuDictionary<uint, ushort> m_LastPacketId = null;
        CcuDictionary<uint, ushort> m_WaitingFor = null;

        public Orderer(HandleOrderedStream handler)
        {
            m_Handler = handler;
            m_Packets = new CcuDictionary<ushort, byte[]>();
            m_LastPacketId = new CcuDictionary<uint,ushort>();
            m_WaitingFor = new CcuDictionary<uint, ushort>();
        }

        public void AddClient(uint clientId) {
            m_WaitingFor.Add(clientId, 0);
        }

        public void Handle(uint clientID,ushort id, byte[] data)
        {
            if (m_Packets.ContainsKey(id)) { throw new Exception("A packet with the same key already exists in the stream!"); }
            m_Packets.Add(id, data);
            if (id == m_WaitingFor[clientID]) { Check(clientID); }
        }

        void Check(uint clientID)
        {
            ushort current = m_WaitingFor[clientID];
            while(true)
            {
                if (!m_Packets.ContainsKey(current))
                {
                    m_WaitingFor.Set(clientID,current);
                    break;
                }

                //Mantık hatası yok merak etme :)tmm :)

                byte[] packet = m_Packets[current];
                m_Packets.Remove(current);
                m_Handler(packet);
                current++;
            }
        }
    }
}