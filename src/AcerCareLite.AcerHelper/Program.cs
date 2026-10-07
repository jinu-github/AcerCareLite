using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using AcerCareLite.Acer;
using AcerCareLite.Core.Acer;

namespace AcerCareLite.AcerHelper;

/// <summary>
/// Elevated helper. Two strictly validated command lines exist (see AcerHelperProtocol.TryParseArguments):
/// a read, and a change of the health mode. It connects to the pipe the main app created, verifies that the pipe's owner
/// really is the main application, reports "Started", does its one job, reports the result, and exits.
/// Default builds contain no write code: the change operation is refused by a stub.
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitBadArgs = 2;
    private const int ExitNoPipe = 3;
    private const int ExitWatchdog = 4;
    private const int ExitUntrustedPeer = 5;

    internal const string MainExeFileName = "AcerCareLite.exe";
    // Must exceed the writer's readback window plus its other steps; the app's result timeout must exceed this (WriteGuardTests pins the order).
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(40);

    private static int Main(string[] args)
    {
        if (!AcerHelperProtocol.TryParseArguments(args, out var operation, out var enable, out var pipeName, out var nonce)) return ExitBadArgs;

        // The main process cannot kill an elevated child, so the helper limits its own lifetime.
        using var watchdog = new Timer(_ => Environment.Exit(ExitWatchdog), null, Watchdog, Timeout.InfiniteTimeSpan);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
        try { client.Connect(5000); }
        catch { return ExitNoPipe; }

        // Never send anything to a pipe whose owner is not the main application installed next to this helper.
        // A change request gets no development exception, in any build configuration.
        var expectedApp = Path.Combine(AppContext.BaseDirectory, MainExeFileName);
        var trusted = operation == AcerHelperOperation.SetHealthMode
            ? PeerVerifier.VerifyServerStrict(client.SafePipeHandle, expectedApp, out _)
            : PeerVerifier.VerifyServer(client.SafePipeHandle, expectedApp, out _);
        if (!trusted) return ExitUntrustedPeer;

        // Announce that we are alive before any firmware work: a later silent exit is then detectable as a crash.
        if (!Send(client, AcerHelperProtocol.Progress(nonce, operation, AcerHelperPhase.Started), nonce, operation)) return ExitNoPipe;

        var result = operation == AcerHelperOperation.SetHealthMode
            ? CollectChange(nonce, enable, message => Send(client, message, nonce, operation))
            : CollectRead(nonce);

        if (!Send(client, result, nonce, operation)) return ExitNoPipe;

        try { client.WaitForPipeDrain(); } catch { /* the data is already buffered for the reader */ }
        return ExitOk;
    }

    private static AcerHelperResponse CollectRead(string nonce)
    {
        try
        {
            if (!IsElevated())
                return Basic(AcerHelperOperation.ReadHealthStatus, nonce, AcerHelperOutcome.NotElevated, "The helper is not running as administrator.");
            return BatteryHealthReader.Read(nonce);
        }
        catch (Exception ex)
        {
            return Basic(AcerHelperOperation.ReadHealthStatus, nonce, AcerHelperOutcome.Unexpected, Trim(ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static AcerHelperResponse CollectChange(string nonce, bool enable, Func<AcerHelperResponse, bool> send)
    {
        const AcerHelperOperation op = AcerHelperOperation.SetHealthMode;
        AcerHelperResponse result;
        try
        {
            result = !IsElevated()
                ? Basic(op, nonce, AcerHelperOutcome.NotElevated, "The helper is not running as administrator.")
                : BatteryHealthWriter.Execute(nonce, enable, send);
        }
        catch (Exception ex)
        {
            result = Basic(op, nonce, AcerHelperOutcome.Unexpected, Trim(ex.GetType().Name + ": " + ex.Message));
        }

        // Every change result echoes the request, so the app can verify the reply answers what it asked.
        return result with { RequestedEnabled = enable };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool Send(Stream client, AcerHelperResponse message, string nonce, AcerHelperOperation operation)
    {
        try
        {
            var text = AcerHelperProtocol.Serialize(message);
            if (text.Length > AcerHelperProtocol.MaxLineBytes)
                text = AcerHelperProtocol.Serialize(Basic(operation, nonce, AcerHelperOutcome.Unexpected, "The result was too large to send."));

            var bytes = Encoding.UTF8.GetBytes(text + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static AcerHelperResponse Basic(AcerHelperOperation operation, string nonce, AcerHelperOutcome outcome, string? detail) => new()
    {
        Protocol = AcerHelperProtocol.Version,
        Nonce = nonce,
        Operation = operation,
        Phase = AcerHelperPhase.Result,
        Outcome = outcome,
        Detail = detail == null ? null : Trim(detail)
    };

    internal static string Trim(string text) =>
        text.Length <= AcerHelperProtocol.MaxDetailLength ? text : text[..AcerHelperProtocol.MaxDetailLength];
}
