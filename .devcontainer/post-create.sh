#!/usr/bin/env bash
set -e

sudo chown -R "$(whoami)":"$(whoami)" "$HOME/.azure"

az extension add --name azure-iot --only-show-errors

npm install -g azure-functions-core-tools@4
npm install -g azurite

echo "--- versions ---"
dotnet --version
func --version
az version --output tsv --query '"azure-cli"'
node --version