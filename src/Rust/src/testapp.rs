use anyhow::Result;
use std::{fs::OpenOptions, io::Write};

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    // Join the arguments with a space
    let line = args.join(" ") + "\n";

    // Open "args.txt" for appending
    let mut file = OpenOptions::new().create(true).append(true).open("args.txt")?;

    // Write the line to the file
    file.write_all(line.as_bytes())?;

    Ok(())
}
