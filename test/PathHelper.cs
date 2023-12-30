using System.Diagnostics;

public static class PathHelper
{
    public static string GetFixturesDir() 
        => Path.Combine(GetTestRoot(), "fixtures");

    public static string GetFixture(params string[] names) 
        => Path.Combine(new string[] { GetTestRoot(), "fixtures" }.Concat(names).ToArray());

    public static string GetTestRoot()
    {
        // XXX: This is an evil hack, but it's okay for a unit test
        // We can't use Assembly.Location because unit test runners love
        // to move stuff to temp directories
        var st = new StackFrame(true);
#pragma warning disable CS8604 // Possible null reference argument.
        var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName())));
#pragma warning restore CS8604 // Possible null reference argument.
        return di.FullName;
    }
}