use crate::errors::Error;
use std::path::Path;
use wstd::http::{Client, HeaderValue, Request, StatusCode};
use wstd::io::{empty, AsyncRead};

const MAX_REDIRECTS: u32 = 10;
const MAX_RETRIES: u32 = 3;
const USER_AGENT: &str = "velopack/1.0";

/// Downloads a URL and returns the response body as a string.
pub async fn download_url_as_string(url: &str) -> Result<String, Error> {
    download_url_as_string_with_headers(url, &[]).await
}

/// Downloads a URL with custom headers and returns the response body as a
/// string.
pub async fn download_url_as_string_with_headers(url: &str, headers: &[(&str, &str)]) -> Result<String, Error> {
    let bytes = download_url_to_bytes_with_headers(url, headers).await?;
    String::from_utf8(bytes).map_err(|e| Error::Network(e.to_string()))
}

/// Downloads a URL to a local file, reporting progress via a callback.
pub async fn download_url_to_file(url: &str, file_path: &Path, progress: impl Fn(i16)) -> Result<(), Error> {
    download_url_to_file_with_headers(url, file_path, &[], progress).await
}

/// Downloads a URL with custom headers to a local file, reporting progress
/// via a callback.
pub async fn download_url_to_file_with_headers(url: &str, file_path: &Path, headers: &[(&str, &str)], progress: impl Fn(i16)) -> Result<(), Error> {
    let bytes = download_url_to_bytes_with_headers(url, headers).await?;
    std::fs::write(file_path, &bytes)?;
    progress(100);
    Ok(())
}

/// Downloads a URL with custom headers and returns the response body as
/// bytes. Follows up to 10 redirects and retries up to 3 times on failure.
pub async fn download_url_to_bytes_with_headers(url: &str, headers: &[(&str, &str)]) -> Result<Vec<u8>, Error> {
    let mut last_error = Error::Network("No attempts made".into());

    for attempt in 0..MAX_RETRIES {
        match do_download_with_redirects(url, headers).await {
            Ok(bytes) => return Ok(bytes),
            Err(e) => {
                log::warn!("Download attempt {} of {} failed: {}", attempt + 1, MAX_RETRIES, e);
                last_error = e;
            }
        }
    }

    Err(last_error)
}

async fn do_download_with_redirects(url: &str, headers: &[(&str, &str)]) -> Result<Vec<u8>, Error> {
    let mut current_url = url.to_string();

    for _ in 0..MAX_REDIRECTS {
        let (status, resp_headers, body) = do_http_get(&current_url, headers).await?;

        if status.is_redirection() {
            let location = resp_headers
                .get(http::header::LOCATION)
                .ok_or_else(|| Error::Network(format!("Redirect {} but no Location header", status.as_u16())))?;
            let location_str = location.to_str().map_err(|e| Error::Network(format!("Invalid Location header: {}", e)))?;

            // Resolve relative redirects against the current URL
            let base = url::Url::parse(&current_url).map_err(|e| Error::Network(e.to_string()))?;
            let resolved = base.join(location_str).map_err(|e| Error::Network(e.to_string()))?;
            current_url = resolved.to_string();

            log::debug!("Following redirect {} -> {}", status.as_u16(), current_url);
            continue;
        }

        if !status.is_success() {
            return Err(Error::Network(format!("HTTP {} for {}", status.as_u16(), current_url)));
        }

        return Ok(body);
    }

    Err(Error::Network(format!("Too many redirects (>{}) for {}", MAX_REDIRECTS, url)))
}

async fn do_http_get(url: &str, extra_headers: &[(&str, &str)]) -> Result<(StatusCode, http::HeaderMap, Vec<u8>), Error> {
    let mut builder = Request::get(url).header("User-Agent", HeaderValue::from_static(USER_AGENT));

    for &(name, value) in extra_headers {
        let header_value = HeaderValue::from_str(value).map_err(|e| Error::Network(format!("Invalid header value: {}", e)))?;
        builder = builder.header(name, header_value);
    }

    let request = builder
        .body(empty())
        .map_err(|e| Error::Network(format!("Failed to build request: {}", e)))?;

    let client = Client::new();
    let mut response = client
        .send(request)
        .await
        .map_err(|e| Error::Network(format!("HTTP request failed: {}", e)))?;

    let status = response.status();
    let headers = response.headers().clone();

    let mut body_buf = Vec::new();
    response
        .body_mut()
        .read_to_end(&mut body_buf)
        .await
        .map_err(|e| Error::Network(format!("Failed to read response body: {}", e)))?;

    Ok((status, headers, body_buf))
}
