# Setup Steps

Create the project:

```pwsh
# winget install --id Microsoft.DotNet.SDK.10 -e
# winget install --id Git.Git -e
# winget install --id github.cli -e
# cd C:\Source\danzuep
$orgName = "danzuep"
$projectName = "PhoneticAnnotator"
$gitUrl = "https://github.com/${orgName}/${projectName}.git"
$projectDescription = "Multi-Language Phonetic Gloss Engine that parses raw text in ideographic/polyphonic languages (Japanese, Chinese) and attaches phonetic annotations (Furigana, Pinyin/Jyutping)."
mkdir $projectName
cd $projectName
git init
gh repo create --source=. --public --description $projectDescription # --confirm
mkdir -p ".vscode"
cat <<'EOF' > ".vscode/${projectName}.code-workspace"
{
  "folders": [
    {
      "path": ".."
    }
  ]
}
EOF
git add ".vscode/${projectName}.code-workspace"
git add ".vscode/steps.md"
git commit -m "Initial commit"
git branch -M main
git push -u origin main
bash ./setup.sh $projectName
```
