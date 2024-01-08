using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging
{
    /// <summary>
    /// Denotes that an error has occurred for which a stack trace should not be printed.
    /// </summary>
    public class UserErrorException : Exception
    {
        public UserErrorException()
        {
        }

        public UserErrorException(string message) : base(message)
        {
        }

        public UserErrorException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UserErrorException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
