namespace Kiji.Hosting;

/// <summary>Allows publish tests to observe exit codes without changing process state.</summary>
internal interface IProcessExitCode
{
    int Value { set; }
}
