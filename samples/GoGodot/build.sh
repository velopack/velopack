cp ../../target/debug/libvelopack_libc.so ./graphics/libvelopack_libc.so
LD_LIBRARY_PATH="$(realpath ../../target/debug)" CGO_LDFLAGS="-L$(realpath ../../target/debug)" go tool gd build
GOOS=$(go env GOOS)
GOARCH=$(go env GOARCH)
cp ../../target/debug/libvelopack_libc.so ./releases/linux/amd64/libvelopack_libc.so
vpk pack --packId "VelopackGoGodotSampleApp" --packVersion "$1" --packDir releases/$GOOS/$GOARCH --mainExe GoGodot -o releases
