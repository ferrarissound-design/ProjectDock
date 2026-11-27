using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Services;

/// <summary>複数プロジェクトの状態を並列数を制限して取得する。</summary>
public sealed class StatusRefresher
{
    private readonly ProjectStatusService _service;
    private readonly IAppLogger _log;

    public StatusRefresher(ProjectStatusService service, IAppLogger log)
    {
        _service = service;
        _log = log;
    }

    /// <param name="onEach">1件終わるごとに呼ばれる（スレッドプールから呼ばれるので UI 側でマーシャリングすること）。</param>
    public async Task RefreshAsync(IReadOnlyList<ProjectEntry> projects, int maxParallelism,
        Action<ProjectEntry, ProjectStatus> onEach, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, maxParallelism));
        // Task.Run で必ずスレッドプールに逃がす（SemaphoreSlim.WaitAsync は同期完了しうるので、
        // そのままだと最初の数件の git 起動やディレクトリ確認が UI スレッドで走ってしまう）
        var tasks = projects.Select(p => Task.Run(async () =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var status = await _service.GetStatusAsync(p, ct).ConfigureAwait(false);
                onEach(p, status);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // キャンセルは呼び出し側に任せる
            }
            catch (Exception ex)
            {
                _log.Error($"状態取得に失敗: {p.Path}", ex);
            }
            finally
            {
                gate.Release();
            }
        }, CancellationToken.None)).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}
