use anyhow::Result;
use std::{fs::OpenOptions, io::Write};

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();

    if args[0] == "--veloapp-version" {
        println!("1.0.0");
        return Ok(());
    }

    let line = args.join(" ") + "\n";
    let mut file = OpenOptions::new().create(true).append(true).open("args.txt")?;
    file.write_all(line.as_bytes())?;
    Ok(())
}
