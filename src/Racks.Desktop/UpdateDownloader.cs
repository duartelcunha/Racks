using System.ComponentModel;
using NetSparkleUpdater;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;

namespace Racks.Desktop;

// NetSparkle 3.1 replaces its cancellation source during cancellation, allowing
// a pending response to continue. Keep one token per transfer; NetSparkle still
// owns signature verification and installation.
internal sealed class UpdateDownloader : IUpdateDownloader
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private CancellationTokenSource? active;
    public bool IsDownloading => Volatile.Read(ref active) != null;
    public event DownloadFromPathToPathEvent? DownloadStarted;
    public event DownloadProgressEvent? DownloadProgressChanged;
    public event AsyncCompletedEventHandler? DownloadFileCompleted;

    public async Task DownloadFile(Uri? uri, string downloadFilePath)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        if (Interlocked.CompareExchange(ref active, cancellation, null) != null) return;
        Exception? error = null;
        var cancelled = false;
        try
        {
            ArgumentNullException.ThrowIfNull(uri);
            DownloadStarted?.Invoke(this, uri.AbsoluteUri, downloadFilePath);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(false);
            await using var output = new FileStream(downloadFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, true);
            var expected = response.Content.Headers.ContentLength;
            var buffer = new byte[65536];
            long received = 0;
            var lastProgress = -1;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation.Token).ConfigureAwait(false);
                received += count;
                var progress = expected is > 0 ? (int)Math.Min(100, received * 100 / expected.Value) : 0;
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    DownloadProgressChanged?.Invoke(this, new ItemDownloadProgressEventArgs(progress, this, received, expected ?? 0));
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (expected.HasValue && expected != received) throw new IOException("The update download was incomplete.");
            await output.FlushAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { cancelled = true; }
        catch (Exception exception) { error = exception; }
        finally { Interlocked.CompareExchange(ref active, null, cancellation); }
        // Close streams before NetSparkle verifies or removes the file.
        DownloadFileCompleted?.Invoke(this, new AsyncCompletedEventArgs(error, cancelled, null));
    }

    public void CancelDownload()
    {
        try { Volatile.Read(ref active)?.Cancel(); }
        catch (ObjectDisposedException) { /* Transfer completed concurrently. */ }
    }
    public Task<string?> RetrieveDestinationFileNameAsync(AppCastItem item) => Task.FromResult<string?>(null);
    public void Dispose() { CancelDownload(); client.Dispose(); }
}
