using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ronin_Portier
{
    // Live port -> process lookup via the IP Helper API (iphlpapi.dll). Pure Win32, not COM,
    // so unlike the NetFwTypeLib calls elsewhere in this app it's safe to run off the UI thread.
    internal static class PortLookup
    {
        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;
        private const uint MIB_TCP_STATE_LISTEN = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint LocalAddr;
            public uint LocalPort;
            public uint OwningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

        // Maps every port with a live TCP listener or bound UDP socket to its owning process name.
        internal static Dictionary<int, string> GetPortProcessMap()
        {
            var map = new Dictionary<int, string>();
            var pidCache = new Dictionary<uint, string>();

            foreach (var (port, pid) in ReadTcpListeners())
                map[port] = ResolveProcessName(pid, pidCache);

            foreach (var (port, pid) in ReadUdpListeners())
                if (!map.ContainsKey(port))
                    map[port] = ResolveProcessName(pid, pidCache);

            return map;
        }

        private static string ResolveProcessName(uint pid, Dictionary<uint, string> cache)
        {
            if (cache.TryGetValue(pid, out var cached)) return cached;

            string name;
            try { name = Process.GetProcessById((int)pid).ProcessName; }
            catch { name = ""; }

            cache[pid] = name;
            return name;
        }

        // Local ports are stored big-endian inside the DWORD; swap the low two bytes to read them.
        private static int SwapPort(uint rawPort) => ((int)(rawPort & 0xFF) << 8) | (int)((rawPort >> 8) & 0xFF);

        private static List<(int port, uint pid)> ReadTcpListeners()
        {
            var results = new List<(int, uint)>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return results;

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint ret = GetExtendedTcpTable(buffer, ref size, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (ret != 0) return results;

                int rowCount = Marshal.ReadInt32(buffer);
                IntPtr rowPtr = IntPtr.Add(buffer, 4);
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    if (row.State == MIB_TCP_STATE_LISTEN)
                        results.Add((SwapPort(row.LocalPort), row.OwningPid));
                }
            }
            catch { /* best-effort — leave results as-is */ }
            finally { Marshal.FreeHGlobal(buffer); }

            return results;
        }

        private static List<(int port, uint pid)> ReadUdpListeners()
        {
            var results = new List<(int, uint)>();
            int size = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref size, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (size <= 0) return results;

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                uint ret = GetExtendedUdpTable(buffer, ref size, true, AF_INET, UDP_TABLE_OWNER_PID, 0);
                if (ret != 0) return results;

                int rowCount = Marshal.ReadInt32(buffer);
                IntPtr rowPtr = IntPtr.Add(buffer, 4);
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    results.Add((SwapPort(row.LocalPort), row.OwningPid));
                }
            }
            catch { /* best-effort — leave results as-is */ }
            finally { Marshal.FreeHGlobal(buffer); }

            return results;
        }
    }
}
