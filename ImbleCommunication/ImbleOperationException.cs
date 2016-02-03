using System;
using System.Runtime.Serialization;

namespace ImbleCommunication
{
    public class ImbleOperationException : Exception
    {
        public ImbleOperationException()
        {
        }

        public ImbleOperationException(string message) : base(message)
        {
        }

        public ImbleOperationException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}