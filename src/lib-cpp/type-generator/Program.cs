using System.Reflection;
using HandlebarsDotNet;

var scriptsDir = Assembly.GetEntryAssembly()!
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(x => x.Key == "SelfDir").Value!;

var librustDir = Path.Combine(scriptsDir, "..", "..", "lib-rust", "src");
var libcppDir = Path.Combine(scriptsDir, "..");
var templatesDir = Path.Combine(scriptsDir, "Templates");
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
// string rustCppLib = Path.Combine(libcppDir, "src", "lib.rs");
string rustTypes = Path.Combine(libcppDir, "src", "types.rs");
//string rustCppMap = Path.Combine(libcppDir, "src", "map.rs");
string rustCppInclude = Path.Combine(libcppDir, "include", "Velopack.hpp");
//string rustBridgeC = Path.Combine(libcppDir, "src", "bridge.cc");

//Console.WriteLine("Generating bridge dtos");
//var sbBridgeDto = new IndentStringBuilder();
//foreach(var rs in availableStructs) {
//    Templates.WriteBridgeDto(desiredStructs, sbBridgeDto, rs);
//}

//Console.WriteLine("Generating bridge to core mappings");
//var sbBridgeMapping = new IndentStringBuilder();
//foreach(var rs in availableStructs) {
//    Templates.WriteBridgeToCoreMapping(desiredStructs, sbBridgeMapping, rs);
//}

// Console.WriteLine("Generating C types");
// var cTypes = new IndentStringBuilder();
// cTypes.AppendLine();
// foreach (var rs in availableStructs) {
//     Templates.WriteBasicC(basic_libc_names, cTypes, rs);
// }

Console.WriteLine("Generating C++ types");
var cppTypes = new IndentStringBuilder();
cppTypes.AppendLine();
foreach (var rs in availableStructs) {
    Templates.WriteCPlusPlus(basic_libc_names, cppTypes, rs);
}
foreach (var rs in availableStructs) {
    Templates.WriteC2CPPMapping(basic_libc_names, cppTypes, rs);
}

Console.WriteLine("Generating Rust-C types");
//var rustCTypes = new IndentStringBuilder();
//foreach (var rs in availableStructs) {
//    Templates.WriteRustCRepr(basic_libc_names, rustCTypes, rs);
//}
var rustCTypesTemplate = Handlebars.Compile(File.ReadAllText(Path.Combine(templatesDir, "rust_struct.hbs")));

var types = new List<TypeMap>() {
    new TypeMap("VelopackAsset", "vpkc_asset_t", "vpkc_asset_t", false),
    new TypeMap("UpdateInfo", "vpkc_update_info_t", "vpkc_update_info_t", false),
    new TypeMap("UpdateOptions", "vpkc_update_options_t", "vpkc_update_options_t", false),
    new TypeMap("VelopackLocatorConfig", "vpkc_locator_config_t", "vpkc_locator_config_t", false),
    new TypeMap("String", "char", "c_char", false),
    new TypeMap("PathBuf", "char", "c_char", false),
    new TypeMap("bool", "bool", "bool", true),
    new TypeMap("i32", "int32_t", "i32", true),
    new TypeMap("i64", "int64_t", "i64", true),
    new TypeMap("u32", "uint32_t", "u32", true),
    new TypeMap("u64", "uint64_t", "u64", true),
}.ToDictionary(v => v.rustType, v => v);

var data = availableStructs.Select(s => new RustStruct_Struct {
    rust_comment = s.DocComment.PrefixEveryLine("/// "),
    struct_rust_name = s.Name,
    struct_c_name = types[s.Name].interopType,
    fields = s.Fields.Select(f => {
        var isString = types[f.Type].rustType == "PathBuf" || types[f.Type].rustType == "String";
        var field = new RustStruct_Field {
            rust_comment = f.DocComment.PrefixEveryLine("/// "),
            field_name = f.Name,
            field_optional = f.Optional,
            field_vector = f.Vec,
            //field_add_pointer = f.Optional && !f.Vec && !isString,
            //field_requires_ref = !isString && !f.Vec && !f.Optional,
            //field_string = isString,
            field_rust_type = f.Type,
            field_c_type = types[f.Type].interopType,
            field_primitive = types[f.Type].primitive,
            field_normal = !f.Vec && !types[f.Type].primitive,
        };
        return field;
    }).ToArray(),
}).ToArray();

var rustCTypes = rustCTypesTemplate(data);

Console.WriteLine();
//Console.WriteLine("Generating C to bridge mappings");
//var cToBridgeMapping = new IndentStringBuilder();
//foreach (var rs in availableStructs) {
//    Templates.WriteCBridgeMapping(basic_libc_names, cToBridgeMapping, rs);
//}

Console.WriteLine("Writing all to file");
//Util.ReplaceTextInFile(rustCppLib, "BRIDGE_DTOS", sbBridgeDto.ToString());
//Util.ReplaceTextInFile(rustCppMap, "CORE_MAPPING", sbBridgeMapping.ToString());
Util.ReplaceTextInFile(rustTypes, "RUST_TYPES", rustCTypes.ToString());
// Util.ReplaceTextInFile(rustCppInclude, "C_TYPES", cTypes.ToString());
Util.ReplaceTextInFile(rustCppInclude, "CPP_TYPES", cppTypes.ToString());
//Util.ReplaceTextInFile(rustBridgeC, "BRIDGE_MAPPING", cToBridgeMapping.ToString());

return 0;

record struct TypeMap(string rustType, string cType, string interopType, bool primitive);

class RustStruct_Struct
{
    public string struct_c_name;
    public string struct_rust_name;
    public string rust_comment;
    public RustStruct_Field[] fields;
}

class RustStruct_Field
{
    public string field_name;
    public string field_c_type;
    public string field_rust_type;
    public bool field_primitive;
    public bool field_optional;
    public bool field_vector;
    //public bool field_prefix;
    //public bool field_add_pointer;
    //public bool field_requires_ref;
    public bool field_normal;
    //public bool field_string;
    public string rust_comment;
}