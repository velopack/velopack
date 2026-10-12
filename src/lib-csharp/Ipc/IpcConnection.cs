using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading.Tasks;
using Velopack.Logging;

namespace Velopack.Ipc
{
    /// <summary>
    /// One end of an IPC connection: reads and writes whole frames, bounded by <see cref="SetTimeout"/>. On Windows the
    /// stream is an overlapped named pipe (a timed operation is <c>ReadAsync</c>/<c>WriteAsync</c> + <c>Task.Wait</c>;
    /// on timeout the stream is disposed, which cancels the pending I/O on every target framework). On Unix it is a
    /// <see cref="NetworkStream"/> over a Unix domain socket, whose own receive/send timeouts apply. Any other stream
    /// (tests) is used synchronously.
    /// </summary>
    internal sealed class IpcConnection : IDisposable
    {
        private readonly Stream _stream;
        private readonly Socket? _socket;
        private readonly bool _overlapped;
        private TimeSpan? _timeout;

        public IpcConnection(Stream stream, Socket? socket = null)
        {
            _stream = stream;
            _socket = socket;
            _overlapped = stream is PipeStream;
        }

        /// <summary> Applies to every later <see cref="Receive"/> and <see cref="Send"/> (null = wait forever). </summary>
        public void SetTimeout(TimeSpan? timeout)
        {
            _timeout = timeout;
            if (_socket != null) {
                var ms = timeout.HasValue ? Math.Max(1, (int) timeout.Value.TotalMilliseconds) : 0;
                _socket.ReceiveTimeout = ms;
                _socket.SendTimeout = ms;
            }
        }

        /// <summary>
        /// Receives one message. Throws <see cref="TimeoutException"/>, <see cref="IOException"/> (incl.
        /// <see cref="EndOfStreamException"/> / <see cref="InvalidDataException"/> for frame errors) or
        /// <see cref="FormatException"/> for a malformed message.
        /// </summary>
        public IpcMessage Receive() => IpcFraming.Decode(ReadFrame());

        /// <summary> Sends one message. </summary>
        public void Send(IpcMessage message)
        {
            var frame = IpcFraming.Encode(message);
            Write(frame);
        }

        /// <summary>
        /// Sends <paramref name="message"/> and waits for the single reply, on the calling thread, bounded by
        /// <paramref name="timeout"/>.
        /// </summary>
        public IpcMessage Request(IpcMessage message, TimeSpan timeout)
        {
            SetTimeout(timeout);
            Send(message);
            return Receive();
        }

        /// <summary>
        /// Serves one request/reply exchange: receives a message (within <see cref="IpcProtocol.ServerIoTimeout"/>),
        /// validates the envelope, answers with the handler's reply (the handler copies the <c>id</c>) and flushes so
        /// the client has the reply before the caller closes the connection. A framing or I/O error ends the exchange
        /// without a reply; a malformed message or version 0 is answered with <c>invalid message</c>, a newer version
        /// with <c>unsupported version</c>.
        /// </summary>
        public void ServeRequest(Func<IpcMessage, IpcMessage> handler, IVelopackLogger? log = null)
        {
            SetTimeout(IpcProtocol.ServerIoTimeout);
            byte[] payload;
            try {
                payload = ReadFrame();
            } catch (Exception ex) {
                log?.Debug($"IPC: dropping connection without a reply: {ex.Message}");
                return;
            }

            IpcMessage reply;
            try {
                var request = IpcFraming.Decode(payload);
                if (request.Version == 0) {
                    reply = IpcMessage.Error(IpcProtocol.ErrInvalidMessage).ReplyTo(request);
                } else if (request.Version > IpcProtocol.ProtocolVersion) {
                    reply = IpcMessage.Error(IpcProtocol.ErrUnsupportedVersion).ReplyTo(request);
                } else {
                    reply = handler(request);
                }
            } catch (FormatException) {
                reply = IpcMessage.Error(IpcProtocol.ErrInvalidMessage);
            }

            try {
                Send(reply);
                Flush();
            } catch (Exception ex) {
                log?.Debug($"IPC: reply could not be written: {ex.Message}");
            }
        }

        /// <summary>
        /// Blocks until the peer has read everything written (a pipe: <c>FlushFileBuffers</c>; never <c>Disconnect</c>,
        /// which discards unread data and would make the client miss its ack).
        /// </summary>
        public void Flush()
        {
            if (VelopackRuntimeInfo.IsWindows && _stream is PipeStream pipe) {
                pipe.WaitForPipeDrain();
            } else {
                _stream.Flush();
            }
        }

        private byte[] ReadFrame()
        {
            var header = new byte[4];
            ReadExactly(header);
            var payload = new byte[IpcFraming.DecodeLength(header)];
            ReadExactly(payload);
            return payload;
        }

        private void ReadExactly(byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length) {
                var read = Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) {
                    throw new EndOfStreamException("the connection was closed inside a frame");
                }

                offset += read;
            }
        }

        private int Read(byte[] buffer, int offset, int count)
        {
            if (_overlapped) {
                return Await(_stream.ReadAsync(buffer, offset, count));
            }

            try {
                return _stream.Read(buffer, offset, count);
            } catch (IOException ex) when (IsSocketTimeout(ex)) {
                throw new TimeoutException("IPC read timed out", ex);
            }
        }

        private void Write(byte[] buffer)
        {
            if (_overlapped) {
                Await(_stream.WriteAsync(buffer, 0, buffer.Length));
                return;
            }

            try {
                _stream.Write(buffer, 0, buffer.Length);
            } catch (IOException ex) when (IsSocketTimeout(ex)) {
                throw new TimeoutException("IPC write timed out", ex);
            }
        }

        private static bool IsSocketTimeout(IOException ex) =>
            ex.InnerException is SocketException se && (se.SocketErrorCode == SocketError.TimedOut || se.SocketErrorCode == SocketError.WouldBlock);

        /// <summary> Waits for an overlapped operation; on timeout the stream is disposed (cancelling it) and the faulted task observed. </summary>
        private int Await(Task<int> task)
        {
            Await((Task) task);
            return task.Result;
        }

        private void Await(Task task)
        {
            bool completed;
            try {
                completed = _timeout == null || task.Wait(_timeout.Value);
            } catch (AggregateException) {
                completed = true;
            }

            if (completed) {
                task.GetAwaiter().GetResult(); // rethrows the operation's own exception, unwrapped
                return;
            }

            task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            Dispose();
            throw new TimeoutException("IPC operation timed out");
        }

        public void Dispose()
        {
            try {
                _stream.Dispose();
            } catch {
                // closing a broken pipe/socket is not an error
            }
        }
    }
}
