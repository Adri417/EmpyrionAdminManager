// Runs inside the game (EAM mod). Shared with the engine's tests.
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Eam.Link
{
    /// <summary>
    /// Listens on 127.0.0.1 only and accepts the EAM engine after the key check.
    /// Never throws into the game: every failure is logged and the server keeps listening.
    /// </summary>
    public sealed class LinkServer : IDisposable
    {
        private readonly int _port;
        private readonly string _key;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly Queue<Dictionary<string, object>> _inbox = new Queue<Dictionary<string, object>>();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _stopping;
        private TcpClient _client;
        private StreamWriter _writer;

        public const int MaxQueued = 2000;

        public LinkServer(int port, string key, Action<string> log)
        {
            _port = port;
            _key = key;
            _log = log ?? (_ => { });
        }

        public bool Connected { get { lock (_gate) return _writer != null; } }

        public event Action ClientAuthenticated;

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "EAM link accept" };
            _acceptThread.Start();
            _log("EAM link listening on 127.0.0.1:" + _port);
        }

        /// <summary>Takes the requests received from the engine (called from the game thread).</summary>
        public List<Dictionary<string, object>> Drain()
        {
            lock (_gate)
            {
                if (_inbox.Count == 0) return null;
                var l = new List<Dictionary<string, object>>(_inbox);
                _inbox.Clear();
                return l;
            }
        }

        public void Send(Dictionary<string, object> message)
        {
            string line;
            try { line = MiniJson.Write(message); }
            catch (Exception e) { _log("EAM link: cannot encode message: " + e.Message); return; }
            lock (_gate)
            {
                if (_writer == null) return;
                try { _writer.Write(line); _writer.Write('\n'); _writer.Flush(); }
                catch { CloseClientLocked(); }
            }
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient c;
                try { c = _listener.AcceptTcpClient(); }
                catch { if (_stopping) return; Thread.Sleep(500); continue; }
                var t = new Thread(() => Serve(c)) { IsBackground = true, Name = "EAM link client" };
                t.Start();
            }
        }

        private void Serve(TcpClient c)
        {
            StreamWriter writer = null;
            try
            {
                c.NoDelay = true;
                var stream = c.GetStream();
                var reader = new StreamReader(stream, new UTF8Encoding(false));
                writer = new StreamWriter(stream, new UTF8Encoding(false));
                var nonce = LinkProtocol.NewNonce();
                writer.Write(LinkProtocol.Greeting + " " + LinkProtocol.Version + " " + nonce + "\n");
                writer.Flush();

                c.ReceiveTimeout = 10000;
                var auth = reader.ReadLine();
                var expected = "AUTH " + LinkProtocol.Sign(_key, nonce);
                if (!LinkProtocol.SameSignature(auth, expected))
                {
                    _log("EAM link: connection refused (wrong key)");
                    c.Close();
                    return;
                }
                writer.Write("OK\n");
                writer.Flush();
                c.ReceiveTimeout = 0;

                lock (_gate)
                {
                    CloseClientLocked(); // only one engine at a time: the newest wins
                    _client = c;
                    _writer = writer;
                }
                _log("EAM link: engine connected");
                ClientAuthenticated?.Invoke();

                while (!_stopping)
                {
                    var line = ReadLine(reader);
                    if (line == null) break;
                    if (line.Length == 0) continue;
                    Dictionary<string, object> msg;
                    try { msg = MiniJson.ParseObject(line); }
                    catch { _log("EAM link: invalid message ignored"); continue; }
                    lock (_gate)
                    {
                        if (_inbox.Count < MaxQueued) _inbox.Enqueue(msg);
                    }
                }
            }
            catch (Exception e)
            {
                if (!_stopping) _log("EAM link: connection closed (" + e.GetType().Name + ")");
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_writer, writer)) CloseClientLocked();
                }
                try { c.Close(); } catch { }
            }
        }

        private static string ReadLine(StreamReader r)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var ch = r.Read();
                if (ch < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (ch == '\n') return sb.ToString().TrimEnd('\r');
                if (sb.Length >= LinkProtocol.MaxLineLength) throw new IOException("line_too_long");
                sb.Append((char)ch);
            }
        }

        private void CloseClientLocked()
        {
            try { _client?.Close(); } catch { }
            _client = null;
            _writer = null;
        }

        public void Dispose()
        {
            _stopping = true;
            try { _listener?.Stop(); } catch { }
            lock (_gate) CloseClientLocked();
        }
    }
}
