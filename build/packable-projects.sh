#!/usr/bin/env bash
# Single source of truth for the projects that ship as NuGet packages.
# Sourced by build/pack.sh (what to pack) and build/assert-pack-layout.sh (what must exist).
# Package id is the project file name without its extension.

packable_projects=(
	src/Vexel.Telegram.Client/Vexel.Telegram.Client.csproj
	src/Vexel.Telegram.Handlers/Vexel.Telegram.Handlers.csproj
	src/Vexel.Telegram.Hosting/Vexel.Telegram.Hosting.csproj
	src/Vexel.Telegram.AspNetCore/Vexel.Telegram.AspNetCore.csproj
)
