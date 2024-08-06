namespace Velopack.Vpk;

public record VelopackDefaults
{
    public bool DefaultPromptValue { get; }
    public RuntimeOs TargetOs { get; }

    public VelopackDefaults(bool defaultPromptValue)
        : this(defaultPromptValue, VelopackRuntimeInfo.SystemOs)
    {
    }

    public VelopackDefaults(bool defaultPromptValue, RuntimeOs targetOs)
    {
        DefaultPromptValue = defaultPromptValue;
        TargetOs = targetOs;
    }
}
