param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [string]$Message = "定心 2.3.4"
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw '未安装 Git：https://git-scm.com/download/win' }
if (-not (Test-Path '.git')) { git init | Out-Null }
git add .
git commit -m $Message
git branch -M main
$remotes = git remote
if ($remotes -contains 'origin') { git remote set-url origin $RepoUrl } else { git remote add origin $RepoUrl }
git push -u origin main
Write-Host '已推送到' $RepoUrl