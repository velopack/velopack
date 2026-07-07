using System.Reflection;
using System.Runtime.InteropServices;

[assembly: ComVisible(false)]
// Parallelize across test collections (one collection per class by default), while every test within a
// single collection still runs serially. Tests that share a backing service are grouped into the same
// named [Collection("...")] so they never run concurrently; tests hitting different services parallelize.
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]
