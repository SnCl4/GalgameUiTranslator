using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameUiTranslator
{
    public enum BatchTaskKind
    {
        Recognition,
        Translation,
        Export
    }

    public enum BatchTaskStatus
    {
        Pending,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class BatchTaskItem
    {
        internal Func<BatchTaskItem, CancellationToken, Task> Executor { get; set; }

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public BatchTaskKind Kind { get; set; }
        public string Target { get; set; } = string.Empty;
        public BatchTaskStatus Status { get; set; } = BatchTaskStatus.Pending;
        public int Attempts { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ImageRelativePath { get; set; } = string.Empty;
        public List<string> RegionIds { get; set; } = new List<string>();
        public string OutputRoot { get; set; } = string.Empty;
        public string MetadataRelativePath { get; set; } = string.Empty;
        public int ResultCount { get; set; }
        public int MemoryMatchCount { get; set; }
        public object State { get; set; }

        public string KindText => Kind == BatchTaskKind.Recognition ? "识图"
            : Kind == BatchTaskKind.Translation ? "翻译"
            : "导出";

        public string StatusText => Status == BatchTaskStatus.Pending ? "等待"
            : Status == BatchTaskStatus.Running ? "运行中"
            : Status == BatchTaskStatus.Paused ? "已暂停"
            : Status == BatchTaskStatus.Completed ? "完成"
            : Status == BatchTaskStatus.Failed ? "失败"
            : "已取消";
    }

    public sealed class BatchTaskCenter
    {
        private readonly List<BatchTaskItem> _items = new List<BatchTaskItem>();
        private readonly object _sync = new object();
        private CancellationTokenSource _runCancellation;
        private TaskCompletionSource<bool> _resumeSignal;
        private volatile bool _preservePendingOnCancellation;

        public event EventHandler Changed;

        public IReadOnlyList<BatchTaskItem> Items
        {
            get
            {
                lock (_sync) return _items.ToArray();
            }
        }

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool CanResume
        {
            get
            {
                lock (_sync)
                    return !IsRunning && _items.Any(item =>
                        item.Status == BatchTaskStatus.Pending && item.Executor != null);
            }
        }

        public async Task RunAsync(
            IEnumerable<BatchTaskItem> items,
            Func<BatchTaskItem, CancellationToken, Task> executor,
            CancellationToken cancellationToken)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            if (IsRunning) throw new InvalidOperationException("已有批量任务正在运行。");

            var runItems = items.ToList();
            foreach (var item in runItems)
            {
                item.Executor = executor;
                item.Status = BatchTaskStatus.Pending;
                item.Message = string.Empty;
            }
            lock (_sync) _items.AddRange(runItems);
            NotifyChanged();
            await RunCoreAsync(runItems, cancellationToken);
        }

        public void Restore(IEnumerable<BatchTaskItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (IsRunning) throw new InvalidOperationException("批量任务运行时不能恢复其他队列。");
            var restored = items.ToList();
            foreach (var item in restored)
            {
                item.RegionIds = item.RegionIds ?? new List<string>();
                item.State = null;
                item.Executor = null;
                if (item.Status == BatchTaskStatus.Running || item.Status == BatchTaskStatus.Paused)
                {
                    item.Status = BatchTaskStatus.Pending;
                    item.Message = "上次运行中断，等待继续";
                }
            }
            lock (_sync)
            {
                _items.Clear();
                _items.AddRange(restored);
            }
            NotifyChanged();
        }

        public void AttachExecutor(
            BatchTaskItem item,
            Func<BatchTaskItem, CancellationToken, Task> executor,
            object state = null,
            bool notify = true)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            item.Executor = executor;
            item.State = state;
            if (notify) NotifyChanged();
        }

        public async Task ResumePendingAsync(CancellationToken cancellationToken)
        {
            if (IsRunning) throw new InvalidOperationException("已有批量任务正在运行。");
            List<BatchTaskItem> pending;
            lock (_sync)
            {
                pending = _items.Where(item =>
                    item.Status == BatchTaskStatus.Pending && item.Executor != null).ToList();
            }
            if (pending.Count > 0) await RunCoreAsync(pending, cancellationToken);
        }

        public async Task RetryFailedAsync(CancellationToken cancellationToken)
        {
            if (IsRunning) throw new InvalidOperationException("已有批量任务正在运行。");
            List<BatchTaskItem> failed;
            lock (_sync)
            {
                failed = _items.Where(item => item.Status == BatchTaskStatus.Failed && item.Executor != null).ToList();
                foreach (var item in failed)
                {
                    item.Status = BatchTaskStatus.Pending;
                    item.Message = string.Empty;
                }
            }
            NotifyChanged();
            if (failed.Count > 0) await RunCoreAsync(failed, cancellationToken);
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            lock (_sync)
            {
                IsPaused = true;
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var running = _items.FirstOrDefault(item => item.Status == BatchTaskStatus.Running);
                if (running != null) running.Status = BatchTaskStatus.Paused;
            }
            NotifyChanged();
        }

        public void Resume()
        {
            TaskCompletionSource<bool> signal;
            lock (_sync)
            {
                if (!IsPaused) return;
                IsPaused = false;
                var paused = _items.FirstOrDefault(item => item.Status == BatchTaskStatus.Paused);
                if (paused != null) paused.Status = BatchTaskStatus.Running;
                signal = _resumeSignal;
                _resumeSignal = null;
            }
            signal?.TrySetResult(true);
            NotifyChanged();
        }

        public void Cancel()
        {
            _preservePendingOnCancellation = false;
            try { _runCancellation?.Cancel(); }
            catch (ObjectDisposedException) { }
            Resume();
        }

        public void SuspendForShutdown()
        {
            if (!IsRunning) return;
            TaskCompletionSource<bool> signal;
            lock (_sync)
            {
                _preservePendingOnCancellation = true;
                IsPaused = false;
                signal = _resumeSignal;
                _resumeSignal = null;
                foreach (var item in _items.Where(item =>
                             item.Status == BatchTaskStatus.Pending ||
                             item.Status == BatchTaskStatus.Running ||
                             item.Status == BatchTaskStatus.Paused))
                {
                    item.Status = BatchTaskStatus.Pending;
                    item.Message = "已保存断点，等待下次继续";
                }
            }
            signal?.TrySetResult(true);
            try { _runCancellation?.Cancel(); }
            catch (ObjectDisposedException) { }
            NotifyChanged();
        }

        public void ClearCompleted()
        {
            if (IsRunning) return;
            lock (_sync)
                _items.RemoveAll(item => item.Status == BatchTaskStatus.Completed || item.Status == BatchTaskStatus.Cancelled);
            NotifyChanged();
        }

        public void ClearAll()
        {
            if (IsRunning) return;
            lock (_sync) _items.Clear();
            NotifyChanged();
        }

        public string CreateReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Galgame UI 图片汉化批量任务报告");
            builder.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            foreach (var item in Items)
            {
                builder.Append('[').Append(item.StatusText).Append("] ")
                    .Append(item.KindText).Append(" | ").Append(item.Target)
                    .Append(" | 尝试 ").Append(item.Attempts);
                if (!string.IsNullOrWhiteSpace(item.Message)) builder.Append(" | ").Append(item.Message);
                builder.AppendLine();
            }
            return builder.ToString();
        }

        private async Task RunCoreAsync(IReadOnlyList<BatchTaskItem> runItems, CancellationToken externalToken)
        {
            IsRunning = true;
            IsPaused = false;
            _preservePendingOnCancellation = false;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            NotifyChanged();
            try
            {
                foreach (var item in runItems)
                {
                    if (_runCancellation.IsCancellationRequested) break;
                    await WaitWhilePausedAsync(_runCancellation.Token);
                    item.Status = BatchTaskStatus.Running;
                    item.Attempts++;
                    NotifyChanged();
                    try
                    {
                        if (item.Executor == null)
                            throw new InvalidOperationException("任务缺少可执行操作，请重新打开工程恢复队列。");
                        await item.Executor(item, _runCancellation.Token);
                        if (_preservePendingOnCancellation && _runCancellation.IsCancellationRequested)
                        {
                            item.Status = BatchTaskStatus.Pending;
                            item.Message = "已保存断点，等待下次继续";
                        }
                        else
                        {
                            item.Status = BatchTaskStatus.Completed;
                            if (string.IsNullOrWhiteSpace(item.Message)) item.Message = "处理成功";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.Status = _preservePendingOnCancellation
                            ? BatchTaskStatus.Pending
                            : BatchTaskStatus.Cancelled;
                        item.Message = _preservePendingOnCancellation
                            ? "已保存断点，等待下次继续"
                            : "任务已取消";
                        _runCancellation.Cancel();
                    }
                    catch (Exception exception)
                    {
                        item.Status = BatchTaskStatus.Failed;
                        item.Message = exception.Message;
                    }
                    NotifyChanged();
                }

            }
            catch (OperationCanceledException)
            {
                // Cancellation is represented on each queue item rather than escaping the UI event handler.
            }
            finally
            {
                if (_runCancellation.IsCancellationRequested)
                {
                    foreach (var item in runItems.Where(item =>
                                 item.Status == BatchTaskStatus.Pending ||
                                 item.Status == BatchTaskStatus.Paused ||
                                 item.Status == BatchTaskStatus.Running))
                    {
                        item.Status = _preservePendingOnCancellation
                            ? BatchTaskStatus.Pending
                            : BatchTaskStatus.Cancelled;
                        item.Message = _preservePendingOnCancellation
                            ? "已保存断点，等待下次继续"
                            : item.Attempts > 0 ? "任务已取消" : "未开始，任务已取消";
                    }
                }
                IsPaused = false;
                IsRunning = false;
                _runCancellation.Dispose();
                _runCancellation = null;
                _preservePendingOnCancellation = false;
                _resumeSignal?.TrySetResult(true);
                _resumeSignal = null;
                NotifyChanged();
            }
        }

        private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            while (IsPaused)
            {
                Task signal;
                lock (_sync) signal = _resumeSignal?.Task ?? Task.CompletedTask;
                await WaitWithCancellationAsync(signal, cancellationToken);
            }
        }

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) == cancelled.Task)
                    cancellationToken.ThrowIfCancellationRequested();
                await task;
            }
        }

        private void NotifyChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
