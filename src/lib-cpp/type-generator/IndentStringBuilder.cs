using System.Text;

public class IndentStringBuilder
{
    private StringBuilder _sb = new();
    int _indent = 0;

    public void AppendLine()
    {
        _sb.AppendLine();
    }

    public void AppendLine(string text)
    {
        AppendIndent();
        _sb.AppendLine(text);
    }
    
    public void AppendDocComment(string comment)
    {
        if (comment != null) {
            foreach (var line in comment.ReplaceLineEndings("\n").Split('\n')) {
                AppendLine($"/// {line}");
            }
        }
    }

    private void AppendIndent()
    {
        _sb.Append(' ', _indent * 4);
    }

    public IDisposable Indent()
    {
        _indent++;
        return new IndentDisposable(this);
    }

    private void RemoveIndent()
    {
        _indent--;
    }

    public override string ToString()
    {
        return _sb.ToString();
    }

    private class IndentDisposable(IndentStringBuilder isb) : IDisposable
    {
        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed) {
                isb.RemoveIndent();
                _disposed = true;
            }
        }
    }
}