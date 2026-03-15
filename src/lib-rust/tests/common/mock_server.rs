use std::io::{Read, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::sync::{Arc, Mutex};
use std::thread;

#[derive(Clone)]
pub struct MockRoute {
    pub path_contains: String,
    pub response_code: u16,
    pub response_body: Vec<u8>,
    pub expected_headers: Vec<(String, String)>,
}

pub struct MockHttpServer {
    pub addr: SocketAddr,
    routes: Arc<Mutex<Vec<MockRoute>>>,
    _handle: thread::JoinHandle<()>,
}

impl MockHttpServer {
    /// Create a server with no routes initially. Use `add_route` to add routes before making requests.
    pub fn empty() -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("Failed to bind test server");
        let addr = listener.local_addr().unwrap();
        let routes: Arc<Mutex<Vec<MockRoute>>> = Arc::new(Mutex::new(Vec::new()));
        let routes_clone = Arc::clone(&routes);

        let handle = thread::spawn(move || {
            while let Ok((stream, _)) = listener.accept() {
                let routes = Arc::clone(&routes_clone);
                thread::spawn(move || {
                    let routes = routes.lock().unwrap().clone();
                    handle_connection(stream, &routes);
                });
            }
        });

        MockHttpServer {
            addr,
            routes,
            _handle: handle,
        }
    }

    pub fn add_route(&self, route: MockRoute) {
        self.routes.lock().unwrap().push(route);
    }

    pub fn url(&self) -> String {
        format!("http://127.0.0.1:{}", self.addr.port())
    }
}

fn handle_connection(mut stream: TcpStream, routes: &[MockRoute]) {
    let mut buf = [0u8; 4096];
    let n = match stream.read(&mut buf) {
        Ok(n) => n,
        Err(_) => return,
    };
    let request = String::from_utf8_lossy(&buf[..n]).to_string();
    let request_lower = request.to_lowercase();

    for route in routes {
        if request.contains(&route.path_contains) {
            // Verify expected headers (case-insensitive)
            for (name, value) in &route.expected_headers {
                let expected = format!("{}: {}", name, value).to_lowercase();
                assert!(
                    request_lower.contains(&expected),
                    "Expected header '{}:{}' not found in request:\n{}",
                    name,
                    value,
                    request
                );
            }

            let status_text = match route.response_code {
                200 => "OK",
                404 => "Not Found",
                500 => "Internal Server Error",
                _ => "Unknown",
            };
            let response = format!(
                "HTTP/1.1 {} {}\r\nContent-Length: {}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n",
                route.response_code,
                status_text,
                route.response_body.len()
            );
            let _ = stream.write_all(response.as_bytes());
            let _ = stream.write_all(&route.response_body);
            let _ = stream.flush();
            return;
        }
    }

    // 404 fallback
    let response = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
    let _ = stream.write_all(response.as_bytes());
    let _ = stream.flush();
}
