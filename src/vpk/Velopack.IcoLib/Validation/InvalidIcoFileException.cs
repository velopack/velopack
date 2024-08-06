using Ico.Model;
using System;
using System.Runtime.Serialization;

namespace Ico.Validation
{
    [Serializable]
    public class InvalidIcoFileException : Exception
    {
        public ParseContext Context { get; private set; }

        public IcoErrorCode ErrorCode { get; private set; }

        public InvalidIcoFileException()
        {
        }

        public InvalidIcoFileException(string message) : base(message)
        {
        }

        public InvalidIcoFileException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public InvalidIcoFileException(IcoErrorCode errorCode, string message, ParseContext context) : this(message)
        {
            ErrorCode = errorCode;
            Context = context;
        }

        protected InvalidIcoFileException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public override string ToString()
        {
            if (Context != null)
            {
                if (Context.DisplayedPath != null && Context.ImageDirectoryIndex == null)
                {
                    return base.ToString() + $"\nFile: \"{Context.DisplayedPath}\"";
                }
                else if (Context.DisplayedPath != null && Context.ImageDirectoryIndex == null)
                {
                    return base.ToString() + $"\nFile: \"{Context.DisplayedPath}\"";
                }
                else if (Context.DisplayedPath != null && Context.ImageDirectoryIndex.HasValue)
                {
                    return base.ToString() + $"\nFile: \"{Context.DisplayedPath}\"\nImage directory index: #{Context.ImageDirectoryIndex.Value}";
                }
            }
            return base.ToString();
        }
    }
}