namespace Velopack.Vpk;

public record VelopackDefaults
{
    public bool SkipUpdates { get; }
    public bool DefaultPromptValue { get; }
    public RuntimeOs TargetOs { get; }

    public VelopackDefaults(bool defaultPromptValue)
        : this(defaultPromptValue, VelopackRuntimeInfo.SystemOs, true)
    {
    }

    public VelopackDefaults(bool defaultPromptValue, RuntimeOs targetOs, bool skipUpdates)
    {
        SkipUpdates = skipUpdates;
        DefaultPromptValue = defaultPromptValue;
        TargetOs = targetOs;
    }
}
