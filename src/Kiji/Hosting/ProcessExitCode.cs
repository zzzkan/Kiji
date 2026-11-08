namespace Kiji.Hosting;

internal sealed class ProcessExitCode : IProcessExitCode
{
    public int Value { set => Environment.ExitCode = value; }
}
