# iOS 证书配置指南

本目录是给 iOS 打包（GitHub Actions 的 `build-ios.yml`，或本地的 `build/build.sh` / `build/build.ps1`）准备签名材料的工作区。

> ⚠️ 除了 `README.md`、`csr.conf`、`convert.bat`，这里的其它文件（私钥、CSR、证书、描述文件、`.p12`、Base64 文本）都是敏感材料，已被 `.gitignore` 排除。**不要提交，也不要贴到 Issue / 聊天 / 日志里。**

## 📁 本目录的文件

| 文件 | 说明 | 是否进仓库 |
|------|------|-----------|
| `csr.conf` | 生成 CSR 用的配置（只有名称，没有敏感信息） | ✅ 提交 |
| `convert.bat` | 把下载的证书和私钥转成 GitHub Secrets 需要的格式（Windows） | ✅ 提交 |
| `ios_distribution.key` | 私钥，签名用。**丢了无法找回，泄露了要立即在 Apple Developer 吊销对应证书** | ❌ 忽略 |
| `ios_distribution.csr` | 证书签名请求，上传到 Apple Developer | ❌ 忽略 |
| `ios_distribution.cer` | Apple 签发的证书（从网站下载） | ❌ 忽略 |
| `*.mobileprovision` | 描述文件（从网站下载） | ❌ 忽略 |
| `ios_distribution.p12`、`*_base64.txt` | `convert.bat` 的产物 | ❌ 忽略 |

---

## 📋 步骤

### 步骤 0：生成私钥和 CSR

```bash
cd build/certificates

# 一次生成私钥 + 证书签名请求（Windows 上 Git Bash 自带 openssl）
openssl req -new -newkey rsa:2048 -nodes -keyout ios_distribution.key -out ios_distribution.csr -config csr.conf
```

私钥是不加密保存的（`-nodes`），所以别把这个目录放进网盘 / 同步盘。

### 步骤 1：在 Apple Developer 创建证书

1. **打开浏览器访问**：
   ```
   https://developer.apple.com/account/resources/certificates/list
   ```

2. **登录你的 Apple Developer 账号**

3. **点击左上角 `+` 按钮创建证书**

4. **选择证书类型**：
   - 如果是个人账号：选择 `Apple Distribution`
   - 如果是企业账号：选择 `iOS Distribution (Ad Hoc and In House)`

5. **上传 CSR 文件**：
   - 点击「Choose File」
   - 选择 `ios_distribution.csr` 文件
   - 点击「Continue」

6. **下载证书**：
   - 下载生成的 `ios_distribution.cer` 文件
   - 保存到 `build/certificates/` 目录

---

### 步骤 2：创建 App ID

1. **访问**：
   ```
   https://developer.apple.com/account/resources/identifiers/list
   ```

2. **点击 `+` 创建新的 App ID**

3. **选择 `App IDs` → `App`**

4. **填写信息**：
   - Description: `FinanceTracker`
   - Bundle ID: `com.finance.tracker.app`（与 `src/FinanceTracker.App/FinanceTracker.App.csproj` 里的 `ApplicationId` 保持一致）

5. **勾选需要的功能**（如推送通知等）

6. **点击「Continue」并注册**

---

### 步骤 3：创建 Provisioning Profile

1. **访问**：
   ```
   https://developer.apple.com/account/resources/profiles/list
   ```

2. **点击 `+` 创建新的 Profile**

3. **选择类型**：
   - 个人账号：选择 `Ad Hoc` (用于测试分发)
   - 企业账号：选择 `In House` (用于内部分发)

4. **选择 App ID**：
   - 选择刚才创建的 `com.finance.tracker.app`

5. **选择证书**：
   - 选择刚才创建的 Distribution 证书

6. **选择设备**（Ad Hoc 类型需要）：
   - 添加测试设备的 UDID
   - 或者选择所有设备（如果有企业账号）

7. **填写 Profile 名称**：
   - 名称：`FinanceTracker Distribution`

8. **下载 Profile**：
   - 下载 `.mobileprovision` 文件，重命名为 `FinanceTracker_Distribution.mobileprovision`
   - 保存到 `build/certificates/` 目录

---

### 步骤 4：转换证书格式

把下载的证书和私钥合成 `.p12`，并做 Base64 编码（GitHub Secrets 只能放文本）。

**Windows：直接运行脚本**

```bat
cd build\certificates
convert.bat
```

脚本会依次做：证书转 PEM → 合成 `.p12` → 把 `.p12` 和描述文件各编码成 `certificate_base64.txt` / `profile_base64.txt`。

