public static class Templates
{
    private static string GetBasicCType(Dictionary<string, string> nameMap, string rustType)
    {
        switch (rustType) {
        case "PathBuf":
        case "String":
            return "char*";
        case "bool":
            return "bool";
        case "i32":
            return "int64_t";
        case "i64":
            return "int64_t";
        case "u32":
            return "uint32_t";
        case "u64":
            return "uint64_t";
        default:
            if (nameMap.TryGetValue(rustType, out var type)) {
                return type;
            }

            throw new NotSupportedException("Unsupported type for basic-c: " + rustType);
        }
    }

    private static string GetCPlusPlusType(string[] coreTypes, string rustType, bool optional)
    {
        string type = rustType switch {
            "PathBuf" => "std::string",
            "String" => "std::string",
            "bool" => "bool",
            "i32" => "int64_t",
            "i64" => "int64_t",
            "u32" => "uint32_t",
            "u64" => "uint64_t",
            _ => coreTypes.Contains(rustType) ? rustType : throw new NotSupportedException("Unsupported type for c-plus-plus: " + rustType),
        };

        return optional ? "std::optional<" + type + ">" : type;
    }

    public static void WriteCBridgeMapping(Dictionary<string, string> nameMap, IndentStringBuilder sb, RustStruct rs)
    {
        var cName = nameMap[rs.Name];

        sb.AppendLine($"static inline {rs.Name}Dto to_bridge({cName}* pDto) {{");
        using (sb.Indent()) {
            sb.AppendLine($"if (pDto == nullptr) {{ return {{}}; }}");
            sb.AppendLine($"return {{");
            using (sb.Indent()) {
                foreach (var field in rs.Fields) {
                    string suffix = field.Optional ? "_opt" : "";
                    string type = field.Type == "PathBuf" ? "string" : field.Type.ToLower();
                    if (nameMap.ContainsKey(field.Type)) {
                        sb.AppendLine($"to_bridge{suffix}(&pDto->{field.Name}),");
                    } else if (type == "string") {
                        sb.AppendLine($"to_bridge{type}{suffix}(pDto->{field.Name}),");
                    } else {
                        sb.AppendLine($"pDto->{field.Name},");
                    }
                }
            }

            sb.AppendLine("};");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"static inline {rs.Name}DtoOption to_bridge_opt({cName}* pDto) {{");
        using (sb.Indent()) {
            sb.AppendLine($"{rs.Name}DtoOption opt;");
            sb.AppendLine($"if (pDto == nullptr) {{");
            using (sb.Indent()) {
                sb.AppendLine($"opt.has_data = false;");
                sb.AppendLine($"return opt;");
            }

            sb.AppendLine($"}}");
            sb.AppendLine();
            sb.AppendLine($"opt.has_data = true;");
            sb.AppendLine($"opt.data = to_bridge(pDto);");
            sb.AppendLine($"return opt;");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"static inline void allocate_{rs.Name.ToLower()}({rs.Name}Dto bridgeDto, {cName}* pDto) {{");
        using (sb.Indent()) {
            sb.AppendLine($"if (pDto == nullptr) {{ return; }}");
            foreach (var field in rs.Fields) {
                string type = field.Type == "PathBuf" ? "string" : field.Type.ToLower();
                string suffix = field.Optional ? "_opt" : "";
                sb.AppendLine(
                    nameMap.ContainsKey(field.Type) || type == "string"
                        ? $"allocate_{type}{suffix}(bridgeDto.{field.Name}, &pDto->{field.Name});"
                        : $"pDto->{field.Name} = bridgeDto.{field.Name};");
            }
        }
        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"static inline void free_{rs.Name.ToLower()}({cName}* pDto) {{");
        using (sb.Indent()) {
            sb.AppendLine($"if (pDto == nullptr) {{ return; }}");
            foreach (var field in rs.Fields) {
                string type = field.Type == "PathBuf" ? "string" : field.Type.ToLower();
                if (nameMap.ContainsKey(field.Type)) {
                    sb.AppendLine($"free_{type}(&pDto->{field.Name});");
                } else if (type == "string") {
                    sb.AppendLine($"free(pDto->{field.Name});");
                }
            }
        }
        sb.AppendLine($"}}");
        sb.AppendLine();
    }

    public static void WriteBasicC(Dictionary<string, string> nameMap, IndentStringBuilder sb, RustStruct rs)
    {
        sb.AppendDocComment(rs.DocComment);
        sb.AppendLine($"typedef struct {nameMap[rs.Name]} {{");
        foreach (var field in rs.Fields) {
            using (sb.Indent()) {
                sb.AppendDocComment(field.DocComment);
                sb.AppendLine($"{GetBasicCType(nameMap, field.Type)} {field.Name};");
            }
        }

        sb.AppendLine($"}} {nameMap[rs.Name]};");
        sb.AppendLine();
    }

    public static void WriteC2CPPMapping(Dictionary<string, string> nameMap, IndentStringBuilder sb, RustStruct rs)
    {
        sb.AppendLine($"static inline {nameMap[rs.Name]} to_c(const {rs.Name}& dto) {{");
        using (sb.Indent()) {
            sb.AppendLine("return {");
            using (sb.Indent()) {
                foreach (var field in rs.Fields) {
                    string suffix = field.Optional ? "_opt" : "";
                    string type = field.Type == "PathBuf" ? "string" : field.Type.ToLower();
                    sb.AppendLine(
                        nameMap.ContainsKey(field.Type)
                            ? $"to_c{suffix}(dto.{field.Name}),"
                            : $"to_c{type}{suffix}(dto.{field.Name}),");
                }
            }

            sb.AppendLine("};");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"static inline {rs.Name} to_cpp(const {nameMap[rs.Name]}& dto) {{");
        using (sb.Indent()) {
            sb.AppendLine("return {");
            using (sb.Indent()) {
                foreach (var field in rs.Fields) {
                    string suffix = field.Optional ? "_opt" : "";
                    string type = field.Type == "PathBuf" ? "string" : field.Type.ToLower();
                    sb.AppendLine(
                        nameMap.ContainsKey(field.Type)
                            ? $"to_cpp{suffix}(dto.{field.Name}),"
                            : $"to_cpp{type}{suffix}(dto.{field.Name}),");
                }
            }

            sb.AppendLine("};");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();
    }

    public static void WriteCPlusPlus(Dictionary<string, string> nameMap, IndentStringBuilder sb, RustStruct rs)
    {
        var coreTypes = nameMap.Keys.ToArray();
        sb.AppendDocComment(rs.DocComment);
        sb.AppendLine($"struct {rs.Name} {{");
        foreach (var field in rs.Fields) {
            using (sb.Indent()) {
                sb.AppendDocComment(field.DocComment);
                sb.AppendLine($"{GetCPlusPlusType(coreTypes, field.Type, field.Optional)} {field.Name};");
            }
        }

        sb.AppendLine($"}};");
        sb.AppendLine();
    }

    public static void WriteBridgeDto(string[] coreTypes, IndentStringBuilder sb, RustStruct rs)
    {
        Func<string, string> nameMapper = (str) =>
            coreTypes.Contains(str) ? str + "Dto" : str;

        using (sb.Indent()) {
            sb.AppendLine($"#[derive(Default)]");
            sb.AppendLine($"pub struct {nameMapper(rs.Name)} {{");
            foreach (var field in rs.Fields) {
                string type = field.Type;
                if (type == "PathBuf") {
                    type = "String";
                }

                using (sb.Indent()) {
                    if (field.Optional) {
                        sb.AppendLine($"pub {field.Name}: {nameMapper(type)}Option,");
                    } else {
                        sb.AppendLine($"pub {field.Name}: {nameMapper(type)},");
                    }
                }
            }

            sb.AppendLine($"}}");
            sb.AppendLine();

            sb.AppendLine($"#[derive(Default)]");
            sb.AppendLine($"pub struct {nameMapper(rs.Name)}Option {{");
            using (sb.Indent()) {
                sb.AppendLine($"pub data: {nameMapper(rs.Name)},");
                sb.AppendLine($"pub has_data: bool,");
            }

            sb.AppendLine($"}}");
            sb.AppendLine();
        }
    }

    public static void WriteBridgeToCoreMapping(string[] coreTypes, IndentStringBuilder sb, RustStruct rs)
    {
        Func<string, string> nameMapper = (str) => coreTypes.Contains(str) ? str + "Dto" : str;

        sb.AppendLine($"pub fn {rs.Name.ToLower()}_to_core(dto: &{nameMapper(rs.Name)}) -> {rs.Name} {{");
        ;
        using (sb.Indent()) {
            sb.AppendLine($"{rs.Name} {{");
            foreach (var field in rs.Fields) {
                using (sb.Indent()) {
                    if (field.Optional) {
                        sb.AppendLine(
                            $"{field.Name}: if dto.{field.Name}.has_data {{ Some({field.Type.ToLower()}_to_core(&dto.{field.Name}.data)) }} else {{ None }},");
                    } else {
                        sb.AppendLine($"{field.Name}: {field.Type.ToLower()}_to_core(&dto.{field.Name}),");
                    }
                }
            }

            sb.AppendLine($"}}");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"pub fn {rs.Name.ToLower()}_to_bridge(dto: &{rs.Name}) -> {nameMapper(rs.Name)} {{");
        using (sb.Indent()) {
            sb.AppendLine($"{nameMapper(rs.Name)} {{");
            foreach (var field in rs.Fields) {
                using (sb.Indent()) {
                    if (field.Optional) {
                        sb.AppendLine(
                            $"{field.Name}: {nameMapper(field.Type)}Option {{ data: {field.Type.ToLower()}_to_bridge(&dto.{field.Name}.clone().unwrap_or_default()), has_data: dto.{field.Name}.is_some() }},");
                    } else {
                        sb.AppendLine($"{field.Name}: {field.Type.ToLower()}_to_bridge(&dto.{field.Name}),");
                    }
                }
            }

            sb.AppendLine($"}}");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"pub fn {rs.Name.ToLower()}_to_core_option(dto: &{nameMapper(rs.Name)}Option) -> Option<{rs.Name}> {{");
        ;
        using (sb.Indent()) {
            sb.AppendLine($"if dto.has_data {{ Some({rs.Name.ToLower()}_to_core(&dto.data)) }} else {{ None }}");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();

        sb.AppendLine($"pub fn {rs.Name.ToLower()}_to_bridge_option(dto: &Option<{rs.Name}>) -> {nameMapper(rs.Name)}Option {{");
        ;
        using (sb.Indent()) {
            sb.AppendLine($"match dto {{");
            using (sb.Indent()) {
                sb.AppendLine($"Some(dto) => {nameMapper(rs.Name)}Option {{ data: {rs.Name.ToLower()}_to_bridge(dto), has_data: true }},");
                sb.AppendLine($"None => {nameMapper(rs.Name)}Option {{ data: Default::default(), has_data: false }},");
            }

            sb.AppendLine($"}}");
        }

        sb.AppendLine($"}}");
        sb.AppendLine();
    }
}