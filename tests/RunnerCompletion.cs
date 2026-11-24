using System;
using System.Threading.Tasks;

namespace Minecraft;

/// <summary>
/// Observes a runner's asynchronous work and then attempts every cleanup step.
/// Any work, cleanup, or completion-reporting exception turns the process
/// result red; a failing diagnostic callback cannot prevent cleanup or exit.
/// </summary>
internal static class RunnerCompletion
{
    public static async Task<int> ExecuteAsync(
            Func<Task>? operation,
            Func<Task>[] cleanupSteps,
            Action<Exception> reportFailure,
            Action<int> requestExit,
            Action<int>? reportResult = null)
    {
        int exitCode = 0;
        if (operation != null)
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                exitCode = 1;
                ReportFailure(reportFailure, exception);
            }
        }

        for (int index = 0; index < cleanupSteps.Length; index++)
        {
            try
            {
                await cleanupSteps[index]();
            }
            catch (Exception exception)
            {
                exitCode = 1;
                ReportFailure(reportFailure, exception);
            }
        }

        try
        {
            reportResult?.Invoke(exitCode);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            ReportFailure(reportFailure, exception);
        }

        try
        {
            requestExit(exitCode);
        }
        catch (Exception exception)
        {
            ReportFailure(reportFailure, exception);
        }

        return exitCode;
    }

    private static void ReportFailure(Action<Exception> reportFailure, Exception exception)
    {
        try
        {
            reportFailure(exception);
        }
        catch
        {
            // Logging is best effort; it must never bypass later cleanup or exit.
        }
    }
}
