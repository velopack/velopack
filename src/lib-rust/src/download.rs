use std::fs::File;
use std::io::{Read, Write};

use crate::{util, Error};

/// Downloads a file from a URL and writes it to a file while reporting progress from 0-100.
pub fn download_url_to_file<A>(url: &str, file_path: &str, mut progress: A) -> Result<(), Error>
where
    A: FnMut(i16),
{
    let agent = get_download_agent()?;
    let (head, body) = agent.get(url).call()?.into_parts();

    let total_size = head.headers.get("Content-Length").and_then(|s| s.to_str().ok()).and_then(|s| s.parse::<u64>().ok());
    let mut file = util::retry_io(|| File::create(file_path))?;

    const CHUNK_SIZE: usize = 2 * 1024 * 1024; // 2MB
    let mut downloaded: u64 = 0;
    let mut buffer = vec![0; CHUNK_SIZE];
    let mut reader = body.into_reader();

    let mut last_progress = 0;

    loop {
        let size = reader.read(&mut buffer)?; // Explicitly propagate errors
        if size == 0 {
            break; // End of stream
        }
        file.write_all(&buffer[..size])?;
        downloaded += size as u64;

        if let Some(total) = total_size {
            // floor to nearest 5% to reduce message spam
            let new_progress = (downloaded as f64 / total as f64 * 20.0).floor() as i16 * 5;
            if new_progress > last_progress {
                last_progress = new_progress;
                progress(last_progress);
            }
        }
    }

    Ok(())
}

/// Downloads a file from a URL and returns it as a string.
pub fn download_url_as_string(url: &str) -> Result<String, Error> {
    let agent = get_download_agent()?;
    let r = agent.get(url).call()?.body_mut().read_to_string()?;
    Ok(r)
}

fn get_download_agent() -> Result<ureq::Agent, Error> {
    // let tls_builder = native_tls::TlsConnector::builder();
    // let tls_connector = tls_builder.build()?;
    // Ok(ureq::AgentBuilder::new().tls_connector(tls_connector.into()).build())
    Ok(ureq::Agent::config_builder().build().into())
}

#[test]
fn test_download_uses_tls_and_encoding_correctly() {
    assert_eq!(
        download_url_as_string("https://dotnetcli.blob.core.windows.net/dotnet/WindowsDesktop/5.0/latest.version").unwrap(),
        "5.0.17"
    );
}

#[test]
fn test_download_file_reports_progress() {
    // https://www.ip-toolbox.com/speedtest-files/
    let test_file = "https://proof.ovh.net/files/10Mb.dat";
    let mut prog_count = 0;
    let mut last_prog = 0;

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let tmppath = tmpfile.path();

    download_url_to_file(test_file, tmppath.to_str().unwrap(), |p| {
        assert!(p >= last_prog);
        prog_count += 1;
        last_prog = p;
    })
    .unwrap();

    assert!(prog_count >= 4);
    assert!(prog_count <= 20);
    assert_eq!(last_prog, 100);

    let meta = tmppath.metadata().unwrap();
    let len = meta.len();

    assert_eq!(len, 10 * 1024 * 1024);
}

#[test]
fn test_interrupted_download() {
    use std::io::Write;
    use std::net::TcpListener;
    use std::thread;

    // Start a simple HTTP server that cuts the connection mid-download
    let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
    let addr = listener.local_addr().unwrap();

    thread::spawn(move || {
        if let Ok((mut stream, _)) = listener.accept() {
            // Write a partial HTTP response
            let response = "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\n";
            stream.write_all(response.as_bytes()).expect("Failed to write response");

            // Send part of the data, then close the connection early
            let partial_data = vec![0u8; 1024]; // 1 KB
            stream.write_all(&partial_data).expect("Failed to write partial data");

            // Connection closes here, simulating an interrupted download
            thread::sleep(std::time::Duration::from_millis(100));
        }
    });

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let result = download_url_to_file(
        &format!("http://{}", addr),
        tmpfile.path().to_str().unwrap(),
        |_| {}
    );

    assert!(result.is_err(), "Download should fail due to connection interruption");
}

#[test]
fn test_successful_download() {
    use std::io::Write;
    use std::net::TcpListener;
    use std::thread;

    // Start a simple HTTP server
    let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
    let addr = listener.local_addr().unwrap();

    thread::spawn(move || {
        if let Ok((mut stream, _)) = listener.accept() {
            // Write a full HTTP response with full content
            let response = "HTTP/1.1 200 OK\r\nContent-Length: 10240\r\n\r\n";
            stream.write_all(response.as_bytes()).expect("Failed to write response");

            // Send full 10KB of data
            let full_data = vec![0u8; 10240];
            stream.write_all(&full_data).expect("Failed to write full data");

            // give client time to receive and disconnect
            thread::sleep(std::time::Duration::from_millis(100));
        }
    });

    let tmpfile = tempfile::NamedTempFile::new().unwrap();
    let _ = download_url_to_file(
        &format!("http://{}", addr),
        tmpfile.path().to_str().unwrap(),
        |_| {},
    ).unwrap();

    // Verify that the downloaded file has the expected size
    let metadata = tmpfile.path().metadata().unwrap();
    assert_eq!(metadata.len(), 10240, "Downloaded file size should match the expected content size");
}