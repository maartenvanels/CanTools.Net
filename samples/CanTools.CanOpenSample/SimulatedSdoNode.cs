using CanTools.CanOpen;
using CanTools.Transport;

namespace CanTools.CanOpenSample;

/// <summary>
/// A tiny in-process CANopen node that answers SDO requests from a fixed object
/// dictionary, so the sample runs the real <see cref="SdoClient"/> end to end
/// without any hardware or external server. It plays the server side of the CiA
/// 301 SDO protocol: expedited and segmented upload (read) and download (write).
/// Block transfer is not simulated — the client only uses it when asked to.
/// </summary>
internal sealed class SimulatedSdoNode : ICanChannel
{
    private readonly byte _nodeId;
    private readonly Dictionary<(ushort Index, byte Subindex), byte[]> _dictionary;
    private readonly Queue<CanFrame> _outgoing = new();

    // Segmented upload (server -> client) state.
    private byte[]? _uploadValue;
    private int _uploadOffset;

    // Segmented download (client -> server) state.
    private (ushort Index, byte Subindex)? _downloadTarget;
    private readonly List<byte> _downloadBuffer = [];

    public SimulatedSdoNode(byte nodeId, Dictionary<(ushort, byte), byte[]> dictionary)
    {
        _nodeId = nodeId;
        _dictionary = dictionary;
    }

    private uint RequestCobId => 0x600u + _nodeId;

    private uint ResponseCobId => 0x580u + _nodeId;

    public ValueTask SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        // A request from the client: answer it immediately by queuing the response.
        if (frame.Id == RequestCobId)
        {
            Handle(frame.Data);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<CanFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        // Every client request is answered synchronously in SendAsync, so a
        // response is always waiting by the time the client reads.
        if (_outgoing.Count > 0)
        {
            return ValueTask.FromResult(_outgoing.Dequeue());
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("The client read a response that was never requested.");
    }

    private void Reply(byte[] data) => _outgoing.Enqueue(new CanFrame(ResponseCobId, data));

    private void Handle(byte[] data)
    {
        switch (SdoFrame.Parse(SdoDirection.Request, data))
        {
            case SdoUploadRequest request:
                BeginUpload(request.Index, request.Subindex);
                break;

            case SdoUploadSegmentRequest request:
                ContinueUpload(request.Toggle);
                break;

            case SdoDownloadRequest request:
                BeginDownload(request);
                break;

            case SdoDownloadSegmentRequest request:
                ContinueDownload(request);
                break;
        }
    }

    private void BeginUpload(ushort index, byte subindex)
    {
        if (!_dictionary.TryGetValue((index, subindex), out var value))
        {
            Reply(new SdoAbort(index, subindex, SdoAbortCode.ObjectDoesNotExist).ToBytes());
            return;
        }

        if (value.Length <= 4)
        {
            // Expedited: the value fits in the initiate response.
            Reply(new SdoUploadResponse(index, subindex, value).ToBytes());
            return;
        }

        // Segmented: announce the size, then stream the value in segments.
        _uploadValue = value;
        _uploadOffset = 0;
        Reply(new SdoUploadResponse(index, subindex)
        {
            SizeSpecified = true,
            Size = (uint)value.Length,
        }.ToBytes());
    }

    private void ContinueUpload(bool toggle)
    {
        var value = _uploadValue ?? throw new InvalidOperationException("No segmented upload in progress.");
        var count = Math.Min(7, value.Length - _uploadOffset);
        var chunk = value.AsSpan(_uploadOffset, count).ToArray();
        _uploadOffset += count;
        var isLast = _uploadOffset >= value.Length;

        Reply(new SdoUploadSegmentResponse(toggle, chunk, isLast).ToBytes());

        if (isLast)
        {
            _uploadValue = null;
        }
    }

    private void BeginDownload(SdoDownloadRequest request)
    {
        if (request.IsExpedited)
        {
            _dictionary[(request.Index, request.Subindex)] = request.ExpeditedData!;
            Reply(new SdoDownloadResponse(request.Index, request.Subindex).ToBytes());
            return;
        }

        _downloadTarget = (request.Index, request.Subindex);
        _downloadBuffer.Clear();
        Reply(new SdoDownloadResponse(request.Index, request.Subindex).ToBytes());
    }

    private void ContinueDownload(SdoDownloadSegmentRequest request)
    {
        var target = _downloadTarget ?? throw new InvalidOperationException("No segmented download in progress.");
        _downloadBuffer.AddRange(request.Data);
        Reply(new SdoDownloadSegmentResponse(request.Toggle).ToBytes());

        if (request.IsLast)
        {
            _dictionary[target] = _downloadBuffer.ToArray();
            _downloadTarget = null;
        }
    }
}
