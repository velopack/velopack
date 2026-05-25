use crate::errors::Error;
use crate::host_fs;
use crate::misc::ChecksumBuilder;
use wstd::http::{Client, HeaderValue, Request};
use wstd::io::{empty, AsyncRead};

const MAX_REDIRECTS: u32 = 10;
const MAX_RETRIES: u32 = 3;
const USER_AGENT: &str = "velopack/1.0";

pub struct DownloadResult {
    pub size: u64,
    pub sha1: String,
    pub sha256: String,
}

pub async fn download_url_as_string(url: &str) -> Result<String, Error> {
    download_url_as_string_with_headers(url, &[]).await
}

pub async fn download_url_as_string_with_headers(url: &str, headers: &[(&str, &str)]) -> Result<String, Error> {
    let mut last_error = Error::Network("No attempts made".into());

    for attempt in 0..MAX_RETRIES {
        match do_download_string(url, headers).await {
            Ok(s) => return Ok(s),
            Err(e) => {
                log::warn!("Download attempt {} of {} failed: {}", attempt + 1, MAX_RETRIES, e);
                last_error = e;
            }
        }
    }

    Err(last_error)
}

pub async fn download_url_to_file(url: &str, file_path: &str, progress: impl Fn(i16)) -> Result<DownloadResult, Error> {
    download_url_to_file_with_headers(url, file_path, &[], progress).await
}

pub async fn download_url_to_file_with_headers(
    url: &str,
    file_path: &str,
    headers: &[(&str, &str)],
    progress: impl Fn(i16),
) -> Result<DownloadResult, Error> {
    let mut last_error = Error::Network("No attempts made".into());

    for attempt in 0..MAX_RETRIES {
        match do_download_file(url, file_path, headers, &progress).await {
            Ok(result) => return Ok(result),
            Err(e) => {
                log::warn!("Download attempt {} of {} failed: {}", attempt + 1, MAX_RETRIES, e);
                last_error = e;
            }
        }
    }

    Err(last_error)
}

async fn do_download_string(url: &str, headers: &[(&str, &str)]) -> Result<String, Error> {
    let mut current_url = url.to_string();

    for _ in 0..MAX_REDIRECTS {
        let mut response = do_http_get(&current_url, headers).await?;
        let status = response.status();

        if status.is_redirection() {
            current_url = resolve_redirect(&current_url, &response)?;
            continue;
        }

        if !status.is_success() {
            return Err(Error::Network(format!("HTTP {} for {}", status.as_u16(), current_url)));
        }

        let mut body_buf = Vec::new();
        response
            .body_mut()
            .read_to_end(&mut body_buf)
            .await
            .map_err(|e| Error::Network(format!("Failed to read response body: {}", e)))?;

        return String::from_utf8(body_buf).map_err(|e| Error::Network(e.to_string()));
    }

    Err(Error::Network(format!("Too many redirects (>{}) for {}", MAX_REDIRECTS, url)))
}

async fn do_download_file(url: &str, file_path: &str, headers: &[(&str, &str)], progress: &dyn Fn(i16)) -> Result<DownloadResult, Error> {
    let mut current_url = url.to_string();

    for _ in 0..MAX_REDIRECTS {
        let mut response = do_http_get(&current_url, headers).await?;
        let status = response.status();

        if status.is_redirection() {
            current_url = resolve_redirect(&current_url, &response)?;
            continue;
        }

        if !status.is_success() {
            return Err(Error::Network(format!("HTTP {} for {}", status.as_u16(), current_url)));
        }

        let content_length = response
            .headers()
            .get(http::header::CONTENT_LENGTH)
            .and_then(|v| v.to_str().ok())
            .and_then(|s| s.parse::<u64>().ok())
            .unwrap_or(0);

        let handle = host_fs::open(file_path, true, true)?;
        let mut csb = ChecksumBuilder::new();
        let mut buf = [0u8; 64 * 1024];
        let mut last_pct: i16 = -1;

        let stream_result: Result<(), Error> = async {
            loop {
                let n = response
                    .body_mut()
                    .read(&mut buf)
                    .await
                    .map_err(|e| Error::Network(format!("Failed to read response body: {}", e)))?;
                if n == 0 {
                    break;
                }
                csb.update(&buf[..n]);
                host_fs::write(handle, &buf[..n])?;
                if content_length > 0 {
                    let pct = ((csb.size() as f64 / content_length as f64) * 100.0) as i16;
                    if pct != last_pct {
                        progress(pct);
                        last_pct = pct;
                    }
                }
            }
            Ok(())
        }
        .await;

        host_fs::close(handle)?;
        stream_result?;
        progress(100);
        let size = csb.size();
        let (sha1, sha256) = csb.finish();
        return Ok(DownloadResult { size, sha1, sha256 });
    }

    Err(Error::Network(format!("Too many redirects (>{}) for {}", MAX_REDIRECTS, url)))
}

fn resolve_redirect<T>(current_url: &str, response: &http::Response<T>) -> Result<String, Error> {
    let location = response
        .headers()
        .get(http::header::LOCATION)
        .ok_or_else(|| Error::Network(format!("Redirect {} but no Location header", response.status().as_u16())))?;
    let location_str = location.to_str().map_err(|e| Error::Network(format!("Invalid Location header: {}", e)))?;

    let base = url::Url::parse(current_url).map_err(|e| Error::Network(e.to_string()))?;
    let resolved = base.join(location_str).map_err(|e| Error::Network(e.to_string()))?;

    log::debug!("Following redirect {} -> {}", response.status().as_u16(), resolved);
    Ok(resolved.to_string())
}

async fn do_http_get(url: &str, extra_headers: &[(&str, &str)]) -> Result<wstd::http::Response<wstd::http::body::IncomingBody>, Error> {
    let mut builder = Request::get(url).header("User-Agent", HeaderValue::from_static(USER_AGENT));

    for &(name, value) in extra_headers {
        let header_value = HeaderValue::from_str(value).map_err(|e| Error::Network(format!("Invalid header value: {}", e)))?;
        builder = builder.header(name, header_value);
    }

    let request = builder
        .body(empty())
        .map_err(|e| Error::Network(format!("Failed to build request: {}", e)))?;

    let client = Client::new();
    let response = client
        .send(request)
        .await
        .map_err(|e| Error::Network(format!("HTTP request failed: {}", e)))?;

    Ok(response)
}
