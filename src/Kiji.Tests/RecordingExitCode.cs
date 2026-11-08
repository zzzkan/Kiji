namespace Kiji.Tests;

internal sealed class RecordingExitCode : IProcessExitCode
{
    public int Value { get; set; }
}
