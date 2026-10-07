using AcerCareLite.Core.Acer;
using AcerCareLite.Core.Capabilities;
using AcerCareLite.Core.Presentation;
using Xunit;

namespace AcerCareLite.Tests;

/// <summary>Phase 7b-0: protocol v2, message sequencing, peer authentication policy, protected-location policy. All read-only.</summary>
public class Phase7b0Tests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    private const AcerHelperOperation Op = AcerHelperOperation.ReadHealthStatus;

    private static AcerHelperResponse Result(AcerHelperOutcome outcome = AcerHelperOutcome.Ok) => new()
    {
        Protocol = AcerHelperProtocol.Version, Nonce = Nonce, Operation = Op, Phase = AcerHelperPhase.Result,
        Outcome = outcome, FunctionList = 3, Return = new[] { 0, 0 }, FunctionStatus = new[] { 1, 0, 0, 0, 0 }
    };

    private static AcerHelperResponse Started() => AcerHelperProtocol.Progress(Nonce, Op, AcerHelperPhase.Started);

    // ---- Protocol v2 ----

    [Fact]
    public void Protocol_is_version_3()
    {
        Assert.Equal(3, AcerHelperProtocol.Version);
    }

    [Fact]
    public void Progress_message_round_trips()
    {
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Started()), Nonce, out var parsed, out var error), error);
        Assert.Equal(AcerHelperPhase.Started, parsed!.Phase);
        Assert.Equal(Op, parsed.Operation);
    }

    [Fact]
    public void Result_message_round_trips_with_operation_and_phase()
    {
        Assert.True(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Result()), Nonce, out var parsed, out var error), error);
        Assert.Equal(AcerHelperPhase.Result, parsed!.Phase);
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, parsed.FunctionStatus);
    }

    [Fact]
    public void Progress_message_with_a_payload_is_rejected()
    {
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Started() with { FunctionList = 3 }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Started() with { FunctionStatus = new[] { 1 } }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Started() with { Detail = "x" }), Nonce, out _, out _));
        Assert.False(AcerHelperProtocol.TryParse(AcerHelperProtocol.Serialize(Started() with { Outcome = AcerHelperOutcome.WmiError }), Nonce, out _, out _));
    }

    [Theory]
    [InlineData("{\"protocol\":3,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"outcome\":0}")]                                  // operation and phase missing
    [InlineData("{\"protocol\":3,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"operation\":1,\"outcome\":0}")]                  // phase missing
    [InlineData("{\"protocol\":3,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"phase\":2,\"outcome\":0}")]                      // operation missing
    [InlineData("{\"protocol\":3,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"operation\":9,\"phase\":2,\"outcome\":0}")]      // undefined operation
    [InlineData("{\"protocol\":3,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"operation\":1,\"phase\":9,\"outcome\":0}")]      // undefined phase
    [InlineData("{\"protocol\":2,\"nonce\":\"0123456789abcdef0123456789abcdef\",\"operation\":1,\"phase\":2,\"outcome\":0}")]      // previous protocol
    public void Protocol_v2_rejects_missing_or_undefined_operation_phase_and_old_versions(string json)
    {
        Assert.False(AcerHelperProtocol.TryParse(json, Nonce, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Operation_argument_is_strict()
    {
        Assert.True(AcerHelperProtocol.TryParseOperationArg("read", out var op));
        Assert.Equal(AcerHelperOperation.ReadHealthStatus, op);
        Assert.Equal("read", AcerHelperProtocol.OperationArg(AcerHelperOperation.ReadHealthStatus));

        foreach (var bad in new[] { "READ", "Read", "write", "read ", " read", "", "read;write", "--op", null })
            Assert.False(AcerHelperProtocol.TryParseOperationArg(bad, out _), bad ?? "(null)");
    }

    [Fact]
    public void Line_cap_is_4_kb_and_total_cap_is_16_kb()
    {
        Assert.Equal(4096, AcerHelperProtocol.MaxLineBytes);
        Assert.Equal(16 * 1024, AcerHelperProtocol.MaxResponseBytes);
        var longLine = AcerHelperProtocol.Serialize(Result()) + new string(' ', AcerHelperProtocol.MaxLineBytes);
        Assert.False(AcerHelperProtocol.TryParse(longLine, Nonce, out _, out _));
    }

    // ---- Message sequence ----

    [Fact]
    public void Sequence_accepts_started_then_result()
    {
        var seq = new HelperMessageSequence(Op);
        Assert.Null(seq.LastPhase);
        Assert.True(seq.Accept(Started(), out var e1), e1);
        Assert.Equal(AcerHelperPhase.Started, seq.LastPhase);
        Assert.False(seq.IsComplete);
        Assert.True(seq.Accept(Result(), out var e2), e2);
        Assert.True(seq.IsComplete);
        Assert.Equal(AcerHelperOutcome.Ok, seq.Result!.Outcome);
    }

    [Fact]
    public void Sequence_rejects_result_first()
    {
        var seq = new HelperMessageSequence(Op);
        Assert.False(seq.Accept(Result(), out var error));
        Assert.NotEmpty(error);
        Assert.False(seq.IsComplete);
    }

    [Fact]
    public void Sequence_rejects_duplicate_started()
    {
        var seq = new HelperMessageSequence(Op);
        Assert.True(seq.Accept(Started(), out _));
        Assert.False(seq.Accept(Started(), out _));
    }

    [Fact]
    public void Sequence_rejects_anything_after_the_result()
    {
        var seq = new HelperMessageSequence(Op);
        Assert.True(seq.Accept(Started(), out _));
        Assert.True(seq.Accept(Result(), out _));
        Assert.False(seq.Accept(Result(), out _));
        Assert.False(seq.Accept(Started(), out _));
    }

    [Fact]
    public void Sequence_rejects_a_different_operation()
    {
        var seq = new HelperMessageSequence(Op);
        Assert.False(seq.Accept(Started() with { Operation = AcerHelperOperation.SetHealthMode }, out _));
    }

    // ---- Classifier: new run kinds and full arrays ----

    [Fact]
    public void Peer_rejection_is_an_error_with_its_own_outcome()
    {
        var r = AcerHealthClassifier.Classify(AcerHelperRunResult.PeerRejected("pid 4: C:\\evil.exe"), Now);
        Assert.Equal(CapabilityStatus.Error, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.PeerNotTrusted, r.Outcome);
        Assert.Null(r.State);
        Assert.Contains("evil.exe", r.Capability.Evidence);
    }

    [Fact]
    public void Incomplete_helper_is_an_error_not_unsupported()
    {
        var r = AcerHealthClassifier.Classify(AcerHelperRunResult.HelperIncomplete("exited after Started"), Now);
        Assert.Equal(CapabilityStatus.Error, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.HelperUnavailable, r.Outcome);
        Assert.Contains("Started", r.Capability.Evidence);
    }

    [Fact]
    public void A_progress_message_is_never_classified_as_a_result()
    {
        var r = AcerHealthClassifier.Classify(AcerHelperRunResult.Responded(Started()), Now);
        Assert.Equal(CapabilityStatus.Error, r.Capability.Status);
        Assert.Equal(AcerReadOutcome.InvalidResponse, r.Outcome);
    }

    [Fact]
    public void Read_state_carries_the_full_raw_arrays()
    {
        var r = AcerHealthClassifier.Classify(AcerHelperRunResult.Responded(Result() with { InstanceName = AcerHealthClassifier.ExpectedInstance }), Now);
        Assert.Equal(AcerReadOutcome.Read, r.Outcome);
        Assert.Equal(3, r.State!.FunctionList);
        Assert.Equal(new[] { 0, 0 }, r.State.Return);
        Assert.Equal(new[] { 1, 0, 0, 0, 0 }, r.State.Status);
        Assert.True(r.State.Enabled);
    }

    // ---- Peer policy ----

    private const string App = @"C:\Program Files\AcerCareLite\AcerCareLite.exe";

    [Fact]
    public void Peer_exact_path_matches_case_insensitively()
    {
        Assert.True(PeerPolicy.IsTrustedPeer(App, App, false));
        Assert.True(PeerPolicy.IsTrustedPeer(App.ToUpperInvariant(), App, false));
        Assert.True(PeerPolicy.IsTrustedPeer(@"\\?\" + App, App, false));
    }

    [Fact]
    public void Peer_dot_dot_segments_are_resolved_before_comparing()
    {
        Assert.True(PeerPolicy.IsTrustedPeer(@"C:\Program Files\AcerCareLite\x\..\AcerCareLite.exe", App, false));
        Assert.False(PeerPolicy.IsTrustedPeer(@"C:\Program Files\AcerCareLite\..\Evil\AcerCareLite.exe", App, false));
    }

    [Theory]
    [InlineData(@"C:\Program Files\AcerCareLite\AcerCareLite.exe.evil")]
    [InlineData(@"C:\Program Files\AcerCareLite\AcerCareLite.exee")]
    [InlineData(@"C:\Program Files\AcerCareLite\Other.exe")]
    [InlineData(@"C:\Program Files\AcerCareLite2\AcerCareLite.exe")]
    [InlineData(@"C:\Program Files (x86)\AcerCareLite\AcerCareLite.exe")]
    [InlineData(@"D:\Program Files\AcerCareLite\AcerCareLite.exe")]
    [InlineData(@"\\server\share\AcerCareLite.exe")]
    [InlineData(@"AcerCareLite.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void Peer_other_paths_are_rejected(string actual)
    {
        Assert.False(PeerPolicy.IsTrustedPeer(actual, App, false));
        Assert.False(PeerPolicy.IsTrustedPeer(actual, App, true) && !actual.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Peer_null_image_is_rejected()
    {
        Assert.False(PeerPolicy.IsTrustedPeer(null, App, false));
        Assert.False(PeerPolicy.IsTrustedPeer(null, App, true));
    }

    [Fact]
    public void Dotnet_host_is_accepted_only_when_the_development_flag_is_set()
    {
        const string host = @"C:\Program Files\dotnet\dotnet.exe";
        Assert.False(PeerPolicy.IsTrustedPeer(host, App, false));
        Assert.True(PeerPolicy.IsTrustedPeer(host, App, true));
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe.evil")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Users\x\mydotnet.exe")]
    [InlineData(@"C:\Users\x\dotnet.exe\payload.exe")]
    public void Development_flag_does_not_widen_trust_to_other_programs(string actual)
    {
        Assert.False(PeerPolicy.IsTrustedPeer(actual, App, true));
    }

    // ---- Protected-location policy ----

    private const string Root = @"C:\Program Files";
    private const string Helper = @"C:\Program Files\AcerCareLite\AcerCareLite.AcerHelper.exe";
    private const string SystemSid = "S-1-5-18";
    private const string Admins = "S-1-5-32-544";
    private const string Installer = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string Users = "S-1-5-32-545";
    private const string AuthUsers = "S-1-5-11";
    private const string SomeUser = "S-1-5-21-1111-2222-3333-1001";

    private const long Full = 0x1F01FF;
    private const long ReadExecute = 0x1200A9;
    private const long Modify = 0x1301BF;

    private static AceFact Ace(string sid, long rights, bool allow = true, bool inheritOnly = false) => new(sid, allow, rights, inheritOnly);

    private static List<AceFact> DefaultAces() => new()
    {
        Ace(SystemSid, Full), Ace(Admins, Full), Ace(Installer, Full), Ace(Users, ReadExecute), Ace("S-1-15-2-1", ReadExecute)
    };

    private static SecuredItem Item(string path, string? owner = Admins, bool reparse = false, List<AceFact>? aces = null) =>
        new(path, reparse, owner, aces ?? DefaultAces());

    private static List<SecuredItem> GoodChain() => new()
    {
        Item(Helper), Item(@"C:\Program Files\AcerCareLite"), Item(Root, Installer)
    };

    private static HelperLocationStatus Evaluate(List<SecuredItem> chain, string helper = Helper) =>
        ProtectedLocationPolicy.Evaluate(helper, Root, chain);

    [Fact]
    public void Chain_paths_run_from_the_file_up_to_program_files()
    {
        var chain = ProtectedLocationPolicy.ChainPaths(Helper, Root);
        Assert.Equal(new[] { Helper, @"C:\Program Files\AcerCareLite", Root }, chain!);
    }

    [Theory]
    [InlineData(@"D:\AcerCareLite-phase1\AcerCareLite\src\bin\AcerCareLite.AcerHelper.exe")]
    [InlineData(@"C:\Program Files Evil\AcerCareLite\AcerCareLite.AcerHelper.exe")]
    [InlineData(@"C:\Program Files\..\Users\x\AcerCareLite.AcerHelper.exe")]
    [InlineData(@"C:\Program Files")]
    [InlineData("")]
    public void Chain_paths_are_null_outside_program_files(string path)
    {
        Assert.Null(ProtectedLocationPolicy.ChainPaths(path, Root));
    }

    [Fact]
    public void Default_program_files_permissions_are_protected()
    {
        var status = Evaluate(GoodChain());
        Assert.True(status.IsProtected, status.Reason);
    }

    [Fact]
    public void Development_location_is_not_protected()
    {
        var dev = @"D:\AcerCareLite-phase1\AcerCareLite\src\x\AcerCareLite.AcerHelper.exe";
        var status = Evaluate(new List<SecuredItem> { Item(dev) }, dev);
        Assert.False(status.IsProtected);
        Assert.Contains("Program Files", status.Reason);
    }

    [Fact]
    public void Look_alike_directory_is_not_protected()
    {
        var evil = @"C:\Program Files Evil\AcerCareLite.AcerHelper.exe";
        Assert.False(Evaluate(new List<SecuredItem> { Item(evil), Item(@"C:\Program Files Evil") }, evil).IsProtected);
    }

    [Fact]
    public void Missing_permission_data_for_any_link_in_the_chain_is_not_protected()
    {
        foreach (var skip in new[] { 0, 1, 2 })
        {
            var chain = GoodChain();
            chain.RemoveAt(skip);
            Assert.False(Evaluate(chain).IsProtected, $"skipped {skip}");
        }
        Assert.False(Evaluate(new List<SecuredItem>()).IsProtected);
    }

    [Fact]
    public void Reparse_point_anywhere_in_the_chain_is_not_protected()
    {
        for (var i = 0; i < 3; i++)
        {
            var chain = GoodChain();
            chain[i] = chain[i] with { IsReparsePoint = true };
            Assert.False(Evaluate(chain).IsProtected, $"reparse at {i}");
        }
    }

    [Theory]
    [InlineData(SomeUser)]
    [InlineData(Users)]
    [InlineData(AuthUsers)]
    [InlineData(null)]
    public void Untrusted_or_missing_owner_is_not_protected(string? owner)
    {
        var chain = GoodChain();
        chain[1] = chain[1] with { OwnerSid = owner };
        Assert.False(Evaluate(chain).IsProtected);
    }

    [Theory]
    [InlineData(Users, Modify)]
    [InlineData(Users, 0x2L)]            // WriteData / CreateFiles
    [InlineData(Users, 0x4L)]            // AppendData / CreateDirectories
    [InlineData(Users, 0x10000L)]        // Delete
    [InlineData(Users, 0x40L)]           // DeleteSubdirectoriesAndFiles
    [InlineData(Users, 0x40000L)]        // ChangePermissions
    [InlineData(Users, 0x80000L)]        // TakeOwnership
    [InlineData(Users, 0x100L)]          // WriteAttributes
    [InlineData(Users, 0x40000000L)]     // GenericWrite
    [InlineData(Users, 0x10000000L)]     // GenericAll
    [InlineData(AuthUsers, Modify)]
    [InlineData(SomeUser, Full)]
    public void Write_like_access_for_an_untrusted_account_is_not_protected(string sid, long rights)
    {
        for (var i = 0; i < 3; i++)
        {
            var chain = GoodChain();
            var aces = DefaultAces();
            aces.Add(Ace(sid, rights));
            chain[i] = chain[i] with { Aces = aces };
            var status = Evaluate(chain);
            Assert.False(status.IsProtected, $"link {i}");
            Assert.Contains(sid, status.Reason);
        }
    }

    [Fact]
    public void Read_and_execute_for_everyone_is_fine()
    {
        var chain = GoodChain();
        var aces = DefaultAces();
        aces.Add(Ace(AuthUsers, ReadExecute));
        aces.Add(Ace(SomeUser, ReadExecute));
        chain[0] = chain[0] with { Aces = aces };
        Assert.True(Evaluate(chain).IsProtected);
    }

    [Fact]
    public void Inherit_only_and_deny_entries_do_not_count_as_write_access()
    {
        var chain = GoodChain();
        var aces = DefaultAces();
        aces.Add(Ace(Users, Modify, allow: true, inheritOnly: true)); // applies to children only, not this object
        aces.Add(Ace(Users, Modify, allow: false));                   // a deny never grants anything
        chain[1] = chain[1] with { Aces = aces };
        Assert.True(Evaluate(chain).IsProtected);
    }

    [Fact]
    public void Trusted_accounts_may_hold_full_control_and_creator_owner_is_ignored()
    {
        var chain = GoodChain();
        var aces = DefaultAces();
        aces.Add(Ace("S-1-3-0", Full));
        chain[2] = chain[2] with { Aces = aces };
        Assert.True(Evaluate(chain).IsProtected);
    }

    // ---- Card: install text ----

    private sealed class FakeGate : IHelperLocationGate
    {
        public HelperLocationStatus Current { get; init; } = HelperLocationStatus.NotProtected("dev");
    }

    private sealed class IdleService : IAcerBatteryHealthService
    {
        public AcerBatteryHealthResult? Last => null;
        public Task<AcerBatteryHealthResult> CheckAsync(CancellationToken ct = default) => Task.FromResult(AcerBatteryHealthResult.NotChecked);
    }

    [Fact]
    public void Card_shows_a_protected_install()
    {
        var card = new AcerHealthCardViewModel(new IdleService(), new FakeGate { Current = HelperLocationStatus.Protected("Under Program Files.") });
        Assert.StartsWith("Helper location: protected", card.InstallText);
    }

    [Fact]
    public void Card_shows_an_unprotected_install_with_the_reason()
    {
        var card = new AcerHealthCardViewModel(new IdleService(), new FakeGate { Current = HelperLocationStatus.NotProtected("Development build.") });
        Assert.StartsWith("Helper location: not protected", card.InstallText);
        Assert.Contains("Development build.", card.InstallText);
    }

    [Fact]
    public void Card_without_a_gate_has_no_install_text_and_still_works()
    {
        var card = new AcerHealthCardViewModel(new IdleService());
        Assert.Equal("", card.InstallText);
        Assert.Equal("Not checked", card.StatusText);
    }
}
