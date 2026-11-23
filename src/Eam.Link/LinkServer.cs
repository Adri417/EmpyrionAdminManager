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
        // Messages to the engine. The game thread only queues them: the socket is written by a background
        // thread, so a slow or frozen EAM can never block the game (it used to freeze the whole server).
        private readonly Queue<Dictionary<string, object>> _outbox = new Queue<Dictionary<string, object>>();
        private readonly AutoResetEvent _outboxSignal = new AutoResetEvent(false);
        private int _dropped;

        public const int MaxQueued = 2000;
        /// <summary>Messages waiting for the engine. Beyond this, EAM is not reading: the link is reset (EAM reconnects and refreshes).</summary>
        public const int MaxOutbox = 20000;
        /// <summary>A write to the engine that takes longer than this closes the link instead of waiting.</summary>
        public const int SendTimeoutMs = 10000;

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

        /// <summary>Queues a message for the engine. Never blocks and never touches the network (safe on the game thread).</summary>
        public void Send(Dictionary<string, object> message)
        {
            if (message == null) return;
            var overflow = false;
            lock (_gate)
            {
                if (_writer == null) return;
                if (_outbox.Count >= MaxOutbox) { _dropped++; overflow = true; CloseClientLocked(); }
                else _outbox.Enqueue(message);
            }
            if (overflow) _log("EAM link: EAM is not reading, link reset (" + MaxOutbox + " messages waiting)");
            else _outboxSignal.Set();
        }

        /// <summary>Background thread of one connection: encodes and writes the queued messages.</summary>
        private void WriteLoop(TcpClient c, StreamWriter writer)
        {
            var batch = new List<Dictionary<string, object>>();
            try
            {
                while (!_stopping)
                {
                    _outboxSignal.WaitOne(1000);
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_writer, writer)) return; // replaced or closed
                        while (_outbox.Count > 0 && batch.Count < 500) batch.Add(_outbox.Dequeue());
                    }
                    if (batch.Count == 0) continue;
                    foreach (var m in batch)
                    {
                        string line;
                        try { line = MiniJson.Write(m); }
                        catch (Exception e) { _log("EAM link: cannot encode message: " + e.Message); continue; }
                        writer.Write(line);
                        writer.Write('\n');
                    }
                    writer.Flush(); // bounded by the socket's send timeout
                    batch.Clear();
                    lock (_gate) { if (_outbox.Count > 0) _outboxSignal.Set(); }
                }
            }
            catch (Exception e)
            {
                if (!_stopping) _log("EAM link: write to EAM failed (" + e.GetType().Name + "), link reset");
                lock (_gate) { if (ReferenceEquals(_writer, writer)) CloseClientLocked(); }
                try { c.Close(); } catch { }
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
                c.SendTimeout = SendTimeoutMs;

                lock (_gate)
                {
                    CloseClientLocked(); // only one engine at a time: the newest wins
                    _client = c;
                    _writer = writer;
                }
                var w = writer;
                new Thread(() => WriteLoop(c, w)) { IsBackground = true, Name = "EAM link writer" }.Start();
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
            _outbox.Clear();
            _outboxSignal.Set(); // lets the writer thread see it is closed
        }

        public void Dispose()
        {
            _stopping = true;
            try { _listener?.Stop(); } catch { }
            lock (_gate) CloseClientLocked();
        }
    }
}
