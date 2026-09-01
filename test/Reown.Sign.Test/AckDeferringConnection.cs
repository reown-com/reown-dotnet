using Reown.Core.Network;
using Reown.Core.Network.Interfaces;
using Reown.Core.Network.Models;
using Reown.Core.Network.Websocket;

namespace Reown.Sign.Test;

/// <summary>
///     Builds connections that hold back the relay's acknowledgement of our own <c>irn_publish</c> until
///     the peer's response has been delivered, which forces the frame ordering that a loaded relay
///     produces naturally.
/// </summary>
internal sealed class AckDeferringConnectionBuilder : IConnectionBuilder
{
    private readonly WebsocketConnectionBuilder _inner = new();

    /// <summary>
    ///     The connection most recently created by this builder.
    /// </summary>
    public AckDeferringConnection? Connection { get; private set; }

    public async Task<IJsonRpcConnection> CreateConnection(string url, string? context = null)
    {
        var connection = new AckDeferringConnection(await _inner.CreateConnection(url, context));
        Connection = connection;
        return connection;
    }
}

/// <summary>
///     Decorates a relay connection so that, while armed, the acknowledgement of an <c>irn_publish</c> we
///     sent is withheld until an inbound relay push has been forwarded and had time to be processed. The
///     relay gives no ordering guarantee between the two frames, so this reproduces deterministically what
///     otherwise happens intermittently: the peer's response reaches the SDK before the publish returns.
/// </summary>
internal sealed class AckDeferringConnection : IJsonRpcConnection
{
    private const string PublishMethod = "irn_publish";
    private const string SubscriptionMethod = "irn_subscription";

    /// <summary>
    ///     How long to keep holding the acknowledgement after the inbound push was forwarded, so that the
    ///     push is fully processed before the publish call returns.
    /// </summary>
    private static readonly TimeSpan PostPushDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     Upper bound on how long an acknowledgement is held when no inbound push arrives at all, so that
    ///     a request without a response cannot wedge the connection.
    /// </summary>
    private static readonly TimeSpan SafetyDelay = TimeSpan.FromSeconds(10);

    private readonly IJsonRpcConnection _inner;
    private readonly object _gate = new();
    private readonly HashSet<long> _pendingPublishIds = [];
    private readonly List<string> _heldAcks = [];

    private bool _armed;

    public AckDeferringConnection(IJsonRpcConnection inner)
    {
        _inner = inner;

        _inner.PayloadReceived += OnInnerPayloadReceived;
        _inner.Closed += (_, e) => Closed?.Invoke(this, e);
        _inner.ErrorReceived += (_, e) => ErrorReceived?.Invoke(this, e);
        _inner.Opened += (_, e) => Opened?.Invoke(this, e);
        _inner.RegisterErrored += (_, e) => RegisterErrored?.Invoke(this, e);
    }

    /// <summary>
    ///     Whether an inbound push was forwarded while an acknowledgement was being held. When this is
    ///     false the test did not actually exercise the inverted ordering.
    /// </summary>
    public bool DeliveredPushBeforeAck { get; private set; }

    /// <summary>
    ///     Whether a publish acknowledgement was actually withheld while armed.
    /// </summary>
    public bool HeldAcknowledgement { get; private set; }

    public bool Connected => _inner.Connected;
    public bool Connecting => _inner.Connecting;
    public string Url => _inner.Url;
    public bool IsPaused => _inner.IsPaused;

    public event EventHandler<string>? PayloadReceived;
    public event EventHandler? Closed;
    public event EventHandler<Exception>? ErrorReceived;
    public event EventHandler<object>? Opened;
    public event EventHandler<Exception>? RegisterErrored;

    public Task Open() => _inner.Open();

    public Task Open<T>(T options) => _inner.Open(options);

    public Task Close()
    {
        ReleaseAll();
        return _inner.Close();
    }

    public Task SendRequest<T>(IJsonRpcRequest<T> requestPayload, object context)
    {
        lock (_gate)
        {
            if (_armed && requestPayload.Method == PublishMethod)
            {
                _pendingPublishIds.Add(requestPayload.Id);
            }
        }

        return _inner.SendRequest(requestPayload, context);
    }

    public Task SendResult<T>(IJsonRpcResult<T> responsePayload, object context) => _inner.SendResult(responsePayload, context);

    public Task SendError(IJsonRpcError errorPayload, object context) => _inner.SendError(errorPayload, context);

    public void Dispose()
    {
        _inner.PayloadReceived -= OnInnerPayloadReceived;
        ReleaseAll();
        _inner.Dispose();
    }

    /// <summary>
    ///     Starts withholding publish acknowledgements.
    /// </summary>
    public void Arm()
    {
        lock (_gate)
        {
            _armed = true;
            DeliveredPushBeforeAck = false;
            HeldAcknowledgement = false;
        }
    }

    /// <summary>
    ///     Stops withholding acknowledgements and forwards anything still held.
    /// </summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _armed = false;
            _pendingPublishIds.Clear();
        }

        Release();
    }

    private void OnInnerPayloadReceived(object? sender, string json)
    {
        var isPush = json.Contains(SubscriptionMethod, StringComparison.Ordinal);
        var holdThisFrame = false;
        var releaseAfterPush = false;

        lock (_gate)
        {
            if (_armed)
            {
                if (isPush)
                {
                    if (_heldAcks.Count > 0)
                    {
                        DeliveredPushBeforeAck = true;
                        releaseAfterPush = true;
                    }
                }
                else if (TryGetResponseId(json, out var id) && _pendingPublishIds.Remove(id))
                {
                    _heldAcks.Add(json);
                    HeldAcknowledgement = true;
                    holdThisFrame = true;
                }
            }
        }

        if (holdThisFrame)
        {
            _ = ReleaseAfterAsync(SafetyDelay);
            return;
        }

        PayloadReceived?.Invoke(this, json);

        if (releaseAfterPush)
        {
            _ = ReleaseAfterAsync(PostPushDelay);
        }
    }

    private async Task ReleaseAfterAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        Release();
    }

    private void Release()
    {
        string[] held;
        lock (_gate)
        {
            if (_heldAcks.Count == 0)
            {
                return;
            }

            held = _heldAcks.ToArray();
            _heldAcks.Clear();
        }

        foreach (var payload in held)
        {
            PayloadReceived?.Invoke(this, payload);
        }
    }

    private static bool TryGetResponseId(string json, out long id)
    {
        id = 0;

        var idIndex = json.IndexOf("\"id\"", StringComparison.Ordinal);
        if (idIndex < 0)
        {
            return false;
        }

        var colon = json.IndexOf(':', idIndex);
        if (colon < 0)
        {
            return false;
        }

        var start = colon + 1;
        while (start < json.Length && (json[start] == ' ' || json[start] == '"'))
        {
            start++;
        }

        var end = start;
        while (end < json.Length && char.IsDigit(json[end]))
        {
            end++;
        }

        return end > start && long.TryParse(json.AsSpan(start, end - start), out id);
    }
}
