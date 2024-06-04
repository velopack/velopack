using Ico.Validation;

namespace Ico.Host
{
    public interface IErrorReporter
    {
        void ErrorLine(IcoErrorCode errorCode, string message);

        void ErrorLine(IcoErrorCode errorCode, string message, string fileName);

        void ErrorLine(IcoErrorCode errorCode, string message, string fileName, uint frameNumber);

        void WarnLine(IcoErrorCode errorCode, string message);

        void WarnLine(IcoErrorCode errorCode, string message, string fileName);

        void WarnLine(IcoErrorCode errorCode, string message, string fileName, uint frameNumber);

        void InfoLine(string message);

        void VerboseLine(string message);
    }
}
