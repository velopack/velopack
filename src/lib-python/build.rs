use std::env;
use std::fs;
use std::path::Path;

fn main() {

    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    
    // Get the workspace version
    let version = get_workspace_version().unwrap_or_else(|| {
        env::var("CARGO_PKG_VERSION").unwrap_or_else(|_| "0.1.0".to_string())
    });
    
    let python_version = convert_to_python_version(&version);
    
    // Set environment variables for PyO3
    println!("cargo:rustc-env=PYTHON_VERSION={}", python_version);
    
    // Try setting the package version for PyO3 to pick up
    println!("cargo:metadata=version={}", python_version);
    
    // Also set it as a cfg value
    println!("cargo:rustc-cfg=version=\"{}\"", python_version);
    
    println!("cargo:rerun-if-changed=../../Cargo.toml");
}

fn get_workspace_version() -> Option<String> {
    // Navigate up to workspace root and read Cargo.toml
    let manifest_dir = env::var("CARGO_MANIFEST_DIR").ok()?;
    let workspace_toml = Path::new(&manifest_dir)
        .parent()?  // src
        .parent()?  // velopack root
        .join("Cargo.toml");
    
    if !workspace_toml.exists() {
        return None;
    }
    
    let content = fs::read_to_string(&workspace_toml).ok()?;
    
    // Simple parsing to extract version from [workspace.package] section
    let mut in_workspace_package = false;
    for (_line_num, line) in content.lines().enumerate() {
        let trimmed = line.trim();
        
        if trimmed == "[workspace.package]" {
            in_workspace_package = true;
            continue;
        }
        
        if trimmed.starts_with('[') && trimmed != "[workspace.package]" {
            if in_workspace_package {
            }
            in_workspace_package = false;
            continue;
        }
        
        if in_workspace_package && trimmed.starts_with("version") {
            if let Some(equals_pos) = trimmed.find('=') {
                let version_part = &trimmed[equals_pos + 1..].trim();
                // Remove quotes
                let version = version_part.trim_matches('"').trim_matches('\'');
                return Some(version.to_string());
            }
        }
    }
    
    None
}

fn convert_to_python_version(rust_version: &str) -> String {
    // Handle git-based versions like "0.0.1213-g57cf68d" - drop git ref, keep base
    if let Some(git_pos) = rust_version.find("-g") {
        let base = &rust_version[..git_pos];
        return ensure_xyz_format(base);
    }
    
    // Handle local development versions like "0.0.0-local" - drop local suffix
    if rust_version.ends_with("-local") {
        let base = rust_version.trim_end_matches("-local");
        return ensure_xyz_format(base);
    }
    
    // Handle Rust pre-release patterns and convert to Python equivalents
    if rust_version.contains("-alpha") {
        let base = rust_version.split("-alpha").next().unwrap();
        let alpha_num = extract_prerelease_number(rust_version, "-alpha");
        return format!("{}a{}", ensure_xyz_format(base), alpha_num);
    }
    
    if rust_version.contains("-beta") {
        let base = rust_version.split("-beta").next().unwrap();
        let beta_num = extract_prerelease_number(rust_version, "-beta");
        return format!("{}b{}", ensure_xyz_format(base), beta_num);
    }
    
    if rust_version.contains("-rc") {
        let base = rust_version.split("-rc").next().unwrap();
        let rc_num = extract_prerelease_number(rust_version, "-rc");
        return format!("{}rc{}", ensure_xyz_format(base), rc_num);
    }
    
    // For any other dash-separated version, just take the base
    if rust_version.contains('-') {
        let base = rust_version.split('-').next().unwrap();
        return ensure_xyz_format(base);
    }
    
    ensure_xyz_format(rust_version)
}

fn extract_prerelease_number(version: &str, pattern: &str) -> String {
    if let Some(pos) = version.find(pattern) {
        let after_pattern = &version[pos + pattern.len()..];
        if after_pattern.starts_with('.') {
            after_pattern.trim_start_matches('.').split('-').next().unwrap_or("0").to_string()
        } else if after_pattern.is_empty() {
            "0".to_string()
        } else {
            after_pattern.split('-').next().unwrap_or("0").to_string()
        }
    } else {
        "0".to_string()
    }
}

fn ensure_xyz_format(version: &str) -> String {
    let parts: Vec<&str> = version.split('.').collect();
    let result = match parts.len() {
        1 => format!("{}.0.0", parts[0]),
        2 => format!("{}.{}.0", parts[0], parts[1]),
        _ => format!("{}.{}.{}", parts[0], parts[1], parts[2]),
    };
    result
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_version_conversion() {
        // Git versions - drop git ref
        assert_eq!(convert_to_python_version("0.0.1213-g57cf68d"), "0.0.1213");
        assert_eq!(convert_to_python_version("1.2-g57cf68d"), "1.2.0");
        
        // Local versions - drop local suffix
        assert_eq!(convert_to_python_version("0.0.0-local"), "0.0.0");
        assert_eq!(convert_to_python_version("1.2.3-local"), "1.2.3");
        
        // Pre-release versions - convert to Python format
        assert_eq!(convert_to_python_version("1.0.0-alpha.1"), "1.0.0a1");
        assert_eq!(convert_to_python_version("1.0.0-alpha"), "1.0.0a0");
        assert_eq!(convert_to_python_version("1.0.0-beta.2"), "1.0.0b2");
        assert_eq!(convert_to_python_version("1.0.0-rc.1"), "1.0.0rc1");
        
        // Standard versions - ensure x.y.z format
        assert_eq!(convert_to_python_version("1.0.0"), "1.0.0");
        assert_eq!(convert_to_python_version("1.2"), "1.2.0");
        assert_eq!(convert_to_python_version("1"), "1.0.0");
        
        // Other dash-separated versions - take base only
        assert_eq!(convert_to_python_version("1.0.0-something-else"), "1.0.0");
    }
}