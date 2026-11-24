
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudyFocus
{
    internal sealed class WebsiteProxy : IDisposable
    {
        private readonly TcpListener listener;
        private readonly object gate = new object();
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private string[] restrictedTo;
        private bool disposed;

        public int Port { get; private set; }

        public WebsiteProxy(int preferredPort = 0)
        {
            listener = new TcpListener(IPAddress.Loopback, preferredPort);
            try { listener.Start(96); }
            catch { listener.Stop(); throw; }
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run((Func<Task>)Accept);
        }

        public void SetRestriction(string[] websites)
        {
            lock (gate)
            {
                if (disposed || object.ReferenceEquals(restrictedTo, websites)) return;
                restrictedTo = websites;
                foreach (TcpClient client in clients) client.Close();
            }
        }

        private async Task Accept()
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { return; }
                lock (gate)
                {
                    if (disposed || clients.Count >= 256) { client.Close(); continue; }
                    clients.Add(client);
                }
                Observe(Handle(client));
            }
        }

        private static async void Observe(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        private async Task Handle(TcpClient client)
        {
            using (client)
            using (TcpClient origin = new TcpClient())
            {
                try
                {
                    client.NoDelay = true;
                    NetworkStream input = client.GetStream();
                    using (CancellationTokenSource timeout = new CancellationTokenSource())
                    using (Timer timer = new Timer(delegate { client.Close(); }, null, 12000, Timeout.Infinite))
                    {
                        Request request = null;
                        bool malformed = false;
                        try { request = await Request.Read(input).ConfigureAwait(false); }
                        catch (FormatException) { malformed = true; }
                        timer.Change(Timeout.Infinite, Timeout.Infinite);
                        if (malformed)
                        {
                            await Reply(input, "400 Bad Request", "Unsupported proxy request.").ConfigureAwait(false);
                            return;
                        }
                        if (request == null) return;
                        bool allowed;
                        lock (gate)
                        {
                            allowed = !disposed && (restrictedTo == null || restrictedTo.Any(x => Website.MatchesSite(request.Address.IdnHost, x)));
                        }
                        if (request.Address.IsLoopback && request.Address.Port == Port &&
                            request.Address.AbsolutePath.StartsWith("/start", StringComparison.Ordinal))
                        {
                            await ReplyHtml(input, StartPage()).ConfigureAwait(false);
                            return;
                        }
                        if (!allowed || (request.Address.IsLoopback && request.Address.Port == Port))
                        {
                            await ReplyHtml(input, BlockedPage(request.Address.Host)).ConfigureAwait(false);
                            return;
                        }
                        Task connect = origin.ConnectAsync(request.Address.IdnHost.Trim('[', ']'), request.Address.Port);
                        if (await Task.WhenAny(connect, Task.Delay(12000, timeout.Token)).ConfigureAwait(false) != connect) return;
                        timeout.Cancel();
                        bool connectedToOrigin = true;
                        try { await connect.ConfigureAwait(false); }
                        catch (SocketException) { connectedToOrigin = false; }
                        if (!connectedToOrigin)
                        {
                            await Reply(input, "502 Bad Gateway", "Study Focus: could not connect to the website.").ConfigureAwait(false);
                            return;
                        }
                        origin.NoDelay = true;
                        NetworkStream output = origin.GetStream();
                        if (request.IsTunnel)
                        {
                            byte[] connected = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                            await input.WriteAsync(connected, 0, connected.Length).ConfigureAwait(false);
                            await Task.WhenAny(input.CopyToAsync(output), output.CopyToAsync(input)).ConfigureAwait(false);
                            return;
                        }

                        byte[] headers = Encoding.GetEncoding(28591).GetBytes(request.OriginHeaders());
                        await output.WriteAsync(headers, 0, headers.Length).ConfigureAwait(false);
                        Task reply = output.CopyToAsync(input);
                        await Task.WhenAny(reply, CopyBody(input, output, request.ContentLength)).ConfigureAwait(false);
                        await reply.ConfigureAwait(false);
                    }
                }
                catch (IOException) { }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
                finally { lock (gate) clients.Remove(client); }
            }
        }

        private static async Task CopyBody(Stream from, Stream to, long remaining)
        {
            byte[] buffer = new byte[16384];
            while (remaining != 0)
            {
                int count = await from.ReadAsync(buffer, 0, (int)Math.Min(remaining, buffer.Length)).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException();
                await to.WriteAsync(buffer, 0, count).ConfigureAwait(false);
                remaining -= count;
            }
        }

        private string BlockedPage(string host)
        {
            string[] sites;
            lock (gate) sites = restrictedTo == null ? new string[0] : restrictedTo.ToArray();
            var builder = new StringBuilder();
            builder.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>定心 · 该网站不在白名单</title>");
            builder.Append("<style>body{margin:0;background:#101827;color:#f2f6ff;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif}");
            builder.Append(".wrap{max-width:760px;margin:0 auto;padding:64px 32px}h1{font-size:26px;margin:0 0 8px}");
            builder.Append("p{color:#b4c1d6;margin:0 0 28px;line-height:1.7}a.card{display:block;padding:15px 20px;margin:0 0 10px;");
            builder.Append("background:#192438;border:1px solid #334461;border-radius:12px;color:#81bbff;text-decoration:none;font-size:16px}");
            builder.Append("a.card:hover{background:#22304a;border-color:#81bbff}.tag{color:#b4c1d6;font-size:12px;float:right}</style></head><body><div class=\"wrap\">");
            builder.Append("<h1>该网站不在本轮白名单内</h1><p>被拦截的地址：").Append(System.Net.WebUtility.HtmlEncode(host));
            builder.Append("<br>当前只允许打开下面这些网站：</p>");
            if (sites.Length == 0) builder.Append("<p>本轮没有配置网站白名单。</p>");
            foreach (string site in sites)
            {
                string target = site.StartsWith("*.", StringComparison.Ordinal) ? site.Substring(2) : site;
                builder.Append("<a class=\"card\" href=\"https://").Append(target).Append("\">").Append(target);
                builder.Append("<span class=\"tag\">").Append(site.StartsWith("*.", StringComparison.Ordinal) ? "包含子域名" : "精确域名").Append("</span></a>");
            }
            builder.Append("</div></body></html>");
            return builder.ToString();
        }
        private string StartPage()
        {
            string[] sites;
            lock (gate) sites = restrictedTo == null ? new string[0] : restrictedTo.ToArray();
            var builder = new StringBuilder();
            builder.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>定心 · 允许访问的网站</title>");
            builder.Append("<style>body{margin:0;background:#101827;color:#f2f6ff;font-family:'Microsoft YaHei UI','Segoe UI',sans-serif}" );
            builder.Append(".wrap{max-width:760px;margin:0 auto;padding:72px 32px}h1{font-size:30px;margin:0 0 10px}");
            builder.Append("p{color:#b4c1d6;margin:0 0 36px}a{display:block;padding:16px 20px;margin:0 0 12px;background:#192438;");
            builder.Append("border:1px solid #334461;border-radius:12px;color:#81bbff;text-decoration:none;font-size:17px}");
            builder.Append("a:hover{background:#22304a;border-color:#81bbff}span{color:#b4c1d6;font-size:13px;float:right}</style></head><body><div class=\"wrap\">");
            builder.Append("<h1>专注中 · 仅允许以下网站</h1><p>点击直接打开，链接均为 https:// 开头。</p>");
            if (sites.Length == 0) builder.Append("<p>当前没有配置网站白名单。</p>");
            foreach (string site in sites)
            {
                string host = site.StartsWith("*.", StringComparison.Ordinal) ? site.Substring(2) : site;
                builder.Append("<a href=\"https://").Append(host).Append("\">").Append(host);
                builder.Append("<span>").Append(site.StartsWith("*.", StringComparison.Ordinal) ? "包含子域名" : "精确域名").Append("</span></a>");
            }
            builder.Append("</div></body></html>");
            return builder.ToString();
        }

        private static async Task ReplyHtml(Stream stream, string html)
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            string header = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
            byte[] head = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }
        private static Task Reply(Stream stream, string status, string body)
        {
            byte[] reply = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nConnection: close\r\nContent-Type: text/plain\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
            return stream.WriteAsync(reply, 0, reply.Length);
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                listener.Stop();
                foreach (TcpClient client in clients) client.Close();
            }
        }

        private sealed class Request
        {
            private static readonly string[] HopHeaders = {
                "Host", "Connection", "Proxy-Connection", "Proxy-Authorization", "Keep-Alive",
                "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Expect", "Content-Length"
            };
            public Uri Address;
            public string Method;
            public long ContentLength;
            public List<KeyValuePair<string, string>> Headers = new List<KeyValuePair<string, string>>();
            public bool IsTunnel { get { return Method == "CONNECT"; } }

            public static async Task<Request> Read(Stream stream)
            {
                List<byte> buffer = new List<byte>();
                byte[] one = new byte[1];
                while (buffer.Count < 32768)
                {
                    if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) return null;
                    buffer.Add(one[0]);
                    int n = buffer.Count;
                    if (n >= 4 && buffer[n - 4] == 13 && buffer[n - 3] == 10 && buffer[n - 2] == 13 && buffer[n - 1] == 10)
                        return Parse(Encoding.GetEncoding(28591).GetString(buffer.ToArray()));
                }
                throw new FormatException();
            }

            private static Request Parse(string text)
            {
                string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] first = lines[0].Split(' ');
                if (first.Length != 3 || first[0].Length == 0 ||
                    first[0].Any(c => c < 'A' || c > 'Z') ||
                    (first[2] != "HTTP/1.0" && first[2] != "HTTP/1.1"))
                    throw new FormatException();
                Request request = new Request { Method = first[0] };
                string target = request.IsTunnel ? "https://" + first[1] : first[1];
                if ((request.IsTunnel && first[1].IndexOfAny(new[] { '/', '?', '#', '@', '\\' }) >= 0) ||
                    !Uri.TryCreate(target, UriKind.Absolute, out request.Address) ||
                    request.Address.UserInfo.Length != 0 ||
                    request.Address.Port <= 0 ||
                    (request.IsTunnel ? request.Address.Scheme != "https" : request.Address.Scheme != "http"))
                    throw new FormatException();

                bool hasLength = false;
                foreach (string line in lines.Skip(1))
                {
                    if (line.Length == 0) break;
                    int colon = line.IndexOf(':');
                    if (colon <= 0) throw new FormatException();
                    string key = line.Substring(0, colon);
                    string value = line.Substring(colon + 1).Trim();
                    if (key.Any(c => c <= 32 || c >= 127) || value.Any(c => c < 32 && c != '\t'))
                        throw new FormatException();
                    if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) throw new FormatException();
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        if (hasLength || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out request.ContentLength))
                            throw new FormatException();
                        hasLength = true;
                    }
                    request.Headers.Add(new KeyValuePair<string, string>(key, value));
                }
                return request;
            }

            public string OriginHeaders()
            {
                HashSet<string> ignored = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
                foreach (var header in Headers.Where(x => x.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)))
                    foreach (string token in header.Value.Split(',')) ignored.Add(token.Trim());
                StringBuilder result = new StringBuilder();
                result.Append(Method).Append(' ').Append(Address.PathAndQuery).Append(" HTTP/1.1\r\n");
                result.Append("Host: ").Append(Address.IdnHost);
                if (!Address.IsDefaultPort) result.Append(':').Append(Address.Port);
                result.Append("\r\nConnection: close\r\n");
                foreach (var header in Headers)
                    if (!ignored.Contains(header.Key)) result.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                if (ContentLength != 0 || Headers.Any(x => x.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
                    result.Append("Content-Length: ").Append(ContentLength).Append("\r\n");
                return result.Append("\r\n").ToString();
            }
        }
    }
}
