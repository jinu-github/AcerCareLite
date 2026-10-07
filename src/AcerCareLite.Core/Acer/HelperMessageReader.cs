using System.Text;

namespace AcerCareLite.Core.Acer;

/// <summary>
/// Reads the helper's line-delimited messages from a stream and enforces the sequence, size and time limits.
/// End of stream before the result is the reliable "helper died" signal. Every failure result carries the messages
/// accepted so far, so the caller can tell whether the helper had already announced a write.
/// </summary>
public static class HelperMessageReader
{
    public static async Task<AcerHelperRunResult> ReadAsync(
        Stream stream, string nonce, AcerHelperOperation operation, TimeSpan timeout, CancellationToken ct)
    {
        var sequence = new HelperMessageSequence(operation);
        var messages = new List<AcerHelperResponse>();
        var line = new MemoryStream();
        var buffer = new byte[1024];
        long total = 0;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        string Step() => sequence.LastPhase?.ToString() ?? "none";

        while (true)
        {
            int read;
            try { read = await stream.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                return ct.IsCancellationRequested
                    ? AcerHelperRunResult.Cancelled(messages)
                    : AcerHelperRunResult.HelperIncomplete($"No result within {timeout.TotalSeconds:0} s (last step: {Step()}).", messages);
            }
            catch (IOException ex)
            {
                return AcerHelperRunResult.HelperIncomplete($"The pipe closed unexpectedly (last step: {Step()}): {ex.Message}", messages);
            }

            if (read == 0)
                return AcerHelperRunResult.HelperIncomplete($"The helper exited before sending a result (last step: {Step()}).", messages);

            total += read;
            if (total > AcerHelperProtocol.MaxResponseBytes)
                return AcerHelperRunResult.InvalidResponse("The helper sent too much data.", messages);

            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b != (byte)'\n')
                {
                    if (line.Length >= AcerHelperProtocol.MaxLineBytes)
                        return AcerHelperRunResult.InvalidResponse("A message line was too long.", messages);
                    line.WriteByte(b);
                    continue;
                }

                var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
                line.SetLength(0);

                if (!AcerHelperProtocol.TryParse(text, nonce, out var message, out var error))
                    return AcerHelperRunResult.InvalidResponse(error, messages);
                if (!sequence.Accept(message!, out error))
                    return AcerHelperRunResult.InvalidResponse(error, messages);

                messages.Add(message!);

                if (sequence.Result != null)
                {
                    if (i + 1 < read)
                        return AcerHelperRunResult.InvalidResponse("Unexpected data after the result.", messages);

                    // The helper closes the pipe right after the result. Anything arriving instead is a protocol violation.
                    if (await SomethingFollowsAsync(stream, ct).ConfigureAwait(false))
                        return AcerHelperRunResult.InvalidResponse("Unexpected data after the result.", messages);

                    return AcerHelperRunResult.Responded(sequence.Result, messages);
                }
            }
        }
    }

    private static async Task<bool> SomethingFollowsAsync(Stream stream, CancellationToken ct)
    {
        using var tail = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tail.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            var probe = new byte[16];
            return await stream.ReadAsync(probe.AsMemory(), tail.Token).ConfigureAwait(false) > 0;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
    }
}
