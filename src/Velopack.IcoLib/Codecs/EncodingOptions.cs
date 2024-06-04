namespace Ico.Codecs
{
    public enum MaskedImagePixelEmitOptions
    {
        Compliant,
        PreserveSource,
        Loose,
    }

    public enum StrictnessPolicy
    {
        Compliant,
        PreserveSource,
        Loose,
    }

    public enum BestFormatPolicy
    {
        PreserveSource,
        MinimizeStorage,
        PngLargeImages,
        AlwaysPng,
        AlwaysBmp,
        Inherited,
    }
}
