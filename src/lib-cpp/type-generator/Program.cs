using System.Reflection;

var scriptsDir = Assembly.GetEntryAssembly()!
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(x => x.Key == "SelfDir").Value!;

var librustDir = Path.Combine(scriptsDir, "..", "..", "lib-rust", "src");
var libcppDir = Path.Combine(scriptsDir, "..");
var files = Directory.EnumerateFiles(librustDir, "*.rs", SearchOption.AllDirectories);

string[] desiredStructs = [
    "VelopackAsset",
    "UpdateInfo",
    "UpdateOptions",
    "VelopackLocatorConfig",
];

Dictionary<string, string> basic_libc_names = new() {
    { "VelopackAsset", "vpkc_asset_t" },
    { "UpdateInfo", "vpkc_update_info_t" },
    { "UpdateOptions", "vpkc_update_options_t" },
    { "VelopackLocatorConfig", "vpkc_locator_config_t" },
};

List<RustStruct> availableStructs = new();
string[] searchStrings = desiredStructs.Select(s => "struct " + s + " {").ToArray();

foreach (var file in files) {
    Console.WriteLine(file);
    var text = File.ReadAllText(file);

    var structs = StructFinder.FindStructs(text);

    foreach (var s in structs) {
        if (searchStrings.Any(search => s.Contains(search))) {
            var result = StructParser.ParseStructs(s);
            availableStructs.AddRange(result);
        }
    }
}

if (desiredStructs.Length != availableStructs.Count) {
    Console.WriteLine("Not all structs were found.");
    Console.WriteLine("Desired structs: " + string.Join(", ", desiredStructs));
    Console.WriteLine("Available structs: " + string.Join(", ", availableStructs.Select(s => s.Name)));
    return -1;
}

// rust bridge code
string rustCppLib = Path.Combine(libcppDir, "src", "lib.rs");
string rustCppMap = Path.Combine(libcppDir, "src", "map.rs");
string rustCppInclude = Path.Combine(libcppDir, "include", "Velopack.h");
string rustBridgeC = Path.Combine(libcppDir, "src", "bridge.cc");

Console.WriteLine("Generating bridge dtos");
var sbBridgeDto = new IndentStringBuilder();
foreach(var rs in availableStructs) {
    Templates.WriteBridgeDto(desiredStructs, sbBridgeDto, rs);
}

Console.WriteLine("Generating bridge to core mappings");
var sbBridgeMapping = new IndentStringBuilder();
foreach(var rs in availableStructs) {
    Templates.WriteBridgeToCoreMapping(desiredStructs, sbBridgeMapping, rs);
}

Console.WriteLine("Generating C types");
var cTypes = new IndentStringBuilder();
foreach(var rs in availableStructs) {
    Templates.WriteBasicC(basic_libc_names, cTypes, rs);
}

Console.WriteLine("Generating C++ types");
var cppTypes = new IndentStringBuilder();
foreach(var rs in availableStructs) {
    Templates.WriteCPlusPlus(basic_libc_names, cppTypes, rs);
}

Console.WriteLine("Generating C to bridge mappings");
var cToBridgeMapping = new IndentStringBuilder();
foreach(var rs in availableStructs) {
    Templates.WriteCBridgeMapping(basic_libc_names, cToBridgeMapping, rs);
}

Console.WriteLine("Writing all to file");
Util.ReplaceTextInFile(rustCppLib, "BRIDGE_DTOS", sbBridgeDto.ToString());
Util.ReplaceTextInFile(rustCppMap, "CORE_MAPPING", sbBridgeMapping.ToString());
Util.ReplaceTextInFile(rustCppInclude, "C_TYPES", cTypes.ToString());
Util.ReplaceTextInFile(rustCppInclude, "CPP_TYPES", cppTypes.ToString());
Util.ReplaceTextInFile(rustBridgeC, "BRIDGE_MAPPING", cToBridgeMapping.ToString());

return 0;