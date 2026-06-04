using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using Newtonsoft.Json;

[RequireComponent(typeof(Camera))]
public class CameraStreamer : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Server")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 9999;

    [Header("Throttle")]
    [Tooltip("Send every N FixedUpdate ticks. FixedUpdate = 50 Hz, so 5 → ~10 Hz")]
    public int sendEveryNFrames = 5;

    // ── Internals ──────────────────────────────────────────────────────────
    private Camera _cam;
    private RenderTexture _rt;
    private int _camWidth;
    private int _camHeight;
    private int _frameBytes;
    private int _fixedUpdateCount;

    private TcpClient _client;
    private NetworkStream _stream;
    private Thread _sendThread;
    private Thread _recvThread;
    private volatile bool _running;

    private byte[] _latestFrame;
    private bool _frameReady;
    private readonly object _frameLock = new object();
    private readonly SemaphoreSlim _frameSem = new SemaphoreSlim(0, 1);
    private readonly byte[] _header = new byte[4];

    // ── JSON structs ───────────────────────────────────────────────────────
    [Serializable]
    public class Detection
    {
        public string cls;
        public float confidence;
    }

    [Serializable]
    public class ResponseData
    {
        public float confidence;
        public Detection[] detections;
    }

    public class ServerResponse
    {
        public float LaneConfidence;       // 0–1  (1 = perfectly centred)
        public string[] DetectedClasses;      // e.g. ["Pedestrian", "Car"]
        public float[] DetectedConfidences;  // parallel array
    }

    private static readonly object _resultLock = new object();
    private static ServerResponse _latestResult;

    /// <summary>
    /// Latest result from the Python vision server.
    /// Returns null until the first response arrives.
    /// Thread-safe — safe to call from any thread or MonoBehaviour.
    /// </summary>
    public static ServerResponse LatestResult
    {
        get { lock (_resultLock) { return _latestResult; } }
    }

    // ── Unity lifecycle ────────────────────────────────────────────────────
    void Start()
    {
        _cam = GetComponent<Camera>();
        _camWidth = _cam.pixelWidth;
        _camHeight = _cam.pixelHeight;

        _rt = new RenderTexture(_camWidth, _camHeight, 0, RenderTextureFormat.ARGB32);
        _rt.Create();

        _frameBytes = _camWidth * _camHeight * 3;

        Connect();
    }

    void FixedUpdate()
    {
        if (!_running) return;

        _fixedUpdateCount++;
        if (_fixedUpdateCount % sendEveryNFrames != 0) return;

        CaptureAsync();
    }

    void OnDestroy() => Disconnect();

    // ── Capture ────────────────────────────────────────────────────────────
    private void CaptureAsync()
    {
        _cam.targetTexture = _rt;
        _cam.Render();
        _cam.targetTexture = null;

        AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGB24, OnReadbackComplete);
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest req)
    {
        if (req.hasError || !_running) return;

        var data = req.GetData<byte>();
        var frame = new byte[_frameBytes];
        data.CopyTo(frame);

        lock (_frameLock)
        {
            _latestFrame = frame;

            if (!_frameReady)
            {
                _frameReady = true;
                if (_frameSem.CurrentCount == 0)
                    _frameSem.Release();
            }
        }
    }

    // ── Network ────────────────────────────────────────────────────────────
    private void Connect()
    {
        try
        {
            _client = new TcpClient();
            _client.NoDelay = true;
            _client.SendBufferSize = _frameBytes * 4;

            _client.Connect(serverIP, serverPort);
            _stream = _client.GetStream();

            // Handshake: width (4 bytes BE) + height (4 bytes BE)
            byte[] handshake = new byte[8];
            handshake[0] = (byte)(_camWidth >> 24);
            handshake[1] = (byte)(_camWidth >> 16);
            handshake[2] = (byte)(_camWidth >> 8);
            handshake[3] = (byte)(_camWidth);
            handshake[4] = (byte)(_camHeight >> 24);
            handshake[5] = (byte)(_camHeight >> 16);
            handshake[6] = (byte)(_camHeight >> 8);
            handshake[7] = (byte)(_camHeight);
            _stream.Write(handshake, 0, 8);

            _running = true;

            _sendThread = new Thread(SendLoop) { IsBackground = true };
            _recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            _sendThread.Start();
            _recvThread.Start();

            Debug.Log($"[CameraStreamer] Connected {serverIP}:{serverPort} | {_camWidth}x{_camHeight}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CameraStreamer] Connection failed: {e.Message}");
        }
    }

    private void Disconnect()
    {
        _running = false;
        _frameSem.Release();   // unblock SendLoop so it can exit cleanly
        _stream?.Close();
        _client?.Close();
        _sendThread?.Join(500);
        _recvThread?.Join(500);
        _rt?.Release();
    }

    // ── Send thread ────────────────────────────────────────────────────────
    private void SendLoop()
    {
        while (_running)
        {
            _frameSem.Wait();
            if (!_running) break;

            byte[] frame;
            lock (_frameLock)
            {
                frame = _latestFrame;
                _frameReady = false;
            }

            if (frame == null) continue;

            try
            {
                int len = frame.Length;
                _header[0] = (byte)(len >> 24);
                _header[1] = (byte)(len >> 16);
                _header[2] = (byte)(len >> 8);
                _header[3] = (byte)(len);

                _stream.Write(_header, 0, 4);
                _stream.Write(frame, 0, len);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CameraStreamer] Send error: {e.Message}");
                _running = false;
            }
        }
    }

    // ── Receive thread ─────────────────────────────────────────────────────
    private void ReceiveLoop()
    {
        try
        {
            while (_running)
            {
                byte[] header = ReadExact(4);
                if (header == null) break;

                int length =
                    (header[0] << 24) |
                    (header[1] << 16) |
                    (header[2] << 8) |
                     header[3];

                byte[] data = ReadExact(length);
                if (data == null) break;

                string json = System.Text.Encoding.UTF8.GetString(data);

                try
                {
                    var parsed = JsonConvert.DeserializeObject<ResponseData>(json);
                    StoreResult(parsed);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CameraStreamer] JSON parse error: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            if (_running)
                Debug.LogError($"[CameraStreamer] Receive error: {e.Message}");
        }
    }

    // ── Store parsed result in thread-safe static slot ─────────────────────
    private void StoreResult(ResponseData parsed)
    {
        int n = parsed.detections?.Length ?? 0;
        string[] cls = new string[n];
        float[] confs = new float[n];

        for (int i = 0; i < n; i++)
        {
            cls[i] = parsed.detections[i].cls;
            confs[i] = parsed.detections[i].confidence;
        }

        lock (_resultLock)
        {
            _latestResult = new ServerResponse
            {
                LaneConfidence = parsed.confidence,
                DetectedClasses = cls,
                DetectedConfidences = confs
            };
        }
    }

    // ── Read exact N bytes from stream ─────────────────────────────────────
    private byte[] ReadExact(int size)
    {
        byte[] buffer = new byte[size];
        int total = 0;

        while (total < size)
        {
            int read = _stream.Read(buffer, total, size - total);
            if (read == 0) return null;
            total += read;
        }

        return buffer;
    }
}