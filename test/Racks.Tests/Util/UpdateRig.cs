using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Racks.Util;

namespace Racks.Tests.Util;

// Test doubles and a release builder for the updater. Nothing here touches the network, a toast, a
// process or WPF: HTTP goes to FakeHttp, toasts to FakeNotifier, the installer start to FakeLauncher.

internal sealed class FakeNotifier : IUpdateNotifier
{
    private readonly List<(string Kind, string Header, string Body)> _messages = new();
    public int ProgressStarted;
    public int ProgressHidden;
    public List<int> ProgressValues { get; } = new();

    public IReadOnlyList<(string Kind, string Header, string Body)> Messages { get { lock (_messages) return _messages.ToList(); } }
    public IReadOnlyList<(string Header, string Body)> Plain => Messages.Where(m => m.Kind == "toast").Select(m => (m.Header, m.Body)).ToList();
    public IReadOnlyList<(string Header, string Body)> Offers => Messages.Where(m => m.Kind == "offer").Select(m => (m.Header, m.Body)).ToList();
    public string AllText => string.Join("\n", Messages.Select(m => m.Header + "\n" + m.Body));

    public void Show(string header, string body) { lock (_messages) _messages.Add(("toast", header, body)); }
    public void ShowOffer(string header, string body) { lock (_messages) _messages.Add(("offer", header, body)); }

    public IUpdateProgress StartProgress(string header)
    {
        Interlocked.Increment(ref ProgressStarted);
        lock (_messages) _messages.Add(("progress", header, ""));
        return new Progress(this);
    }

    private sealed class Progress(FakeNotifier owner) : IUpdateProgress
    {
        public void Report(int percent) { lock (owner.ProgressValues) owner.ProgressValues.Add(percent); }
        public void Hide() => Interlocked.Increment(ref owner.ProgressHidden);
    }
}

internal sealed class FakeLauncher : IInstallerLauncher
{
    public List<string> Started { get; } = new();
    public Action<string>? OnStart;
    public Exception? Throws;

    public void Start(string installerPath)
    {
        lock (Started) Started.Add(installerPath);
        OnStart?.Invoke(installerPath);
        if (Throws != null) throw Throws;
    }
}

internal sealed class FakeShutdown : IAppShutdown
{
    public int Count;
    public void Shutdown() => Interlocked.Increment(ref Count);
}

// Answers by exact URL. Anything not registered is a 404 that is also recorded, so a test can assert
// that a URL was (or was never) requested.
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _requests = new();
    private readonly List<string> _userAgents = new();

    public IReadOnlyList<string> Requests { get { lock (_gate) return _requests.ToList(); } }
    public IReadOnlyList<string> UserAgents { get { lock (_gate) return _userAgents.ToList(); } }

    public void Route(string url, Func<HttpRequestMessage, HttpResponseMessage> respond) { lock (_gate) _routes[url] = respond; }
    public void Route(string url, byte[] body) => Route(url, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.AbsoluteUri;
        Func<HttpRequestMessage, HttpResponseMessage>? route;
        lock (_gate)
        {
            _requests.Add(url);
            _userAgents.Add(request.Headers.UserAgent.ToString());
            _routes.TryGetValue(url, out route);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(route != null ? route(request) : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

// Follows a 3xx once, as SocketsHttpHandler does in production (GitHub's asset URLs redirect to
// another host). The URL of the final request is recorded by the inner FakeHttp.
internal sealed class FollowRedirectsHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } to)
        {
            response.Dispose();
            using var next = new HttpRequestMessage(HttpMethod.Get, to);
            return await base.SendAsync(next, cancellationToken);
        }
        return response;
    }
}

// A body that never ends and counts what the reader pulled from it.
internal sealed class EndlessContent : HttpContent
{
    public long Produced;
    protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => throw new NotSupportedException();
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Gen(this));

    private sealed class Gen(EndlessContent owner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)7, offset, count);
            Interlocked.Add(ref owner.Produced, count);
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span.Fill(7);
            Interlocked.Add(ref owner.Produced, buffer.Length);
            return new ValueTask<int>(buffer.Length);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

// One GitHub release for the updater to find, with every part tweakable before the check runs.
internal sealed class UpdateRig : IDisposable
{
    public const string Running = "1.4.0.0";
    public const string Repo = "https://github.com/duartelcunha/Racks/releases/download/";

    public readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public readonly FakeHttp Http = new();
    public readonly FakeNotifier Notifier = new();
    public readonly FakeLauncher Launcher = new();
    public readonly FakeShutdown Shutdown = new();
    public readonly string TempRoot = Path.Combine(Path.GetTempPath(), "racks-updater-test-" + Guid.NewGuid().ToString("N"));
    public HttpMessageHandler? HandlerOverride;
    public string PublicKey;

