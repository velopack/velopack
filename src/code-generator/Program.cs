﻿using System.Reflection;
using HandlebarsDotNet;

var scriptsDir = Assembly.GetEntryAssembly()!
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(x => x.Key == "SelfDir").Value!;

var librustDir = Path.Combine(scriptsDir, "..", "lib-rust", "src");
var libcppDir = Path.Combine(scriptsDir, "..", "lib-cpp");
var templatesDir = Path.Combine(scriptsDir, "Templates");
var files = Directory.EnumerateFiles(librustDir, "*.rs", SearchOption.AllDirectories);

string[] desiredStructs = [
    "VelopackAsset",
    "UpdateInfo",
    "UpdateOptions",
    "VelopackLocatorConfig",
];

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

Handlebars.RegisterHelper("indent", (writer, context, args) => {
    var comment = (string) context[(string) args[0]];
    var indent = (string) args[1];
    writer.WriteSafeString(comment.PrefixEveryLine(indent));
    writer.WriteSafeString("\n");
});

var types = new List<TypeMap>() {
    TypeMap.RustStruct("VelopackAsset", "vpkc_asset_t"),
    TypeMap.RustStruct("UpdateInfo", "vpkc_update_info_t"),
    TypeMap.RustStruct("UpdateOptions", "vpkc_update_options_t"),
    TypeMap.RustStruct("VelopackLocatorConfig", "vpkc_locator_config_t"),
    TypeMap.SystemType("String", "char", "string", "c_char"),
    TypeMap.SystemType("PathBuf", "char", "string", "c_char"),
    TypeMap.Primitive("bool", "bool", "boolean"),
    TypeMap.Primitive("i32", "int32_t", "number"),
    TypeMap.Primitive("i64", "int64_t", "number"),
    TypeMap.Primitive("u32", "uint32_t", "number"),
    TypeMap.Primitive("u64", "uint64_t", "number"),
}.ToDictionary(v => v.rustType, v => v);

var handlebarData = availableStructs.Select(s => {
    var fields = s.Fields.Select(f => {
        //var isString = types[f.Type].rustType == "PathBuf" || types[f.Type].rustType == "String";
        var field = new RustStruct_Field {
            rust_comment = f.DocComment.ToRustComment(),
            cpp_comment = f.DocComment.ToCppComment(),
            field_name = f.Name,
            field_optional = f.Optional,
            field_vector = f.Vec,
            field_rust_type = f.Type,
            field_c_type = types[f.Type].interopType,
            field_cpp_type = types[f.Type].cppType,
            field_node_type = types[f.Type].nodeType,
            field_system = types[f.Type].system,
            field_primitive = types[f.Type].primitive,
            field_normal = !f.Vec && !types[f.Type].primitive,
        };
        return field;
    }).ToArray();

    var opt_ordered_fields = fields
        .Where(f => !f.field_optional)
        .Concat(fields.Where(f => f.field_optional))
        .ToArray();

    var stru = new RustStruct_Struct {
        rust_comment = s.DocComment.ToRustComment(),
        cpp_comment = s.DocComment.ToCppComment(),
        struct_rust_name = s.Name,
        struct_c_name = types[s.Name].interopType,
        fields = fields,
        opt_ordered_fields = opt_ordered_fields,
    };
    return stru;
}).ToArray();

// --- rust generation ---
string rustTypes = Path.Combine(libcppDir, "src", "types.rs");
var rustCTypesTemplate = Handlebars.Compile(File.ReadAllText(Path.Combine(templatesDir, "rust_types.hbs")));
var rustCTypes = rustCTypesTemplate(handlebarData);
Util.ReplaceTextInFile(rustTypes, "RUST_TYPES", rustCTypes.ToString().ReplaceLineEndings("\n"));

// --- C++ generation ---
string rustCppInclude = Path.Combine(libcppDir, "include", "Velopack.hpp");
var cppTypesTemplate = Handlebars.Compile(File.ReadAllText(Path.Combine(templatesDir, "cpp_mapping.hbs")));
var cppTypes = cppTypesTemplate(handlebarData);
Util.ReplaceTextInFile(rustCppInclude, "CPP_TYPES", cppTypes.ToString().ReplaceLineEndings("\n"));

// --- python generation ---
string pythonAssetRs = Path.Combine(scriptsDir, "..", "lib-python", "src", "types.rs");
var pythonAssetTemplate = Handlebars.Compile(File.ReadAllText(Path.Combine(templatesDir, "python_asset.hbs")));
var pythonAsset = pythonAssetTemplate(handlebarData);
File.WriteAllText(pythonAssetRs, pythonAsset.ToString().ReplaceLineEndings("\n"));

// --- nodejs generation ---
var libnodeTypesFile = Path.Combine(scriptsDir, "..", "lib-nodejs", "src", "types.ts");
var nodeTemplate = Handlebars.Compile(File.ReadAllText(Path.Combine(templatesDir, "node_types.hbs")));
var nodeData = nodeTemplate(handlebarData);
File.WriteAllText(libnodeTypesFile, nodeData.ToString().ReplaceLineEndings("\n"));

return 0;

class TypeMap
{
    public string rustType;
    public string cType;
    public string cppType;
    public string nodeType;
    public string interopType;
    public bool primitive;
    public bool system;

    public static TypeMap RustStruct(string rustName, string cType)
    {
        return new TypeMap() {
            rustType = rustName,
            cType = cType,
            cppType = rustName,
            nodeType = rustName,
            interopType = cType,
            primitive = false,
            system = false,
        };
    }

    public static TypeMap Primitive(string rustName, string cType, string nodeType)
    {
        return new TypeMap() {
            rustType = rustName,
            cType = cType,
            cppType = cType,
            nodeType = nodeType,
            interopType = rustName,
            primitive = true,
            system = false,
        };
    }

    public static TypeMap SystemType(string rustName, string cType, string cppType, string interopType)
    {
        return new TypeMap() {
            rustType = rustName,
            cType = cType,
            cppType = cppType,
            nodeType = cppType,
            interopType = interopType,
            primitive = false,
            system = true,
        };
    }
}

class RustStruct_Struct
{
    public string struct_c_name;
    public string struct_rust_name;
    public string rust_comment;
    public string cpp_comment;
    public RustStruct_Field[] fields;
    public RustStruct_Field[] opt_ordered_fields;
}

class RustStruct_Field
{
    public string field_name;
    public string field_c_type;
    public string field_cpp_type;
    public string field_rust_type;
    public string field_node_type;
    public bool field_primitive;
    public bool field_optional;
    public bool field_vector;
    public bool field_system;
    public bool field_normal;
    public bool field_primitive_or_system => field_primitive || field_system;
    public string rust_comment;
    public string cpp_comment;
}