using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using AcerCareLite.Core.Acer;

namespace AcerCareLite.Acer;

/// <summary>
/// Starts the elevated read-only helper (one UAC prompt per check) and receives its replies over a one-shot named pipe.
/// The pipe is created here, before the helper starts, with a random name and nonce. Once the helper connects, the process
/// on the other end is verified (its image must be the helper file we launched) before a single byte is read.
/// The replies are only ever displayed: nothing in this phase acts on them.
/// </summary>
public sealed class ElevatedHelperChannel : IAcerHelperChannel, IAcerHelperWriteChannel
{
    public const string HelperFileName = "AcerCareLite.AcerHelper.exe";

    private const int ErrorCancelled = 1223; // the user declined the UAC prompt

    // Until the helper connects: UAC prompt plus process start. After it connects: just over the helper's own 40 s watchdog.
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(45);

    private readonly string _helperPath;

    public ElevatedHelperChannel() : this(Path.Combine(AppContext.BaseDirectory, HelperFileName)) { }

    public ElevatedHelperChannel(string helperPath) => _helperPath = helperPath;

    public Task<AcerHelperRunResult> RunAsync(CancellationToken ct) =>
        RunCoreAsync(AcerHelperOperation.ReadHealthStatus, null, ct);

    public Task<AcerHelperRunResult> RunSetAsync(bool enable, CancellationToken ct) =>
        RunCoreAsync(AcerHelperOperation.SetHealthMode, enable, ct);

    private async Task<AcerHelperRunResult> RunCoreAsync(AcerHelperOperation operation, bool? enable, CancellationToken ct)
    {
        var problem = ValidateHelperFile();
        if (problem != null) return AcerHelperRunResult.LaunchFailed(problem);

        var pipeName = AcerHelperProtocol.PipeNamePrefix + RandomHex();
        var nonce = RandomHex();

        NamedPipeServerStream server;
        try { server = CreateServer(pipeName); }
        catch (Exception ex) { return AcerHelperRunResult.LaunchFailed("Could not create the pipe: " + ex.Message); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(_helperPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(_helperPath) ?? AppContext.BaseDirectory,
                // All three values are validated by the helper; the pipe name and nonce are fixed-format hex.
                Arguments = string.Join(" ", AcerHelperProtocol.BuildArguments(operation, enable, pipeName, nonce))
            };

            try { process = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { return AcerHelperRunResult.Declined(); }
            catch (Exception ex) { return AcerHelperRunResult.LaunchFailed("Could not start the helper: " + ex.Message); }
            if (process == null) return AcerHelperRunResult.LaunchFailed("The helper did not start.");

            cts.CancelAfter(ApprovalTimeout);
            var connect = server.WaitForConnectionAsync(cts.Token);
            var exited = ObserveExitAsync(process, cts.Token);

            var first = await Task.WhenAny(connect, exited).ConfigureAwait(false);
            if (cts.IsCancellationRequested)
                return ct.IsCancellationRequested ? AcerHelperRunResult.Cancelled() : AcerHelperRunResult.TimedOut("No connection within the approval window.");

            if (first == exited && !connect.IsCompleted)
            {
                // The helper may have connected, written and exited in one go; give the connection a moment to show up.
                var late = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
                if (late != connect) return AcerHelperRunResult.HelperIncomplete("The helper exited before connecting.");
            }

            try { await connect.ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                return ct.IsCancellationRequested ? AcerHelperRunResult.Cancelled() : AcerHelperRunResult.TimedOut("No connection within the approval window.");
            }
            catch (Exception ex) { return AcerHelperRunResult.LaunchFailed("Pipe connection failed: " + ex.Message); }

            // Authenticate the peer before reading anything: it must be exactly the helper file we launched.
            if (!PeerVerifier.VerifyClient(server.SafePipeHandle, _helperPath, out var peerDetail))
                return AcerHelperRunResult.PeerRejected(peerDetail);

            return await HelperMessageReader.ReadAsync(server, nonce, operation, ResultTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            try { cts.Cancel(); } catch { /* already disposed or cancelled */ }
            server.Dispose();
            process?.Dispose();
        }
    }

    private string? ValidateHelperFile()
    {
        try
        {
            var info = new FileInfo(_helperPath);
            if (!info.Exists) return $"{Path.GetFileName(_helperPath)} was not found next to the application.";
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return "The helper path is a link and was refused.";
            return null;
        }
        catch (Exception ex) { return "The helper file could not be checked: " + ex.Message; }
    }

    private static NamedPipeServerStream CreateServer(string name)
    {
        var security = new PipeSecurity();
        using (var identity = WindowsIdentity.GetCurrent())
        {
            // The elevated helper normally runs as the same user. Administrators are allowed too, for the
            // "different admin account approves the prompt" case. Remote clients are denied outright.
            security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));

        // One instance only: nobody else can create another server for this name.
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
    }

    /// <summary>Best effort: an elevated process may not be observable from a non-elevated one, in which case we rely on the timeout.</summary>
    private static async Task ObserveExitAsync(Process process, CancellationToken token)
    {
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* cannot observe it */ }
        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
    }

    private static string RandomHex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
