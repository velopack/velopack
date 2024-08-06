using System.Diagnostics.CodeAnalysis;
using Ico.Codecs;
using Ico.Host;
using Ico.Model;
using Ico.Validation;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Windows;

public class IcoExtract
{
    private readonly ILogger _logger;
    private readonly List<IcoFrame> _frames = new();

    public IcoExtract(ILogger logger)
    {
        _logger = logger;
    }

    public List<IcoFrame> ExtractFrames(FileInfo file)
    {
        var reporter = new IcoILoggerReporter(_logger);
        var context = new ParseContext {
            DisplayedPath = file.Name,
            FullPath = file.FullName,
            GeneratedFrames = new List<IcoFrame>(),
            Reporter = reporter,
            PngEncoder = new SixLabors.ImageSharp.Formats.Png.PngEncoder {
                CompressionLevel = SixLabors.ImageSharp.Formats.Png.PngCompressionLevel.Level9,
                ColorType = SixLabors.ImageSharp.Formats.Png.PngColorType.RgbWithAlpha,
            },
            AllowPaletteTruncation = StrictnessPolicy.Loose,
            MaskedImagePixelEmitOptions = StrictnessPolicy.Loose,
        };

        _frames.Clear();
        ExceptionWrapper.Try(() => DoExtractFile(context), context, reporter);
        return _frames;
    }

    private void DoExtractFile(ParseContext context)
    {
        var length = new FileInfo(context.FullPath).Length;
        var data = File.ReadAllBytes(context.FullPath);
        IcoDecoder.DoFile(data, context, _frames.Add);
    }

    #region IErrorReporter

    [ExcludeFromCodeCoverage]
    private class IcoILoggerReporter : IErrorReporter
    {
        private readonly ILogger _logger;

        public IcoILoggerReporter(ILogger logger)
        {
            _logger = logger;
        }

        void IErrorReporter.ErrorLine(IcoErrorCode errorCode, string message)
        {
            _logger.LogDebug($"Error{GenerateCode(errorCode)}: {message}");
        }

        void IErrorReporter.ErrorLine(IcoErrorCode errorCode, string message, string fileName)
        {
            _logger.LogDebug($"{fileName}: Error{GenerateCode(errorCode)}: {message}");
        }

        void IErrorReporter.ErrorLine(IcoErrorCode errorCode, string message, string fileName, uint frameNumber)
        {
            _logger.LogDebug($"{fileName}({frameNumber + 1}): Error{GenerateCode(errorCode)}: {message}");
        }

        void IErrorReporter.InfoLine(string message)
        {
            _logger.LogDebug(message);
        }

        void IErrorReporter.VerboseLine(string message)
        {
            _logger.LogDebug(message);
        }

        void IErrorReporter.WarnLine(IcoErrorCode errorCode, string message)
        {
            _logger.LogDebug($"Warning{GenerateCode(errorCode)}: {message}");
        }

        void IErrorReporter.WarnLine(IcoErrorCode errorCode, string message, string fileName)
        {
            _logger.LogDebug($"{fileName}: Warning{GenerateCode(errorCode)}: {message}");
        }

        void IErrorReporter.WarnLine(IcoErrorCode errorCode, string message, string fileName, uint frameNumber)
        {
            _logger.LogDebug($"{fileName}({frameNumber + 1}): Warning{GenerateCode(errorCode)}: {message}");
        }

        private string GenerateCode(IcoErrorCode code)
        {
            if (code == IcoErrorCode.NoError) {
                return "";
            } else {
                return $" ICO{(uint) code}";
            }
        }
    }

    #endregion
}