    // The release as GitHub lists it.
    public string Tag = "v1.5.0";
    public string ManifestVersion = "1.5.0";
    public string? ReleaseName = "Racks 1.5.0";
    public string? ReleaseBody = "Release notes";
    public string? InstallerUrl;       // default: the release path + installer name
    public string? ManifestUrl;
    public string? SignatureUrl;

    // The bytes: what the manifest was made from, and what the "server" serves.
    public byte[] SignedInstaller = Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray();
    public byte[]? ServedInstaller;    // default: SignedInstaller
    public UpdateManifest? ManifestOverride;
    public byte[]? ManifestBytesOverride;   // served and signed exactly as given
    public byte[]? SignatureBytesOverride;  // served exactly as given
    public ECDsa? SigningKey;               // default: Key
    public bool OmitManifest, OmitSignature;
    public string? ExtraReleaseJson;        // another release object listed before this one
    public Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> RouteOverrides { get; } = new();

    public UpdateRig()
    {
        PublicKey = UpdateSignature.ExportPublicKey(Key);
        Directory.CreateDirectory(TempRoot);
    }

    public Updater Updater { get; private set; } = null!;
    public string Base => Repo + Tag + "/";
    public string InstallerAsset => InstallerUrl ?? Base + UpdateManifest.InstallerName(ManifestVersion);
    public string ManifestAsset => ManifestUrl ?? Base + UpdateManifest.ManifestName(ManifestVersion);
    public string SignatureAsset => SignatureUrl ?? Base + UpdateManifest.SignatureName(ManifestVersion);

    public UpdateManifest Manifest => ManifestOverride
        ?? UpdateManifest.ForInstaller(ManifestVersion, SHA256.HashData(SignedInstaller), SignedInstaller.Length);

    public Updater Build(string? runningVersion = null, string? publicKey = null)
    {
        Updater = new Updater(Notifier, Launcher, Shutdown, TempRoot, HandlerOverride ?? Http, publicKey ?? PublicKey,
            () => runningVersion ?? Running);
        return Updater;
    }

    public string CatalogJson()
    {
        string v = Tag.StartsWith('v') ? Tag[1..] : Tag;
        var assets = new List<object>
        {
            new { name = UpdateManifest.InstallerName(v), browser_download_url = InstallerAsset },
            new { name = "SHA256SUMS.txt", browser_download_url = Base + "SHA256SUMS.txt" },
        };
        if (!OmitManifest) assets.Add(new { name = UpdateManifest.ManifestName(v), browser_download_url = ManifestAsset });
        if (!OmitSignature) assets.Add(new { name = UpdateManifest.SignatureName(v), browser_download_url = SignatureAsset });
        string json = JsonSerializer.Serialize(new[] { new { tag_name = Tag, name = ReleaseName, body = ReleaseBody, draft = false, prerelease = false, assets } });
        return ExtraReleaseJson == null ? json : "[" + ExtraReleaseJson + "," + json[1..];
    }

    // Registers the catalog and the three assets on the fake server.
    public void Serve()
    {
        Http.Route(Updater_ReleasesApi, Encoding.UTF8.GetBytes(CatalogJson()));
        byte[] manifestBytes = ManifestBytesOverride ?? Manifest.ToCanonicalBytes();
        byte[] sig = SignatureBytesOverride ?? Encoding.ASCII.GetBytes(UpdateSignature.Sign(manifestBytes, SigningKey ?? Key));
        Http.Route(ManifestAsset, manifestBytes);
        Http.Route(SignatureAsset, sig);
        Http.Route(InstallerAsset, ServedInstaller ?? SignedInstaller);
    }

    public Func<HttpRequestMessage, HttpResponseMessage>? InstallerAssetRoute;

    public const string Updater_ReleasesApi = Racks.Updater.ReleasesApi;

    // Check (with toasts, as the user's own click) and, when it offered, the pending update is ready to install.
    public async Task CheckAsync(bool showToastIfNoUpdate = true)
    {
        Serve();
        if (InstallerAssetRoute != null) Http.Route(InstallerAsset, InstallerAssetRoute);
        foreach (var (url, route) in RouteOverrides) Http.Route(url, route);
        await Updater.CheckAsync(Updater_ReleasesApi, showToastIfNoUpdate);
    }

    public string[] Downloads() => Directory.Exists(TempRoot)
        ? Directory.GetDirectories(TempRoot, "Racks-update-*")
        : Array.Empty<string>();

    public void Dispose()
    {
        Key.Dispose();
        SigningKey?.Dispose();
        try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
    }
}
