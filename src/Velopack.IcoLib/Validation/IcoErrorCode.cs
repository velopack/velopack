using System;
using System.Collections.Generic;
using System.Text;

namespace Ico.Validation
{
    public enum IcoErrorCode
    {
        NoError = 0,

        // 100 - 199: invalid ICO file format
        InvalidIcoHeader_idReserved = 100,
        InvalidIconHeader_idType = 101,
        InvalidFrameHeader_bReserved = 110,
        InvalidFrameHeader_wPlanes = 111,
        InvalidFrameHeader_dwBytesInRes = 112,
        InvalidFrameHeader_dwImageOffset = 113,
        TooManyFrames = 120,
        InvalidBitapInfoHeader_ciSize = 130,
        InvalidBitapInfoHeader_biXPelsPerMeter = 131,
        InvalidBitapInfoHeader_biYPelsPerMeter = 132,
        InvalidBitapInfoHeader_biBitCount = 133,
        InvalidBitapInfoHeader_biClrUsed = 134,

        // 200 - 299: nonstandard or nonportable ICO file format
        ZeroFrames = 200,
        DuplicateFrameTypes = 201,
        MismatchedHeight = 210,
        MismatchedWidth = 211,
        NonzeroAlpha = 220,
        MaskedPixelWithColor = 221,
        NoMaskedPixels = 222,
        IndexedColorOutOfBounds = 230,
        UndersizedColorTable = 231,
        PngNot32Bit = 240,
        PngNotRGBA32 = 241,
        NotSquare = 256,

        // 300 - 399: tool limitations
        FileTooLarge = 300,
        BitfieldCompressionNotSupported = 310,
        BitmapCompressionNotSupported = 311,

        // 400 - 499: usage or environmental error
        FileExists = 403,
        FileNotFound = 404,
        UnsupportedCodec = 410,
        UnsupportedBitmapEncoding = 411,
        OnlySupportedOnBitmaps = 412,
        BitmapMaskWrongDimensions = 420,
        BitampMaskWrongColors = 421,
        InvalidFrameIndex = 430,
        TooManyColorsForBitDepth = 431,
        NotPng = 440,
        PngBadIHDR = 441,
        PngIllegalInputDimensions = 445,
        PngIllegalInputDepth = 446,
        PngIllegalColorType = 447,
    }
}
