use velopack::VelopackAssetFeed;

#[test]
fn noteshtml_csharp_format() {
    // JSON as produced by C# vpk (SimpleJson.SerializeObject): field is "NotesHTML" (uppercase HTML)
    let json = r##"{"Assets":[{
        "PackageId":"TestApp","Version":"2.0.0","Type":"Full",
        "FileName":"TestApp-2.0.0-full.nupkg","SHA1":"abc","SHA256":"def","Size":1000,
        "NotesMarkdown":"# v2","NotesHTML":"<h1>v2</h1>"
    }]}"##;
    let feed: VelopackAssetFeed = serde_json::from_str(json).unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].NotesHtml, "<h1>v2</h1>");
}

#[test]
fn noteshtml_rust_format() {
    // JSON with Rust field naming: "NotesHtml" (camelCase)
    let json = r##"{"Assets":[{
        "PackageId":"TestApp","Version":"2.0.0","Type":"Full",
        "FileName":"TestApp-2.0.0-full.nupkg","SHA1":"abc","SHA256":"def","Size":1000,
        "NotesMarkdown":"# v2","NotesHtml":"<h1>v2</h1>"
    }]}"##;
    let feed: VelopackAssetFeed = serde_json::from_str(json).unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].NotesHtml, "<h1>v2</h1>");
}

#[test]
fn noteshtml_lowercase() {
    // JSON with all-lowercase keys
    let json = r##"{"assets":[{
        "packageid":"TestApp","version":"2.0.0","type":"Full",
        "filename":"TestApp-2.0.0-full.nupkg","sha1":"abc","sha256":"def","size":1000,
        "notesmarkdown":"# v2","noteshtml":"<h1>v2</h1>"
    }]}"##;
    let feed: VelopackAssetFeed = serde_json::from_str(json).unwrap();
    assert_eq!(feed.Assets.len(), 1);
    assert_eq!(feed.Assets[0].NotesHtml, "<h1>v2</h1>");
}
