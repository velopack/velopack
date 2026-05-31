use pyo3::prelude::*;
use velopack::sources::{AutoSource, GiteaSource, GithubSource, GitlabSource, HttpSource, UpdateSource};

/// Retrieves available releases from a GitHub repository. Supports both github.com
/// and GitHub Enterprise instances.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "GithubSource", from_py_object)]
#[derive(Clone)]
pub struct PyGithubSource {
    pub repo_url: String,
    pub access_token: Option<String>,
    pub prerelease: bool,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl PyGithubSource {
    /// Create a new GithubSource.
    /// - `repo_url`: The URL of the GitHub repository (e.g. "https://github.com/myuser/myrepo")
    /// - `access_token`: Optional GitHub access token. Without one, requests are rate limited to 60/hr per IP.
    /// - `prerelease`: If true, pre-releases will also be searched/downloaded.
    #[new]
    #[pyo3(signature = (repo_url, access_token = None, prerelease = false))]
    pub fn new(repo_url: String, access_token: Option<String>, prerelease: bool) -> Self {
        PyGithubSource {
            repo_url,
            access_token,
            prerelease,
        }
    }
}

/// Retrieves available releases from a GitLab repository.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "GitlabSource", from_py_object)]
#[derive(Clone)]
pub struct PyGitlabSource {
    pub repo_url: String,
    pub access_token: Option<String>,
    pub prerelease: bool,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl PyGitlabSource {
    /// Create a new GitlabSource.
    /// - `repo_url`: The GitLab API URL (e.g. "https://gitlab.com/api/v4/projects/ProjectId")
    /// - `access_token`: Optional GitLab access token (sent as PRIVATE-TOKEN header).
    /// - `prerelease`: If true, upcoming/pre-releases will also be searched.
    #[new]
    #[pyo3(signature = (repo_url, access_token = None, prerelease = false))]
    pub fn new(repo_url: String, access_token: Option<String>, prerelease: bool) -> Self {
        PyGitlabSource {
            repo_url,
            access_token,
            prerelease,
        }
    }
}

/// Retrieves available releases from a Gitea repository.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "GiteaSource", from_py_object)]
#[derive(Clone)]
pub struct PyGiteaSource {
    pub repo_url: String,
    pub access_token: Option<String>,
    pub prerelease: bool,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl PyGiteaSource {
    /// Create a new GiteaSource.
    /// - `repo_url`: The URL of the Gitea repository (e.g. "https://gitea.com/myuser/myrepo")
    /// - `access_token`: Optional Gitea access token (sent as `Authorization: token {token}`).
    /// - `prerelease`: If true, pre-releases will also be searched/downloaded.
    #[new]
    #[pyo3(signature = (repo_url, access_token = None, prerelease = false))]
    pub fn new(repo_url: String, access_token: Option<String>, prerelease: bool) -> Self {
        PyGiteaSource {
            repo_url,
            access_token,
            prerelease,
        }
    }
}

/// Retrieves updates from a static file host or other web server.
#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pyclass)]
#[pyclass(name = "HttpSource", from_py_object)]
#[derive(Clone)]
pub struct PyHttpSource {
    pub url: String,
}

#[cfg_attr(feature = "stub-gen", pyo3_stub_gen::derive::gen_stub_pymethods)]
#[pymethods]
impl PyHttpSource {
    /// Create a new HttpSource with the specified base URL.
    #[new]
    pub fn new(url: String) -> Self {
        PyHttpSource { url }
    }
}

/// A union type accepted by UpdateManager that can be either a URL/path string
/// (auto-detected) or one of the explicit source classes.
#[derive(FromPyObject)]
pub enum PySourceArg {
    Github(PyGithubSource),
    Gitlab(PyGitlabSource),
    Gitea(PyGiteaSource),
    Http(PyHttpSource),
    Auto(String),
}

impl PySourceArg {
    pub fn into_source(self) -> Box<dyn UpdateSource> {
        match self {
            PySourceArg::Github(s) => Box::new(GithubSource::new(&s.repo_url, s.access_token, s.prerelease)),
            PySourceArg::Gitlab(s) => Box::new(GitlabSource::new(&s.repo_url, s.access_token, s.prerelease)),
            PySourceArg::Gitea(s) => Box::new(GiteaSource::new(&s.repo_url, s.access_token, s.prerelease)),
            PySourceArg::Http(s) => Box::new(HttpSource::new(&s.url)),
            PySourceArg::Auto(s) => Box::new(AutoSource::new(&s)),
        }
    }
}

// FromPyObject unions aren't pyclasses, so stub-gen can't derive their Python type. Describe it explicitly.
#[cfg(feature = "stub-gen")]
pyo3_stub_gen::impl_stub_type!(PySourceArg = PyGithubSource | PyGitlabSource | PyGiteaSource | PyHttpSource | String);
