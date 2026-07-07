using System.Reflection;
using System.Runtime.InteropServices;

[assembly: ComVisible(false)]
// Parallelize across test collections (one collection per class by default), while every test within
// a single collection still runs serially. This was previously fully serialized because packing
// recompiled the shared TestApp project; TestApp is now published once per process into a cache
// (Velopack.TestCommon.TestApp) and tests that run `dotnet publish` themselves hold
// TestApp.WithPublishLock, so classes can safely run concurrently.
[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]