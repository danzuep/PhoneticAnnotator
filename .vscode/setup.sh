#!/usr/bin/env bash

# Setup Instructions for PowerShell / Windows:
# winget install --id Microsoft.DotNet.SDK.10 -e
# winget install --id Git.Git -e
# winget install --id github.cli -e
# bash -c "./.vscode/setup.sh"

set -e

# Variable Definitions
ORG_NAME="danzuep"
PROJECT_NAME="${1:-${projectName:-PhoneticAnnotator}}"
PROJECT_DESC="Multi-Language Phonetic Gloss Engine that parses raw text in ideographic/polyphonic languages (Japanese, Chinese) and attaches phonetic annotations (Furigana, Pinyin/Jyutping)."

# Locate and append dotnet to PATH if missing in non-interactive bash
if ! command -v dotnet &> /dev/null; then
    echo "'dotnet' CLI could not be found in PATH, attempting to install via apt." >&2
    sudo apt update && sudo apt install -y dotnet-sdk-10.0
fi

# 1. Environment Verification
echo "Using .NET SDK Version:"
dotnet --version

echo "Initializing project: ${PROJECT_NAME}..."

# 2. Root Directory & Git Repo Setup
if [ ! -d "${PROJECT_NAME}" ]; then
    mkdir -p "${PROJECT_NAME}"
fi
cd "${PROJECT_NAME}"

if [ ! -d ".git" ]; then
    git init
    git branch -M main
fi

# 3. VS Code Workspace Setup
if [ ! -d ".vscode" ]; then
	mkdir -p ".vscode"
	cat << EOF > ".vscode/${PROJECT_NAME}.code-workspace"
{
    "folders": [
        {
            "path": ".."
        }
    ]
}
EOF
fi

# 4. .NET Project & Solution Setup
mkdir -p src tests

# Core Library
if [ ! -d "src/${PROJECT_NAME}.Core" ]; then
    dotnet new classlib -n "${PROJECT_NAME}.Core" -o "src/${PROJECT_NAME}.Core"
fi

# CLI Project
if [ ! -d "src/${PROJECT_NAME}.Cli" ]; then
    dotnet new console -n "${PROJECT_NAME}.Cli" -o "src/${PROJECT_NAME}.Cli"
    dotnet add "src/${PROJECT_NAME}.Cli" package "Microsoft.Extensions.Hosting"
    dotnet add "src/${PROJECT_NAME}.Cli" package "Microsoft.Extensions.DependencyInjection"
    dotnet add "src/${PROJECT_NAME}.Cli" reference "src/${PROJECT_NAME}.Core/${PROJECT_NAME}.Core.csproj"
fi

# Test Project
if [ ! -d "tests/${PROJECT_NAME}.Tests" ]; then
    dotnet new xunit -n "${PROJECT_NAME}.Tests" -o "tests/${PROJECT_NAME}.Tests"
    dotnet add "tests/${PROJECT_NAME}.Tests" reference "src/${PROJECT_NAME}.Core/${PROJECT_NAME}.Core.csproj"
fi

# Solution File Creation inside src/
SLN_PATH="src/${PROJECT_NAME}.sln"
SLNX_PATH="${SLN_PATH}x"

if [ ! -f "${SLNX_PATH}" ] && [ ! -f "${SLN_PATH}" ]; then
    cd src
    # .NET 10 SDK creates slnx solution format
    dotnet new sln -n "${PROJECT_NAME}"

    dotnet sln add "${PROJECT_NAME}.Core/${PROJECT_NAME}.Core.csproj"
    dotnet sln add "${PROJECT_NAME}.Cli/${PROJECT_NAME}.Cli.csproj"
    dotnet sln add "../tests/${PROJECT_NAME}.Tests/${PROJECT_NAME}.Tests.csproj"
    cd ..
fi

# 5. Build Solution Verification
BUILD_PATH="${SLNX_PATH}"
if [ -f "${SLN_PATH}" ]; then
    BUILD_PATH="${SLN_PATH}"
fi
dotnet build "${BUILD_PATH}" --verbosity quiet

# 6. Git Commit & GitHub Repository Creation
# Verify Git identity is set; fall back to local defaults if missing
if git config user.name > /dev/null && git config user.email > /dev/null; then
    git add .
    if git status --porcelain | grep -q .; then
        git commit -m "Initial commit: Solution layout and projects"
    fi

    if ! git remote | grep -q "origin"; then
        gh repo create "${ORG_NAME}/${PROJECT_NAME}" --source=. --public --description "${PROJECT_DESC}" # --confirm
        git push -u origin main
    fi
fi

echo "Project ${PROJECT_NAME} initialized and built successfully."
