using System;
using System.Runtime.Serialization;

namespace Ico.Validation
{
    public class InvalidPngFileException : Exception
    {
        public IcoErrorCode ErrorCode { get; private set; }

        public InvalidPngFileException()
        {
        }

        public InvalidPngFileException(string message) : base(message)
        {
        }

        public InvalidPngFileException(IcoErrorCode errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public InvalidPngFileException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvalidPngFileException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

    }
}
