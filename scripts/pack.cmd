@echo off
cd /d "%~dp0\.."
dotnet build Ponder.sln -c Release && dotnet script scripts\pack.csx
