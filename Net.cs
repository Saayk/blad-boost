// Red y ping de GameBoost (BLAD Studios)
//
// Herramientas de medida para el módulo "Red y ping": ping ICMP, latencia TCP a centros de datos por región
// y comparativa de servidores DNS. Todo se mide desde este PC; no hay servidores intermedios.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace GameBoost
{
    public class NetRegion { public string Name, Host; }
    public class NetDns { public string Name, Primary, Secondary; }

    static class NetTools
    {
        // Endpoints públicos de AWS por región. Se mide el tiempo de conexión TCP (443), que equivale a un viaje de ida y vuelta.
        public static readonly NetRegion[] Regions =
        {
            new NetRegion { Name = "EE. UU. Este (Virginia)", Host = "dynamodb.us-east-1.amazonaws.com" },
            new NetRegion { Name = "EE. UU. Este (Ohio)", Host = "dynamodb.us-east-2.amazonaws.com" },
            new NetRegion { Name = "EE. UU. Oeste (California)", Host = "dynamodb.us-west-1.amazonaws.com" },
            new NetRegion { Name = "EE. UU. Oeste (Oregón)", Host = "dynamodb.us-west-2.amazonaws.com" },
            new NetRegion { Name = "México (Querétaro)", Host = "dynamodb.mx-central-1.amazonaws.com" },
            new NetRegion { Name = "Brasil (São Paulo)", Host = "dynamodb.sa-east-1.amazonaws.com" },
            new NetRegion { Name = "Europa (Irlanda)", Host = "dynamodb.eu-west-1.amazonaws.com" },
            new NetRegion { Name = "Europa (Fráncfort)", Host = "dynamodb.eu-central-1.amazonaws.com" },
            new NetRegion { Name = "Europa (España)", Host = "dynamodb.eu-south-2.amazonaws.com" },
            new NetRegion { Name = "Asia (Tokio)", Host = "dynamodb.ap-northeast-1.amazonaws.com" }
        };

        public static readonly NetDns[] DnsServers =
        {
            new NetDns { Name = "Cloudflare", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
            new NetDns { Name = "Google", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
            new NetDns { Name = "Quad9", Primary = "9.9.9.9", Secondary = "149.112.112.112" },
            new NetDns { Name = "OpenDNS", Primary = "208.67.222.222", Secondary = "208.67.220.220" },
            new NetDns { Name = "AdGuard", Primary = "94.140.14.14", Secondary = "94.140.15.15" }
        };

        // Ping ICMP en ms; -1 si se pierde.
        public static long Ping(string host, int timeout = 1000)
        {
            try
            {
                using (var p = new Ping())
                {
                    var r = p.Send(host, timeout);
                    return r.Status == IPStatus.Success ? r.RoundtripTime : -1;
                }
            }
            catch { return -1; }
        }

        // Latencia TCP (conexión al puerto 443): el mejor de varios intentos, en ms; -1 si no responde.
        public static double TcpLatency(string host, int tries = 4, int timeout = 1500)
        {
            IPAddress ip;
            try { ip = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork); }
            catch { return -1; }
            if (ip == null) return -1;
            double best = -1;
            for (int i = 0; i < tries; i++)
            {
                try
                {
                    using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        var sw = Stopwatch.StartNew();
                        var ar = s.BeginConnect(ip, 443, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(timeout)) continue;
                        s.EndConnect(ar);
                        double ms = sw.Elapsed.TotalMilliseconds;
                        if (best < 0 || ms < best) best = ms;
                    }
                }
                catch { }
            }
            return best;
        }

        // Tiempo de respuesta de un servidor DNS (mediana de varias consultas UDP), en ms; -1 si no responde.
        public static double DnsLatency(string server, int tries = 5)
        {
            var times = new List<double>();
            var rnd = new Random();
            for (int i = 0; i < tries; i++)
            {
                try
                {
                    using (var udp = new UdpClient())
                    {
                        udp.Client.ReceiveTimeout = 1500;
                        var q = DnsQuery((ushort)rnd.Next(1, 65535), i % 2 == 0 ? "www.google.com" : "www.cloudflare.com");
                        var ep = new IPEndPoint(IPAddress.Parse(server), 53);
                        var sw = Stopwatch.StartNew();
                        udp.Send(q, q.Length, ep);
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var resp = udp.Receive(ref from);
                        if (resp.Length >= 2 && resp[0] == q[0] && resp[1] == q[1]) times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch { }
            }
            if (times.Count == 0) return -1;
            times.Sort();
            return times[times.Count / 2];
        }

        static byte[] DnsQuery(ushort id, string name)
        {
            var b = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
            foreach (var label in name.Split('.'))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                b.Add((byte)bytes.Length);
                b.AddRange(bytes);
            }
            b.AddRange(new byte[] { 0, 0, 1, 0, 1 });
            return b.ToArray();
        }

        // Primer DNS IPv4 del adaptador activo (para compararlo con los públicos).
        public static string CurrentDns()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    var d = props.DnsAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (d != null) return d.ToString();
                }
            }
            catch { }
            return null;
        }
    }
}
