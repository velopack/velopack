use anyhow::Result;
use std::fs::File;
use std::io::{Read, Write};

use crate::shared;

pub fn download_url_to_file<A>(url: &str, file_path: &str, mut progress: A) -> Result<()>
where
    A: FnMut(i16),
{
    // let url = "https://proof.ovh.net/files/10Mb.dat";
    let tls_connector = native_tls::TlsConnector::new()?;
    let agent: ureq::Agent = ureq::AgentBuilder::new().tls_connector(tls_connector.into()).build();
    let response = agent.get(url).call()?;

    let total_size = response.header("Content-Length").and_then(|s| s.parse::<u64>().ok());
    let mut file = shared::retry_io(|| File::create(file_path))?;

    const CHUNK_SIZE: usize = 2 * 1024 * 1024; // 2MB
    let mut downloaded: u64 = 0;
    let mut buffer = vec![0; CHUNK_SIZE];
    let mut reader = response.into_reader();

    let mut last_progress = 0;

    while let Ok(size) = reader.read(&mut buffer) {
        if size == 0 {
            break; // End of stream
        }
        file.write_all(&buffer[..size])?;
        downloaded += size as u64;

        if total_size.is_some() {
            // floor to nearest 5% to reduce message spam
            let new_progress = (downloaded as f64 / total_size.unwrap() as f64 * 20.0).floor() as i16 * 5;
            if new_progress > last_progress {
                last_progress = new_progress;
                progress(last_progress);
            }
        }
    }

    Ok(())
}

pub fn download_url_as_string(url: &str) -> Result<String> {
    let tls_connector = native_tls::TlsConnector::new()?;
    let agent: ureq::Agent = ureq::AgentBuilder::new().tls_connector(tls_connector.into()).build();
    let r = agent.get(url).call()?.into_string()?;
    Ok(r)
}

#[test]
fn test_download_uses_tls_and_encoding_correctly() {
    assert_eq!(download_url_as_string("https://dotnetcli.blob.core.windows.net/dotnet/WindowsDesktop/5.0/latest.version").unwrap(), "5.0.17");
}

#[test]
fn test_download_file_reports_progress() {
    // https://www.ip-toolbox.com/speedtest-files/
    let test_file = "https://proof.ovh.net/files/10Mb.dat";
    let mut prog_count = 0;
    let mut last_prog = 0;

    download_url_to_file(test_file, "test_download_file_reports_progress.txt", |p| {
        assert!(p >= last_prog);
        prog_count += 1;
        last_prog = p;
    })
    .unwrap();

    assert!(prog_count >= 4);
    assert!(prog_count <= 20);
    assert_eq!(last_prog, 100);

    let p = std::path::Path::new("test_download_file_reports_progress.txt");
    let meta = p.metadata().unwrap();
    let len = meta.len();

    assert_eq!(len, 10 * 1024 * 1024);
    std::fs::remove_file(p).unwrap();
}
