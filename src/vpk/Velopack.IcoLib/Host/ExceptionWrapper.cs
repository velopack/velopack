using Ico.Model;
using Ico.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace Ico.Host
{
    public static class ExceptionWrapper
    {
        public static void Try(Action action, ParseContext context, IErrorReporter reporter)
        {
            try
            {
                action();
            }
            catch (InvalidIcoFileException e)
            {
                var frame = (e.Context?.ImageDirectoryIndex);

                if (frame != null)
                {
                    reporter.ErrorLine(e.ErrorCode, e.Message, context.DisplayedPath, context.ImageDirectoryIndex.Value);
                }
                else
                {
                    reporter.ErrorLine(e.ErrorCode, e.Message, context.DisplayedPath);
                }
            }
            catch (InvalidPngFileException e) when (e.ErrorCode != IcoErrorCode.NoError)
            {
                reporter.ErrorLine(e.ErrorCode, e.Message);
            }
            catch (Exception e)
            {
                reporter.ErrorLine(IcoErrorCode.NoError, e.ToString(), context.DisplayedPath);
            }
        }
    }
}
