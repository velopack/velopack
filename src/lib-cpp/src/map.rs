#![allow(dead_code)]

use std::path::PathBuf;
use velopack::locator::VelopackLocatorConfig;
use velopack::{UpdateInfo, UpdateOptions, VelopackAsset};
use crate::ffi::*;

fn pathbuf_to_core(dto: &String) -> PathBuf {
    PathBuf::from(dto)
}

fn pathbuf_to_bridge(dto: &PathBuf) -> String {
    dto.to_string_lossy().to_string()
}

fn string_to_core(dto: &String) -> String {
    dto.clone()
}

fn string_to_bridge(dto: &String) -> String {
    dto.clone()
}

fn bool_to_core(dto: &bool) -> bool {
    *dto
}

fn bool_to_bridge(dto: &bool) -> bool {
    *dto
}

fn u64_to_core(dto: &u64) -> u64 {
    *dto
}

fn u64_to_bridge(dto: &u64) -> u64 {
    *dto
}

// !! AUTO-GENERATED-START CORE_MAPPING
pub fn velopacklocatorconfig_to_core(dto: &VelopackLocatorConfigDto) -> VelopackLocatorConfig {
    VelopackLocatorConfig {
        RootAppDir: pathbuf_to_core(&dto.RootAppDir),
        UpdateExePath: pathbuf_to_core(&dto.UpdateExePath),
        PackagesDir: pathbuf_to_core(&dto.PackagesDir),
        ManifestPath: pathbuf_to_core(&dto.ManifestPath),
        CurrentBinaryDir: pathbuf_to_core(&dto.CurrentBinaryDir),
        IsPortable: bool_to_core(&dto.IsPortable),
    }
}

pub fn velopacklocatorconfig_to_bridge(dto: &VelopackLocatorConfig) -> VelopackLocatorConfigDto {
    VelopackLocatorConfigDto {
        RootAppDir: pathbuf_to_bridge(&dto.RootAppDir),
        UpdateExePath: pathbuf_to_bridge(&dto.UpdateExePath),
        PackagesDir: pathbuf_to_bridge(&dto.PackagesDir),
        ManifestPath: pathbuf_to_bridge(&dto.ManifestPath),
        CurrentBinaryDir: pathbuf_to_bridge(&dto.CurrentBinaryDir),
        IsPortable: bool_to_bridge(&dto.IsPortable),
    }
}

pub fn velopacklocatorconfig_to_core_option(dto: &VelopackLocatorConfigDtoOption) -> Option<VelopackLocatorConfig> {
    if dto.has_data { Some(velopacklocatorconfig_to_core(&dto.data)) } else { None }
}

pub fn velopacklocatorconfig_to_bridge_option(dto: &Option<VelopackLocatorConfig>) -> VelopackLocatorConfigDtoOption {
    match dto {
        Some(dto) => VelopackLocatorConfigDtoOption { data: velopacklocatorconfig_to_bridge(dto), has_data: true },
        None => VelopackLocatorConfigDtoOption { data: Default::default(), has_data: false },
    }
}

pub fn velopackasset_to_core(dto: &VelopackAssetDto) -> VelopackAsset {
    VelopackAsset {
        PackageId: string_to_core(&dto.PackageId),
        Version: string_to_core(&dto.Version),
        Type: string_to_core(&dto.Type),
        FileName: string_to_core(&dto.FileName),
        SHA1: string_to_core(&dto.SHA1),
        SHA256: string_to_core(&dto.SHA256),
        Size: u64_to_core(&dto.Size),
        NotesMarkdown: string_to_core(&dto.NotesMarkdown),
        NotesHtml: string_to_core(&dto.NotesHtml),
    }
}

pub fn velopackasset_to_bridge(dto: &VelopackAsset) -> VelopackAssetDto {
    VelopackAssetDto {
        PackageId: string_to_bridge(&dto.PackageId),
        Version: string_to_bridge(&dto.Version),
        Type: string_to_bridge(&dto.Type),
        FileName: string_to_bridge(&dto.FileName),
        SHA1: string_to_bridge(&dto.SHA1),
        SHA256: string_to_bridge(&dto.SHA256),
        Size: u64_to_bridge(&dto.Size),
        NotesMarkdown: string_to_bridge(&dto.NotesMarkdown),
        NotesHtml: string_to_bridge(&dto.NotesHtml),
    }
}

pub fn velopackasset_to_core_option(dto: &VelopackAssetDtoOption) -> Option<VelopackAsset> {
    if dto.has_data { Some(velopackasset_to_core(&dto.data)) } else { None }
}

pub fn velopackasset_to_bridge_option(dto: &Option<VelopackAsset>) -> VelopackAssetDtoOption {
    match dto {
        Some(dto) => VelopackAssetDtoOption { data: velopackasset_to_bridge(dto), has_data: true },
        None => VelopackAssetDtoOption { data: Default::default(), has_data: false },
    }
}

pub fn updateinfo_to_core(dto: &UpdateInfoDto) -> UpdateInfo {
    UpdateInfo {
        TargetFullRelease: velopackasset_to_core(&dto.TargetFullRelease),
        IsDowngrade: bool_to_core(&dto.IsDowngrade),
    }
}

pub fn updateinfo_to_bridge(dto: &UpdateInfo) -> UpdateInfoDto {
    UpdateInfoDto {
        TargetFullRelease: velopackasset_to_bridge(&dto.TargetFullRelease),
        IsDowngrade: bool_to_bridge(&dto.IsDowngrade),
    }
}

pub fn updateinfo_to_core_option(dto: &UpdateInfoDtoOption) -> Option<UpdateInfo> {
    if dto.has_data { Some(updateinfo_to_core(&dto.data)) } else { None }
}

pub fn updateinfo_to_bridge_option(dto: &Option<UpdateInfo>) -> UpdateInfoDtoOption {
    match dto {
        Some(dto) => UpdateInfoDtoOption { data: updateinfo_to_bridge(dto), has_data: true },
        None => UpdateInfoDtoOption { data: Default::default(), has_data: false },
    }
}

pub fn updateoptions_to_core(dto: &UpdateOptionsDto) -> UpdateOptions {
    UpdateOptions {
        AllowVersionDowngrade: bool_to_core(&dto.AllowVersionDowngrade),
        ExplicitChannel: if dto.ExplicitChannel.has_data { Some(string_to_core(&dto.ExplicitChannel.data)) } else { None },
    }
}

pub fn updateoptions_to_bridge(dto: &UpdateOptions) -> UpdateOptionsDto {
    UpdateOptionsDto {
        AllowVersionDowngrade: bool_to_bridge(&dto.AllowVersionDowngrade),
        ExplicitChannel: StringOption { data: string_to_bridge(&dto.ExplicitChannel.clone().unwrap_or_default()), has_data: dto.ExplicitChannel.is_some() },
    }
}

pub fn updateoptions_to_core_option(dto: &UpdateOptionsDtoOption) -> Option<UpdateOptions> {
    if dto.has_data { Some(updateoptions_to_core(&dto.data)) } else { None }
}

pub fn updateoptions_to_bridge_option(dto: &Option<UpdateOptions>) -> UpdateOptionsDtoOption {
    match dto {
        Some(dto) => UpdateOptionsDtoOption { data: updateoptions_to_bridge(dto), has_data: true },
        None => UpdateOptionsDtoOption { data: Default::default(), has_data: false },
    }
}
// !! AUTO-GENERATED-END CORE_MAPPING