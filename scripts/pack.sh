#!/usr/bin/env bash
cd "$(dirname "$0")/.."
dotnet build Ponder.sln -c Release && dotnet script scripts/pack.csx
