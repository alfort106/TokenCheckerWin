using TokenChecker.Models;
using TokenChecker.Services;

namespace TokenChecker.Providers;

public sealed class CodexUsageProvider : IUsageProvider, IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly CodexAppServerClient _client;
    private int _consecutiveFailures;
    private DateTime _nextAttemptUtc = DateTime.MinValue;
    private DomainError? _lastError;

    public CodexUsageProvider(CodexAppServerClient? client = null)
        => _client = client ?? new CodexAppServerClient();

    public async Task<ServiceUsage> FetchAsync(CancellationToken ct = default)
    {
        // 失敗が続く間は指数バックオフし、毎ポーリングでの Start→Exit 連打を防ぐ。
        if (_lastError != null && DateTime.UtcNow < _nextAttemptUtc)
            throw _lastError;

        try
        {
            var usage = await FetchOnceAsync(ct);
            _consecutiveFailures = 0;
            _nextAttemptUtc = DateTime.MinValue;
            _lastError = null;
            return usage;
        }
        catch (OperationCanceledException)
        {
            throw; // 終了・キャンセルは失敗回数に数えない
        }
        catch (DomainError e)
        {
            _consecutiveFailures++;
            var seconds = Math.Min(MaxBackoff.TotalSeconds, 5 * Math.Pow(2, _consecutiveFailures - 1));
            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(seconds);

            // stderr に手掛かりがあれば（古い codex / 認証切れ等）エラーに添える。
            var stderr = _client.LastStderr;
            // 認証失効は分かりやすいメッセージを保持し、stderr で上書きしない。
            _lastError = (string.IsNullOrWhiteSpace(stderr) || e.Kind == DomainErrorKind.CodexUnauthorized)
                ? e
                : DomainError.CodexRpcError($"{e.Message} / {stderr}");
            throw _lastError;
        }
    }

    /// <summary>手動更新時にバックオフを解除し、即座に再取得できるようにする。</summary>
    public void ResetBackoff()
    {
        _consecutiveFailures = 0;
        _nextAttemptUtc = DateTime.MinValue;
        _lastError = null;
        // 認証切れ後に再ログインした場合、古いプロセスを停止して新トークンで再起動させる。
        _client.Stop();
    }

    private async Task<ServiceUsage> FetchOnceAsync(CancellationToken ct)
    {
        // 取得完了後は必ずプロセスを終了してメモリを解放する。
        try
        {
            await _client.StartAsync(ct);
            try
            {
                return Map(await _client.ReadRateLimitsAsync(ct));
            }
            catch (DomainError e) when (e.Kind == DomainErrorKind.CodexProcessExited)
            {
                // プロセスが落ちていたら一度だけ再起動して再試行
                _client.Stop();
                await _client.StartAsync(ct);
                return Map(await _client.ReadRateLimitsAsync(ct));
            }
        }
        finally
        {
            _client.Stop();
        }
    }

    private static ServiceUsage Map(CodexRateLimitsDto dto) => new(
        FiveHour:     dto.FiveHourRateLimit(),
        Weekly:       dto.WeeklyRateLimit(),
        WeeklySonnet: null);

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
