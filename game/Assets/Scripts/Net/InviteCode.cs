using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace RadiantPool.Game
{
    /// <summary>v1 invite codes encode IPv4 + port as human-typable base32 (no vowels →
    /// no accidental words, no 0/1 confusion). Example: a LAN host at 192.168.1.20:7770
    /// becomes a code like "W2PT-9XKC". Unity Relay join codes replace these when a UGS
    /// project is linked (see docs/hosting.md); the UI is agnostic to which is in use.</summary>
    public static class InviteCode
    {
        // 32 chars (5 bits each; 10 chars = 50 bits ≥ 48-bit payload), no 0/1/I/O confusion.
        private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static string Encode(IPAddress address, ushort port)
        {
            byte[] ip = address.MapToIPv4().GetAddressBytes();
            ulong value = ((ulong)ip[0] << 40) | ((ulong)ip[1] << 32) | ((ulong)ip[2] << 24)
                        | ((ulong)ip[3] << 16) | port;
            char[] chars = new char[10];
            for (int i = 9; i >= 0; i--)
            {
                chars[i] = Alphabet[(int)(value % (ulong)Alphabet.Length)];
                value /= (ulong)Alphabet.Length;
            }
            string raw = new string(chars);
            return raw.Substring(0, 5) + "-" + raw.Substring(5);
        }

        public static bool TryDecode(string code, out string address, out ushort port)
        {
            address = "";
            port = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;
            string raw = code.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (raw.Length != 10) return false;

            ulong value = 0;
            foreach (char c in raw)
            {
                int idx = Alphabet.IndexOf(c);
                if (idx < 0) return false;
                value = value * (ulong)Alphabet.Length + (ulong)idx;
            }

            port = (ushort)(value & 0xFFFF);
            value >>= 16;
            byte b3 = (byte)(value & 0xFF); value >>= 8;
            byte b2 = (byte)(value & 0xFF); value >>= 8;
            byte b1 = (byte)(value & 0xFF); value >>= 8;
            byte b0 = (byte)(value & 0xFF);
            address = new IPAddress(new[] { b0, b1, b2, b3 }).ToString();
            return port != 0;
        }

        /// <summary>Best-guess LAN IPv4 of this machine for hosting.</summary>
        public static IPAddress LocalAddress()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers expose no sockets and no LAN identity; callers never show the
            // invite code on web, this only keeps the API total.
            return IPAddress.Loopback;
#else
            try
            {
                // Doesn't send traffic; just forces the OS to pick the outbound interface.
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                socket.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)socket.LocalEndPoint!).Address;
            }
            catch
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                return host.AddressList.FirstOrDefault(
                    a => a.AddressFamily == AddressFamily.InterNetwork) ?? IPAddress.Loopback;
            }
#endif
        }
    }
}
