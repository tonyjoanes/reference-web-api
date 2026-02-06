using Serilog;

namespace Platform.Hosting;

/// <summary>
/// Creates a standard bootstrap Serilog logger for use before the host
/// is built. This ensures startup diagnostics (configuration source
/// loading, Azure connection failures, etc.) are visible in the console.
///
/// Call <see cref="Create"/> as the first line in Program.cs.
/// </summary>
public static class BootstrapLogger
{
    /// <summary>
    /// Creates and assigns the bootstrap logger to <see cref="Log.Logger"/>.
    /// Returns it for convenience, but it's already globally available.
    /// </summary>
    public static Serilog.ILogger Create()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] (bootstrap) {Message:lj}{NewLine}{Exception}")
            .CreateBootstrapLogger();

        return Log.Logger;
    }
}
