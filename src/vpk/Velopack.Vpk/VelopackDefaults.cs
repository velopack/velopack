namespace Velopack.Vpk;

public record VelopackDefaults
{
    public bool SkipUpdates { get; }
    public bool DefaultPromptValue { get; }
    public RuntimeOs TargetOs { get; }
    public bool TelemetryEnabled { get; }

    public VelopackDefaults(bool defaultPromptValue)
        : this(defaultPromptValue, VelopackRuntimeInfo.SystemOs, true, true)
    {
    }

    public VelopackDefaults(bool defaultPromptValue, RuntimeOs targetOs, bool skipUpdates, bool telemetryEnabled = true)
    {
        SkipUpdates = skipUpdates;
        DefaultPromptValue = defaultPromptValue;
        TargetOs = targetOs;
        TelemetryEnabled = telemetryEnabled;
    }
}
