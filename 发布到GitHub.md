# 发布到 GitHub

## 1. 创建仓库

1. 打开 https://github.com/new
2. 仓库名建议：`dingxin-focus`（或 `定心`）
3. 不要勾选 “Add a README / .gitignore / license”（本地已有）
4. 创建后复制仓库地址，例如：`https://github.com/你的用户名/dingxin-focus.git`

## 2. 安装 Git

下载安装：https://git-scm.com/download/win
安装后在 PowerShell 里确认：

```powershell
git --version
```

## 3. 推送（在本目录执行）

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -RepoUrl "https://github.com/你的用户名/dingxin-focus.git"
```

第一次会要求登录 GitHub（浏览器授权或填 token）。

也可以手动：

```powershell
git init
git add .
git commit -m "定心 2.3.4"
git branch -M main
git remote add origin https://github.com/你的用户名/dingxin-focus.git
git push -u origin main
```

## 4. 上传安装包到 Releases

1. 仓库页右侧 → Releases → Draft a new release
2. Tag 填 `v2.3.4`，标题 `定心 2.3.4`
3. 上传 `定心_2.3.4_Setup.exe`（可同时上传 `定心_应急停止_1.6.0.exe`）
4. 发布

## 5. 可选

- 修改 `LICENSE` 里的 `你的名字`；
- 修改 README 里的仓库链接/徽章；
- 在仓库 Settings → Pages 不需要开启（纯桌面程序）。