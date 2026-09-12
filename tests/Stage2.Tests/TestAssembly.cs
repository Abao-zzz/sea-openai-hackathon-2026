using Xunit;
// SQL integration cases share one server. The production engine intentionally
// rejects concurrent Snapshot batches rather than queuing an unnoticed run.
[assembly: CollectionBehavior(DisableTestParallelization=true)]
