"""The generated binding types keep the constructor shape older code was written against: fields added
later (the chunk-index pointer, DisableChunkedUpdates) are trailing keyword arguments with defaults."""

import velopack


def test_update_options_positional_shape_is_unchanged():
    options = velopack.UpdateOptions(True, "beta")
    assert options.AllowVersionDowngrade is True
    assert options.ExplicitChannel == "beta"
    assert options.DisableChunkedUpdates is False

    options = velopack.UpdateOptions(False, DisableChunkedUpdates=True)
    assert options.ExplicitChannel is None
    assert options.DisableChunkedUpdates is True


def test_velopack_asset_without_chunk_index_fields():
    asset = velopack.VelopackAsset("app", "1.0.0", "Full", "app-1.0.0-full.nupkg", "sha1", "sha256", 10, "", "")
    assert asset.FileName == "app-1.0.0-full.nupkg"
    assert asset.Size == 10
    assert asset.ChunkIndexOffset == 0
    assert asset.ChunkIndexLength == 0
    assert asset.ChunkIndexSHA256 == ""

    asset = velopack.VelopackAsset("app", "1.0.0", "Full", "f", "", "", 10, "", "", ChunkIndexOffset=5, ChunkIndexLength=3, ChunkIndexSHA256="AB")
    assert (asset.ChunkIndexOffset, asset.ChunkIndexLength, asset.ChunkIndexSHA256) == (5, 3, "AB")
