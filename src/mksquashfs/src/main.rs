use std::fs::File;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::ExitCode;

use backhand::compression::Compressor;
use backhand::{FilesystemCompressor, FilesystemWriter, NodeHeader};
use clap::Parser;
use walkdir::WalkDir;

#[derive(Parser)]
#[command(name = "mksquashfs", version, about = "Create squashfs filesystem from directory")]
struct Args {
    /// Source directory to pack
    input_dir: PathBuf,

    /// Output squashfs file path
    output_file: PathBuf,

    /// Compression algorithm
    #[arg(short = 'c', long = "comp", default_value = "gzip")]
    compression: String,

    /// Block size in bytes
    #[arg(short = 'b', long = "block-size", default_value = "131072")]
    block_size: u32,
}

fn is_elf(path: &Path) -> bool {
    let Ok(mut file) = File::open(path) else {
        return false;
    };
    let mut magic = [0u8; 4];
    if file.read_exact(&mut magic).is_err() {
        return false;
    }
    magic == [0x7f, b'E', b'L', b'F']
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let args = Args::parse();

    let compressor = match args.compression.as_str() {
        "gzip" => Compressor::Gzip,
        "xz" => Compressor::Xz,
        "zstd" => Compressor::Zstd,
        other => return Err(format!("unsupported compression: {other}").into()),
    };

    let mut fs = FilesystemWriter::default();
    fs.set_time(0);
    fs.set_only_root_id();
    fs.set_root_mode(0o755);
    fs.set_block_size(args.block_size);
    fs.set_compressor(FilesystemCompressor::new(compressor, None)?);
    fs.set_no_padding();

    let input_dir = args.input_dir.canonicalize()?;

    for entry in WalkDir::new(&input_dir).sort_by_file_name() {
        let entry = entry?;
        let full_path = entry.path();

        if full_path == input_dir {
            continue;
        }

        let rel_path = full_path.strip_prefix(&input_dir)?;
        let squashfs_path = rel_path.to_string_lossy().replace('\\', "/");

        let file_type = entry.file_type();

        if file_type.is_dir() {
            let header = NodeHeader::new(0o755, 0, 0, 0);
            fs.push_dir(&squashfs_path, header)?;
        } else if file_type.is_symlink() {
            let link_target = std::fs::read_link(full_path)?;
            let link_str = link_target.to_string_lossy().replace('\\', "/");
            let header = NodeHeader::new(0o777, 0, 0, 0);
            fs.push_symlink(link_str, &squashfs_path, header)?;
        } else if file_type.is_file() {
            let mode = if is_elf(full_path) { 0o755 } else { 0o644 };
            let header = NodeHeader::new(mode, 0, 0, 0);
            let file = File::open(full_path)?;
            fs.push_file(file, &squashfs_path, header)?;
        }
    }

    let output = File::create(&args.output_file)?;
    fs.write(output)?;

    Ok(())
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("error: {e}");
            ExitCode::FAILURE
        }
    }
}
