using Ico.Codecs;
using Ico.Host;
using Ico.Validation;
using SixLabors.ImageSharp.Formats.Png;
using System.Collections.Generic;

namespace Ico.Model
{
    public class ParseContext
    {
        public string FullPath { get; set; }

        public string DisplayedPath { get; set; }

        public uint? ImageDirectoryIndex { get; set; }

        public List<IcoFrame> GeneratedFrames { get; set; }

        public PngEncoder PngEncoder { get; set; }

        public StrictnessPolicy MaskedImagePixelEmitOptions { get; set; }

        public StrictnessPolicy AllowPaletteTruncation { get; set; }

        public IErrorReporter Reporter { get; set; }

        public IcoErrorCode LastEncodeError { get; set; }
    }
}
