using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Taskschedule {
    public class Task {

        public Action<object> Job {
            get {
                return this._job;
            }
        }

        public object State {
            get {
                return this._state;
            }
        }

        public string Tag {
            get {
                return this._tag;
            }
        }

        private readonly Action<object> _job;
        private readonly object _state;
        private readonly string _tag;

        public Task(Action<object> job, string tag = null, object state= null) {
            this._tag = tag ?? Guid.NewGuid().ToString().Substring(0, 6);
            this._state = state;
            this._job = job;
        }

        internal void Execute(object state = null) {
            this._job.Invoke(state);
        }
    }

    public class Taskscheduler : IDisposable {

        public static event UnhandledExceptionEventHandler UnhandledException;
        private const int DEFAULT_WORKER_COUNT = 30;
        private bool _stop = false;
        private bool _disposed = false;

        private readonly object _syncRoot = new object();
        private readonly int _maxWorkersCount;
        private readonly Queue<Task> _taskQueue;
        private readonly Semaphore _taskLock;
        private readonly List<Thread> _workers;

        public Taskscheduler() :this(DEFAULT_WORKER_COUNT) { }

        public Taskscheduler(int maxWorker)  {
            this._maxWorkersCount = maxWorker;
            this._taskQueue = new Queue<Task>();
            this._workers = new List<Thread>(maxWorker);
            this._taskLock = new Semaphore(0, maxWorker);
            this.CreateWorkers(maxWorker);
        }

        public void Reset() {
            lock (_syncRoot) {
                this.Disposal();
                this._stop = false;
                this.CreateWorkers(this._maxWorkersCount);
            }
        }

        public void TaskEnqueue(Task task) {
            lock (this._syncRoot) {
                if (this._stop)
                    return;

                if (this._workers.Count == 0)
                    this.CreateWorkers(this._maxWorkersCount);

                if (this._taskQueue.Count < this._maxWorkersCount) {
                    this._taskQueue.Enqueue(task);
                    this._taskLock.Release(1);
                } else {
                    this._taskQueue.Enqueue(task);
                }
            }
        }

        public void EndScheduler(bool isCancel = false) {
            if (this._workers.Count > 0) {
                lock (this._syncRoot) {
                    if (this._workers.Count == 0)
                        return;
                    if (isCancel) {
                        this.Disposal();
                        return;
                    }

                    this._stop = true;
                    this._taskLock.Release(this._workers.Count);
                    foreach (Thread thread in this._workers) {
                        try {
                            thread.Join();                        
                        } catch { }
                    }
                    this._workers.Clear();
                }
            }
        }

        private void Disposal() {
            lock (this._syncRoot) {
                foreach (Task item in this._taskQueue) {
                    try {
                        if (item.State is IDisposable)
                            ( (IDisposable)item ).Dispose();
                    } catch { }
                }
                this._taskQueue.Clear();

                foreach (Thread thread in this._workers) {
                    try {
                        if (thread != null)
                            thread.Abort(thread.Name + " Disposal");
                    } catch { }
                }
                this._workers.Clear();
            }
        }

        private void CreateWorkers(int count) {
            for (int i = 0; i < count; i++) {
                var worker = new Thread(new ThreadStart(ProcessTask));
                worker.Name = "work#" + i;
                worker.IsBackground = true;
                worker.Start();
                this._workers.Add(worker);
            }
        }

        private void ProcessTask() {
            this._taskLock.WaitOne();
            while (!this._stop) {
                Task task = null;
                lock (this._syncRoot) {
                    if (this._taskQueue.Count > 0) {
                        try {
                            task = this._taskQueue.Dequeue();
                        } catch { }
                    }
                    
                }
                if (task == null) {
                    this._taskLock.WaitOne();
                    continue;
                }

                try {
                    task.Execute();
                } catch (Exception ex) {
                    try {
                        UnhandledException?.Invoke(typeof(Taskscheduler), new UnhandledExceptionEventArgs(ex, false));
                    } catch { }
                }
            }
        }

        protected virtual void Dispose(bool isDisposing) {
            if (!isDisposing)
                return;
            if (!this._disposed) {
                this.Disposal();
            }
            this._disposed = true;
        }

        public void Dispose() {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
