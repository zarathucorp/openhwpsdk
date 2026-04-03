using System;
using System.Runtime.InteropServices;

namespace OpenHwp.Automation
{
    public sealed class HwpAutomationException : InvalidOperationException
    {
        public HwpAutomationException(string message)
            : base(message)
        {
        }

        public HwpAutomationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public int? ComErrorCode
        {
            get
            {
                var comException = InnerException as COMException;
                return comException == null ? (int?)null : comException.ErrorCode;
            }
        }
    }
}
