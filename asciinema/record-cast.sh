#!/bin/bash

########################
# include the magic
########################
# . ../../asciinema/demo-magic.sh

cd ../examples/AvaloniaCrossPlat


rm -rf publish
rm -rf releases

dotnet publish -r osx-x64 -c Release --self-contained -o publish
vpk pack -u AvaloniaCrossPlat -v 1.0.0 -p publish -i Velopack.icns

clear
asciinema rec ../../asciinema/demo.cast --overwrite -c "../../asciinema/do.sh"

# # Put your stuff here
# pei "dotnet publish -r osx-x64 -c Release --self-contained -o publish"
# pei "vpk pack -u AvaloniaCrossPlat -v 1.0.1 -p publish -i Velopack.icns"

# exit