- **`.p12` 的导出密码**：默认现场生成一个随机密码，**只在结束时显示一次，不写进任何文件**，请当场把它填进 GitHub Secret `APPLE_CERTIFICATE_PASSWORD`。想自己定密码的话，运行前先设环境变量：`set P12_PASSWORD=你自己定的强密码`（脚本不会回显它）。
- **openssl 在哪**：优先用环境变量 `OPENSSL_BIN`（openssl.exe 的完整路径），其次是 PATH 里的 `openssl`，最后是 Git for Windows 自带的那份。

**macOS / Linux / Git Bash：手动执行**

```bash
cd build/certificates

# 将 .cer 转换为 PEM 格式
openssl x509 -in ios_distribution.cer -inform DER -out ios_distribution.pem -outform PEM

# 生成随机导出密码（也可以自己定一个）。密码经环境变量传给 openssl，不会出现在命令行里
export P12_PASSWORD="$(openssl rand -hex 16)"

# 合并证书和私钥为 .p12 文件
openssl pkcs12 -export -inkey ios_distribution.key -in ios_distribution.pem -out ios_distribution.p12 -passout env:P12_PASSWORD

# 记下这个密码，稍后填进 GitHub Secret APPLE_CERTIFICATE_PASSWORD；不要写进文件或提交
echo "$P12_PASSWORD"
```

---

### 步骤 5：Base64 编码文件

用了 `convert.bat` 的话这一步已经完成，可以直接跳到步骤 6。手动执行：

```bash
# 编码 .p12 证书
base64 -i ios_distribution.p12 > certificate_base64.txt
cat certificate_base64.txt | clip  # 复制到剪贴板（macOS 用 pbcopy）

# 编码 .mobileprovision 文件
base64 -i FinanceTracker_Distribution.mobileprovision > profile_base64.txt
cat profile_base64.txt | clip  # 复制到剪贴板（macOS 用 pbcopy）
```

---

### 步骤 6：配置 GitHub Secrets 和变量

打开你的 GitHub 仓库：

```
https://github.com/<你的用户名>/<仓库名>
```

进入 `Settings` → `Secrets and variables` → `Actions`

添加以下 Secrets：

| Secret 名称 | 值 |
|-------------|-----|
| `APPLE_CERTIFICATE` | 粘贴 `certificate_base64.txt` 的内容 |
| `APPLE_CERTIFICATE_PASSWORD` | 导出 `.p12` 时用的密码（步骤 4：`convert.bat` 显示的随机密码，或你设置的 `P12_PASSWORD`） |
| `APPLE_PROVISIONING_PROFILE` | 粘贴 `profile_base64.txt` 的内容 |
| `APPLE_PROVISIONING_PROFILE_NAME` | `FinanceTracker Distribution` |
| `PGYER_API_KEY` | 蒲公英后台「API 信息」中的 API Key（仅配置在 GitHub Secrets，切勿写入仓库） |
| `PGYER_INSTALL_PASSWORD` | **可选**。蒲公英应用的安装密码（自己定；不设就是公开安装；仓库是公开的，所以只放 Secret，不要写进代码或文档） |

另外还需要 `CLOUD_API_BASE_URL`（打出的 App 连哪台云端服务器）：在同一页面添加为 **Secret 或 Variable**（二选一，同名时 Secret 优先），值形如 `https://api.example.com/`。仓库不内置线上域名，没配的话工作流会在第一步报错。

---

## 🔁 更换 `.p12` 密码 / 轮换证书

1. 重新运行 `convert.bat`（或步骤 4 的手动命令），用新的随机密码重新导出 `.p12`
2. 更新 GitHub Secrets：`APPLE_CERTIFICATE`、`APPLE_CERTIFICATE_PASSWORD`
3. 删除本地旧的 `.p12` 和 `*_base64.txt`

---

## 🎯 快速参考

### 需要访问的 Apple 网站

| 功能 | 网址 |
|------|------|
| 证书管理 | https://developer.apple.com/account/resources/certificates/list |
| App ID 管理 | https://developer.apple.com/account/resources/identifiers/list |
| Profile 管理 | https://developer.apple.com/account/resources/profiles/list |
| 设备管理 | https://developer.apple.com/account/resources/devices/list |

---

## ⚠️ 注意事项

1. **私钥安全**：`ios_distribution.key` 文件非常重要，不要泄露或丢失
2. **密码不入库**：任何密码（`.p12`、蒲公英安装密码（如果设置了）、API Key）都不要写进仓库——包括脚本、文档、Issue、PR 描述；仓库是公开的
3. **证书有效期**：Distribution 证书有效期为 1 年，需要定期更新
4. **Profile 有效期**：Provisioning Profile 有效期为 1 年
5. **Bundle ID 必须匹配**：Profile 中的 App ID 必须与项目的 `ApplicationId` 一致
